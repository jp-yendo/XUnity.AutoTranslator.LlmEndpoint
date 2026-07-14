using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Threading;
using XUnity.AutoTranslator.LlmEndpoint.Backends;
using XUnity.AutoTranslator.LlmEndpoint.Configuration;
using XUnity.AutoTranslator.LlmEndpoint.Logging;
using XUnity.AutoTranslator.LlmEndpoint.Prompts;
using XUnity.AutoTranslator.LlmEndpoint.Text;
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
            UntranslatedTextInfo[] infos = context.UntranslatedTextInfos;
            DateTime now = DateTime.UtcNow;
            string sourceLanguage = context.SourceLanguage;
            string targetLanguage = context.DestinationLanguage;

            lock (sync)
            {
                for (int i = 0; i < originals.Length; i++)
                {
                    PendingItem item = new PendingItem();
                    item.Operation = operation;
                    item.Index = i;
                    item.Id = "t_" + Guid.NewGuid().ToString("N");
                    item.ProtectedText = TextProtector.Protect(originals[i]);
                    item.SourceLanguage = sourceLanguage;
                    item.TargetLanguage = targetLanguage;
                    item.EnqueuedUtc = now;
                    if (infos != null && i < infos.Length && infos[i] != null)
                    {
                        item.ContextBefore = CopyContextBefore(infos[i].ContextBefore);
                        item.ContextAfter = CopyContextAfter(infos[i].ContextAfter);
                    }
                    queue.Add(item);
                }
                Monitor.PulseAll(sync);
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
                catch (Exception ex)
                {
                    logger.Error("The dispatcher loop recovered from an error.", ex);
                    Thread.Sleep(LlmSettings.DispatcherRecoveryDelayMs);
                }
            }
        }

        private List<PendingItem> WaitForBatch()
        {
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
                return TakeCompatibleBatch();
            }
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

        private static int GetTextCharacterCount(List<PendingItem> items)
        {
            int count = 0;
            for (int i = 0; i < items.Count; i++) count += items[i].ProtectedText.Original.Length;
            return count;
        }

        private static int GetContextCharacterCount(List<PendingItem> items)
        {
            int count = 0;
            for (int i = 0; i < items.Count; i++)
            {
                count += GetCharacterCount(items[i].ContextBefore);
                count += GetCharacterCount(items[i].ContextAfter);
            }
            return count;
        }

        private static int GetCharacterCount(List<string> values)
        {
            int count = 0;
            if (values == null) return count;
            for (int i = 0; i < values.Count; i++) count += values[i] == null ? 0 : values[i].Length;
            return count;
        }

        private void ProcessBatch(List<PendingItem> items)
        {
            ProcessWithRecovery(items);
        }

        private void ProcessWithRecovery(List<PendingItem> items)
        {
            List<PendingItem> unresolved = new List<PendingItem>(items);
            for (int attempt = 0; attempt < profile.FormatAttemptCount && unresolved.Count > 0; attempt++)
            {
                List<PromptItem> promptItems = BuildPromptItems(unresolved);
                PromptContext promptContext = BuildPromptContext(unresolved[0]);
                string requestId = "r_" + Guid.NewGuid().ToString("N");
                Stopwatch stopwatch = null;
                try
                {
                    PromptEnvelope prompt = profile.BuildPrompt(promptItems, promptContext, attempt);
                    prompt.MaxOutputTokens = PromptBudgetEstimator.EstimateOutputUpperBoundTokens(promptItems);
                    int textCharacters = GetTextCharacterCount(unresolved);
                    int contextCharacters = GetContextCharacterCount(unresolved);
                    int estimatedTokens = PromptBudgetEstimator.EstimateUpperBoundTokens(prompt, promptItems);
                    logger.BatchActivity(
                       settings.LogBatchActivity,
                       "Batch request started (request_id=" + requestId +
                       ", backend=" + settings.Backend +
                       ", profile=" + profile.Id +
                       ", items=" + unresolved.Count +
                       ", text_characters=" + textCharacters +
                       ", context_characters=" + contextCharacters +
                       ", estimated_tokens=" + estimatedTokens +
                       ", max_request_tokens=" + settings.MaxRequestTokens +
                       ", format_attempt=" + (attempt + 1) + ").");
                    stopwatch = Stopwatch.StartNew();
                    string output = backend.Generate(prompt);
                    stopwatch.Stop();
                    logger.BatchActivity(
                       settings.LogBatchActivity,
                       "Batch response received (request_id=" + requestId +
                       ", items=" + unresolved.Count +
                       ", response_characters=" + output.Length +
                       ", elapsed=" + DurationFormatter.Format(stopwatch.Elapsed) + ").");
                    ProfileParseResult parsed = profile.ParseResponse(output, promptItems, attempt);
                    List<PendingItem> next = new List<PendingItem>();
                    for (int i = 0; i < unresolved.Count; i++)
                    {
                        PendingItem item = unresolved[i];
                        string translated;
                        if (parsed.Translations.TryGetValue(item.Id, out translated) && !StringUtil.IsBlank(translated))
                        {
                            string restored;
                            string restoreError;
                            if (item.ProtectedText.TryRestore(translated, out restored, out restoreError))
                            {
                                item.Operation.CompleteItem(item.Index, restored);
                                logger.Debug("Translation item completed (" + item.Id + ").");
                            }
                            else
                            {
                                logger.Warn("Rejected a translation because protected tokens were not preserved.");
                                next.Add(item);
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
                        logger.Warn("The model response was incomplete or malformed; unresolved items will be retried.");
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
            FailItems(unresolved, "The model response could not be matched to the requested translation.");
        }

        private PromptContext BuildPromptContext(PendingItem first)
        {
            PromptContext context = new PromptContext();
            context.SourceLanguage = first.SourceLanguage;
            context.TargetLanguage = first.TargetLanguage;
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
                promptItem.Text = items[i].ProtectedText.Value;
                promptItem.ContextBefore = items[i].ContextBefore;
                promptItem.ContextAfter = items[i].ContextAfter;
                result.Add(promptItem);
            }
            return result;
        }

        private static void FailItems(List<PendingItem> items, string error)
        {
            for (int i = 0; i < items.Count; i++) items[i].Operation.FailItem(items[i].Index, error);
        }

        private static List<string> CopyContextBefore(List<string> source)
        {
            List<string> result = new List<string>();
            if (source == null) return result;
            int start = Math.Max(0, source.Count - LlmSettings.MaxContextItems);
            for (int i = start; i < source.Count; i++) result.Add(source[i] ?? string.Empty);
            return result;
        }

        private static List<string> CopyContextAfter(List<string> source)
        {
            List<string> result = new List<string>();
            if (source == null) return result;
            int count = Math.Min(source.Count, LlmSettings.MaxContextItems);
            for (int i = 0; i < count; i++) result.Add(source[i] ?? string.Empty);
            return result;
        }
    }
}
