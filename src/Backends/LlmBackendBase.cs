using System;
using System.Threading;
using XUnity.AutoTranslator.LlmEndpoint.Configuration;
using XUnity.AutoTranslator.LlmEndpoint.Logging;
using XUnity.AutoTranslator.LlmEndpoint.Prompts;

namespace XUnity.AutoTranslator.LlmEndpoint.Backends
{
    internal abstract class LlmBackendBase : ILlmBackend
    {
        private static readonly object RandomLock = new object();
        private static readonly Random Random = new Random();
        protected readonly LlmSettings Settings;
        protected readonly EndpointLogger Logger;
        protected LlmBackendBase(LlmSettings settings, EndpointLogger logger)
        {
            Settings = settings;
            Logger = logger;
        }

        public string Generate(PromptEnvelope prompt)
        {
            BackendException lastError = null;
            for (int attempt = 0; attempt <= Settings.RetryCount; attempt++)
            {
                try
                {
                    return GenerateOnce(prompt);
                }
                catch (BackendException ex)
                {
                    if (!ex.IsTransient || attempt >= Settings.RetryCount) throw;
                    lastError = ex;
                    WaitBeforeRetry(attempt, ex.RetryAfterMs);
                }
            }
            throw lastError ?? new BackendException("The backend request failed.", false, 0);
        }

        protected abstract string GenerateOnce(PromptEnvelope prompt);

        protected static bool IsTransientStatus(int statusCode)
        {
            return statusCode == 408 || statusCode == 409 || statusCode == 425 ||
               statusCode == 429 || statusCode == 529 || statusCode >= 500;
        }

        private void WaitBeforeRetry(int attempt, int retryAfterMs)
        {
            int exponential = LlmSettings.RetryBaseDelayMs;
            for (int i = 0; i < attempt && exponential < LlmSettings.RetryMaximumDelayMs; i++)
            {
                exponential = Math.Min(exponential * 2, LlmSettings.RetryMaximumDelayMs);
            }
            int jitter;
            lock (RandomLock) jitter = Random.Next(0, Math.Max(1, LlmSettings.RetryBaseDelayMs / 2 + 1));
            int delay = Math.Max(retryAfterMs, exponential + jitter);
            Logger.Debug("Retrying an LLM request after " + delay + " ms.");
            if (delay > 0) Thread.Sleep(delay);
        }
    }
}
