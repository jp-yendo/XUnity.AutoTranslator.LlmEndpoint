using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Threading;
using XUnity.AutoTranslator.LlmEndpoint.Backends;
using XUnity.AutoTranslator.LlmEndpoint.Configuration;
using XUnity.AutoTranslator.LlmEndpoint.Logging;
using XUnity.AutoTranslator.LlmEndpoint.Prompts;
using XUnity.AutoTranslator.LlmEndpoint.Utilities;
using XUnity.AutoTranslator.Plugin.Core.Endpoints;

namespace XUnity.AutoTranslator.LlmEndpoint.Dispatch
{
    internal sealed class TranslationDispatcher
    {
        private readonly object sync = new object();
        private readonly List<PendingItem> queue = new List<PendingItem>();
        private readonly LlmSettings settings;
        private readonly EndpointLogger logger;
        private readonly TranslationProfile profile;
        private readonly ILlmBackend backend;
        private readonly Semaphore requestSlots;
        private readonly Thread dispatcherThread;

        public TranslationDispatcher(
           LlmSettings settings,
           EndpointLogger logger,
           TranslationProfile profile,
           ILlmBackend backend)
        {
            this.settings = settings;
            this.logger = logger;
            this.profile = profile;
            this.backend = backend;
            requestSlots = new Semaphore(settings.MaxParallelRequests, settings.MaxParallelRequests);
            dispatcherThread = new Thread(DispatchLoop);
            dispatcherThread.Name = "XUnity LLM translation dispatcher";
            dispatcherThread.IsBackground = true;
            dispatcherThread.Start();
        }

        public PendingOperation Enqueue(ITranslationContext context)
        {
            string[] sourceTexts = context.UntranslatedTexts;
            if (sourceTexts == null || sourceTexts.Length == 0)
            {
                sourceTexts = new string[] { context.UntranslatedText ?? string.Empty };
            }
            string[] originals = new string[sourceTexts.Length];
            for (int i = 0; i < sourceTexts.Length; i++) originals[i] = sourceTexts[i] ?? string.Empty;

            PendingOperation operation = new PendingOperation(originals);
            DateTime now = DateTime.UtcNow;
            string sourceLanguage = context.SourceLanguage;
            string targetLanguage = context.DestinationLanguage;

            int queuedCount = 0;
            int queueSize = 0;
            List<string> passedThrough = null;
            lock (sync)
            {
                for (int i = 0; i < originals.Length; i++)
                {
                    // Text that needs no translation (numbers, timers, FPS, versions,
                    // single Latin letters, ...) is completed with its original value and
                    // never enters the queue or reaches the backend.
                    if (PassthroughFilter.ShouldPassthrough(originals[i]))
                    {
                        operation.CompleteItem(i, originals[i]);
                        if (logger.IsEnabled(LogLevel.Debug))
                        {
                            if (passedThrough == null) passedThrough = new List<string>();
                            passedThrough.Add(originals[i]);
                        }
                        continue;
                    }

                    PendingItem item = new PendingItem();
                    item.Operation = operation;
                    item.Index = i;
                    item.Id = "t_" + Guid.NewGuid().ToString("N");
                    item.Source = originals[i];
                    item.SourceLanguage = sourceLanguage;
                    item.TargetLanguage = targetLanguage;
                    item.EnqueuedUtc = now;
                    queue.Add(item);
                    queuedCount++;
                }
                queueSize = queue.Count;
                if (queuedCount > 0) Monitor.PulseAll(sync);
            }
            if (passedThrough != null)
            {
                for (int i = 0; i < passedThrough.Count; i++)
                {
                    logger.Debug("Passthrough item (source=" + passedThrough[i] + ").");
                }
            }
            if (queuedCount > 0 && logger.IsEnabled(LogLevel.Debug))
            {
                logger.Debug("Queued " + queuedCount + " item(s) (queue_size=" + queueSize + ").");
            }
            return operation;
        }

        private void DispatchLoop()
        {
            while (true)
            {
                List<PendingItem> batch;
                try
                {
                    batch = WaitForBatch();
                    requestSlots.WaitOne();
                    ThreadPool.QueueUserWorkItem(delegate
                    {
                        try
                        {
                            ProcessBatch(batch);
                        }
                        catch (ThreadAbortException)
                        {
                            // Host shutdown aborts the worker thread. Record it as a normal
                            // stop and let the abort terminate the thread.
                            logger.Debug("Batch processing stopped due to host shutdown.");
                            throw;
                        }
                        catch (Exception ex)
                        {
                            logger.Error("Unexpected batch processing failure.", ex);
                            FailItems(batch, "Unexpected endpoint processing failure.");
                        }
                        finally
                        {
                            requestSlots.Release();
                        }
                    });
                }
                catch (ThreadAbortException)
                {
                    // Host shutdown aborts the dispatcher thread. Record it as a normal
                    // stop and let the abort terminate the thread.
                    logger.Debug("Dispatcher thread stopped due to host shutdown.");
                    throw;
                }
                catch (Exception ex)
                {
                    logger.Error("The dispatcher loop recovered from an error.", ex);
                    Thread.Sleep(LlmSettings.DispatcherRecoveryDelayMs);
                }
            }
        }

