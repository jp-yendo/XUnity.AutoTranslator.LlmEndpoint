using System;
using System.Collections;
using XUnity.AutoTranslator.LlmEndpoint.Backends;
using XUnity.AutoTranslator.LlmEndpoint.Configuration;
using XUnity.AutoTranslator.LlmEndpoint.Dispatch;
using XUnity.AutoTranslator.LlmEndpoint.Logging;
using XUnity.AutoTranslator.LlmEndpoint.Prompts;
using XUnity.AutoTranslator.Plugin.Core.Endpoints;

namespace XUnity.AutoTranslator.LlmEndpoint
{
    public sealed class LlmEndpoint : ITranslateEndpoint
    {
        private EndpointLogger logger;
        private TranslationDispatcher dispatcher;

        public string Id { get { return "LlmEndpoint"; } }
        public string FriendlyName { get { return "LLM Translation Endpoint"; } }
        public int MaxConcurrency { get { return LlmSettings.EndpointMaxConcurrency; } }
        public int MaxTranslationsPerRequest { get { return 1; } }

        public void Initialize(IInitializationContext context)
        {
            try
            {
                LlmSettings settings = LlmSettings.Load(context);
                logger = new EndpointLogger(
                   settings.LogLevel,
                   settings.LogFile,
                   settings.ApiKey);
                ProfileRegistry registry = new ProfileRegistry();
                TranslationProfile profile = registry.Resolve(settings.Model);
                ILlmBackend backend = BackendFactory.Create(settings, logger);
                dispatcher = new TranslationDispatcher(settings, logger, profile, backend);
                logger.Info("Initialized backend " + settings.Backend + " with model profile " + profile.Id + ".");
            }
            catch (EndpointInitializationException)
            {
                throw;
            }
            catch (Exception ex)
            {
                throw new EndpointInitializationException("Failed to initialize [LlmEndpoint].", ex);
            }
        }

        public IEnumerator Translate(ITranslationContext context)
        {
            PendingOperation operation = null;
            Exception enqueueError = null;
            try
            {
                operation = dispatcher.Enqueue(context);
            }
            catch (Exception ex)
            {
                enqueueError = ex;
            }

            if (enqueueError != null)
            {
                if (logger != null) logger.Error("Failed to queue the LLM translation.", enqueueError);
                CompleteOriginal(context);
                yield break;
            }

            while (!operation.IsComplete) yield return null;

            if (operation.HasFailures) logger.Warn("One or more translations failed; returning the original text.");
            string[] results = operation.GetResults(true);
            if (results.Length == 1) context.Complete(results[0]);
            else context.Complete(results);
        }

        private static void CompleteOriginal(ITranslationContext context)
        {
            string[] originals = context.UntranslatedTexts;
            if (originals != null && originals.Length > 1)
            {
                context.Complete(originals);
            }
            else if (originals != null && originals.Length == 1)
            {
                context.Complete(originals[0] ?? string.Empty);
            }
            else
            {
                context.Complete(context.UntranslatedText ?? string.Empty);
            }
        }
    }
}