        private List<PendingItem> WaitForBatch()
        {
            List<PendingItem> batch;
            int queueSize;
            lock (sync)
            {
                while (queue.Count == 0) Monitor.Wait(sync);
                if (profile.DispatchMode == DispatchMode.Batch && settings.BatchIntervalMs > 0)
                {
                    DateTime flushAt = queue[0].EnqueuedUtc.AddMilliseconds(settings.BatchIntervalMs);
                    while (queue.Count < settings.MaxBatchSize)
                    {
                        int waitMs = (int)(flushAt - DateTime.UtcNow).TotalMilliseconds;
                        if (waitMs <= 0) break;
                        Monitor.Wait(sync, waitMs);
                    }
                }
                batch = TakeCompatibleBatch();
                queueSize = queue.Count;
            }
            if (batch.Count > 0 && logger.IsEnabled(LogLevel.Debug))
            {
                logger.Debug("Dequeued " + batch.Count + " item(s) for a batch (queue_size=" + queueSize + ").");
            }
            return batch;
        }

        private List<PendingItem> TakeCompatibleBatch()
        {
            List<PendingItem> result = new List<PendingItem>();
            while (queue.Count > 0)
            {
                PendingItem first = queue[0];
                int limit = profile.DispatchMode == DispatchMode.Single ? 1 : settings.MaxBatchSize;
                for (int i = 0; i < queue.Count && result.Count < limit;)
                {
                    PendingItem candidate = queue[i];
                    bool compatible = string.Equals(first.SourceLanguage, candidate.SourceLanguage, StringComparison.OrdinalIgnoreCase) &&
                       string.Equals(first.TargetLanguage, candidate.TargetLanguage, StringComparison.OrdinalIgnoreCase);
                    if (!compatible)
                    {
                        i++;
                        continue;
                    }

                    List<PendingItem> proposed = new List<PendingItem>(result);
                    proposed.Add(candidate);
                    int estimatedTokens = EstimateRequestTokens(proposed);
                    if (RequestBudgetPolicy.CanAddItem(result.Count, estimatedTokens, settings.MaxRequestTokens))
                    {
                        result.Add(candidate);
                        queue.RemoveAt(i);
                        continue;
                    }
                    i++;
                }
                if (result.Count > 0) break;
            }
            return result;
        }

        private int EstimateRequestTokens(List<PendingItem> items)
        {
            List<PromptItem> promptItems = BuildPromptItems(items);
            PromptEnvelope prompt = profile.BuildPrompt(promptItems, BuildPromptContext(items[0]), 0);
            return PromptBudgetEstimator.EstimateUpperBoundTokens(prompt, promptItems);
        }

        private int CurrentQueueSize()
        {
            lock (sync) return queue.Count;
        }

        private static int GetTextCharacterCount(List<PendingItem> items)
        {
            int count = 0;
            for (int i = 0; i < items.Count; i++) count += items[i].Source.Length;
            return count;
        }

        private void ProcessBatch(List<PendingItem> items)
        {
            ProcessWithRecovery(items);
        }

        private void ProcessWithRecovery(List<PendingItem> items)
        {
            List<PendingItem> unresolved = new List<PendingItem>(items);
            bool logItems = settings.LogTranslationItems && logger.IsEnabled(LogLevel.Info);
            for (int attempt = 0; attempt < profile.FormatAttemptCount && unresolved.Count > 0; attempt++)
            {
                List<PromptItem> promptItems = BuildPromptItems(unresolved);
                PromptContext promptContext = BuildPromptContext(unresolved[0]);
                string requestId = "r_" + Guid.NewGuid().ToString("N");
                Stopwatch stopwatch = null;
                try
                {
                    PromptEnvelope prompt = profile.BuildPrompt(promptItems, promptContext, attempt);
                    int textCharacters = GetTextCharacterCount(unresolved);
                    int estimatedTokens = PromptBudgetEstimator.EstimateUpperBoundTokens(prompt, promptItems);
                    logger.BatchActivity(
                       settings.LogBatchActivity,
                       "Batch request started (request_id=" + requestId +
                       ", items=" + unresolved.Count +
                       ", text_characters=" + textCharacters +
                       ", estimated_tokens=" + estimatedTokens +
                       // The batch items were already removed from the queue, so add them
                       // back in to report the total still pending including this request.
                       ", queue_size=" + (CurrentQueueSize() + unresolved.Count) +
                       ", format_attempt=" + (attempt + 1) + ").");
                    if (logItems)
                    {
                        for (int i = 0; i < unresolved.Count; i++)
                        {
                            logger.Info("Request item (request_id=" + requestId +
                               ", id=" + unresolved[i].Id +
                               ", source=" + unresolved[i].Source + ").");
                        }
                    }
                    stopwatch = Stopwatch.StartNew();
                    string output = backend.Generate(prompt);
                    stopwatch.Stop();
                    logger.BatchActivity(
                       settings.LogBatchActivity,
                       "Batch response received (request_id=" + requestId +
                       ", items=" + unresolved.Count +
                       ", response_characters=" + output.Length +
                       ", elapsed=" + DurationFormatter.Format(stopwatch.Elapsed) +
                       ", queue_size=" + CurrentQueueSize() + ").");
                    ProfileParseResult parsed = profile.ParseResponse(output, promptItems, attempt);
                    List<PendingItem> next = new List<PendingItem>();
                    for (int i = 0; i < unresolved.Count; i++)
                    {
                        PendingItem item = unresolved[i];
                        string translated;
                        if (parsed.Translations.TryGetValue(item.Id, out translated) && !StringUtil.IsBlank(translated))
                        {
                            item.Operation.CompleteItem(item.Index, translated);
                            if (logItems)
                            {
                                logger.Info("Result item (request_id=" + requestId +
                                   ", id=" + item.Id +
                                   ", translation=" + translated + ").");
                            }
                        }
                        else
                        {
                            next.Add(item);
                        }
                    }
                    unresolved = next;
                    if (unresolved.Count > 0 && !parsed.IsFormatValid)
                    {
                        // Intermediate, recoverable state (a later attempt or a split may
                        // still resolve these), so keep it at Debug; the terminal give-up
                        // below reports at Warn.
                        logger.Debug("Model response was incomplete or malformed (request_id=" + requestId +
                           ", unresolved=" + unresolved.Count +
                           ", format_attempt=" + (attempt + 1) + ").");
                    }
                }
                catch (BackendException ex)
                {
                    if (stopwatch != null && stopwatch.IsRunning) stopwatch.Stop();
                    string elapsed = stopwatch == null ? "not-started" : DurationFormatter.Format(stopwatch.Elapsed);
                    logger.Error(
                       "Batch request failed (request_id=" + requestId + ", elapsed=" + elapsed + ").",
                       ex);
                    FailItems(unresolved, "LLM backend request failed: " + ex.Message);
                    return;
                }
            }

            if (unresolved.Count == 0) return;
            if (profile.DispatchMode == DispatchMode.Batch && unresolved.Count > 1)
            {
                int middle = unresolved.Count / 2;
                ProcessWithRecovery(unresolved.GetRange(0, middle));
                ProcessWithRecovery(unresolved.GetRange(middle, unresolved.Count - middle));
                return;
            }
            // Terminal give-up: format recovery and splitting are exhausted. These items
            // fall back to their original text, so report it once at Warn.
            logger.Warn("Giving up on " + unresolved.Count +
               " item(s) that could not be matched to a model response; returning original text.");
            FailItems(unresolved, "The model response could not be matched to the requested translation.");
        }

        private PromptContext BuildPromptContext(PendingItem first)
        {
            PromptContext context = new PromptContext();
            context.SourceLanguage = first.SourceLanguage;
            context.TargetLanguage = first.TargetLanguage;
            context.AppSummary = settings.AppSummary;
            context.AdditionalInstructions = settings.AdditionalInstructions;
            return context;
        }

        private static List<PromptItem> BuildPromptItems(List<PendingItem> items)
        {
            List<PromptItem> result = new List<PromptItem>();
            for (int i = 0; i < items.Count; i++)
            {
                PromptItem promptItem = new PromptItem();
                promptItem.Id = items[i].Id;
                promptItem.Text = items[i].Source;
                result.Add(promptItem);
            }
            return result;
        }

        private static void FailItems(List<PendingItem> items, string error)
        {
            for (int i = 0; i < items.Count; i++) items[i].Operation.FailItem(items[i].Index, error);
        }
    }
}
