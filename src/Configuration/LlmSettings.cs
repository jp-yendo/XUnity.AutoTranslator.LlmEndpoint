using System;
using System.IO;
using XUnity.AutoTranslator.LlmEndpoint.Utilities;
using XUnity.AutoTranslator.Plugin.Core.Endpoints;

namespace XUnity.AutoTranslator.LlmEndpoint.Configuration
{
    internal sealed class LlmSettings
    {
        public const string Section = "LlmEndpoint";

        // Hard ceiling for the configurable concurrency-related settings.
        public const int EndpointMaxConcurrency = 50;
        // How many XUnity translations may be buffered in flight (ITranslateEndpoint.MaxConcurrency).
        // Kept small by default so that, on slow backends, queued items are processed well
        // within the host's per-translation timeout instead of waiting past it.
        public const int DefaultMaxConcurrency = 10;
        public const int RetryBaseDelayMs = 500;
        public const int RetryMaximumDelayMs = 30000;
        public const int DispatcherRecoveryDelayMs = 100;
        public const string AnthropicApiVersion = "2023-06-01";
        // Anthropic requires max_tokens. It is only an upper bound (billing is on actual
        // output), and every currently targeted model (Sonnet 5, Haiku 4.5, Opus 4.8)
        // allows at least this, so a generous fixed cap avoids truncating batch output.
        public const int AnthropicMaxOutputTokens = 8192;
        public const int DefaultBatchIntervalMs = 300;
        public const int DefaultRequestTimeoutMs = 300000;
        public const int DefaultLogRetentionDays = 7;
        public BackendKind Backend;
        public string EndpointUrl;
        public string ApiKey;
        public string Model;
        public int BatchIntervalMs;
        public int MaxBatchSize;
        public int MaxRequestTokens;
        public int MaxParallelRequests;
        public int MaxConcurrency;
        public int RequestTimeoutMs;
        public int RetryCount;
        public LogLevel LogLevel;
        public bool LogBatchActivity;
        public bool LogTranslationItems;
        public string LogFile;
        public int LogRetentionDays;
        public string AppSummary;
        public string AdditionalInstructions;

        public static LlmSettings Load(IInitializationContext context)
        {
            LlmSettings settings = new LlmSettings();
            string backendText = context.GetOrCreateSetting<string>(Section, "Backend", "Ollama");
            settings.Backend = StringUtil.ParseEnum<BackendKind>(backendText, BackendKind.Ollama);
            settings.EndpointUrl = context.GetOrCreateSetting<string>(Section, "EndpointUrl", DefaultEndpoint(settings.Backend));
            settings.ApiKey = context.GetOrCreateSetting<string>(Section, "ApiKey", string.Empty);
            if (StringUtil.IsBlank(settings.ApiKey)) settings.ApiKey = ReadDefaultApiKey(settings.Backend);
            settings.Model = context.GetOrCreateSetting<string>(Section, "Model", string.Empty);
            settings.BatchIntervalMs = context.GetOrCreateSetting<int>(
               Section,
               "BatchIntervalMs",
               DefaultBatchIntervalMs);
            settings.MaxBatchSize = context.GetOrCreateSetting<int>(Section, "MaxBatchSize", 5);
            settings.MaxRequestTokens = context.GetOrCreateSetting<int>(Section, "MaxRequestTokens", 8192);
            settings.MaxParallelRequests = context.GetOrCreateSetting<int>(Section, "MaxParallelRequests", 1);
            settings.MaxConcurrency = context.GetOrCreateSetting<int>(
               Section,
               "MaxConcurrency",
               DefaultMaxConcurrency);
            settings.RequestTimeoutMs = context.GetOrCreateSetting<int>(
               Section,
               "RequestTimeoutMs",
               DefaultRequestTimeoutMs);
            settings.RetryCount = context.GetOrCreateSetting<int>(Section, "RetryCount", 1);
            settings.LogLevel = StringUtil.ParseEnum<LogLevel>(
               context.GetOrCreateSetting<string>(Section, "LogLevel", "Info"),
               LogLevel.Info);
            settings.LogBatchActivity = context.GetOrCreateSetting<bool>(
               Section,
               "LogBatchActivity",
               true);
            settings.LogTranslationItems = context.GetOrCreateSetting<bool>(
               Section,
               "LogTranslationItems",
               false);
            settings.LogFile = ResolvePath(
               context.GetOrCreateSetting<string>(Section, "LogFile", string.Empty),
               context.TranslatorDirectory);
            settings.LogRetentionDays = context.GetOrCreateSetting<int>(
               Section,
               "LogRetentionDays",
               DefaultLogRetentionDays);
            settings.AppSummary = StringUtil.UnescapeSequences(
               context.GetOrCreateSetting<string>(Section, "AppSummary", string.Empty));
            settings.AdditionalInstructions = StringUtil.UnescapeSequences(
               context.GetOrCreateSetting<string>(Section, "AdditionalInstructions", string.Empty));
            settings.Validate();
            return settings;
        }

        private void Validate()
        {
            if (StringUtil.IsBlank(Model)) throw new EndpointInitializationException("[LlmEndpoint] Model is required.");
            Uri endpoint;
            if (!Uri.TryCreate(EndpointUrl, UriKind.Absolute, out endpoint) ||
               (endpoint.Scheme != Uri.UriSchemeHttp && endpoint.Scheme != Uri.UriSchemeHttps))
            {
                throw new EndpointInitializationException("[LlmEndpoint] EndpointUrl must be an absolute HTTP or HTTPS URL.");
            }
            RequireAtLeast("BatchIntervalMs", BatchIntervalMs, 0);
            RequireRange("MaxBatchSize", MaxBatchSize, 1, EndpointMaxConcurrency);
            RequireAtLeast("MaxRequestTokens", MaxRequestTokens, 1);
            RequireRange("MaxParallelRequests", MaxParallelRequests, 1, EndpointMaxConcurrency);
            RequireRange("MaxConcurrency", MaxConcurrency, 1, EndpointMaxConcurrency);
            RequireAtLeast("RequestTimeoutMs", RequestTimeoutMs, 0);
            RequireAtLeast("RetryCount", RetryCount, 0);
            RequireAtLeast("LogRetentionDays", LogRetentionDays, 0);
        }

        private static void RequireRange(string name, int value, int minimum, int maximum)
        {
            if (value < minimum || value > maximum)
            {
                throw new EndpointInitializationException(
                   "[LlmEndpoint] " + name + " must be between " + minimum + " and " + maximum + ".");
            }
        }

        private static void RequireAtLeast(string name, int value, int minimum)
        {
            if (value < minimum)
            {
                throw new EndpointInitializationException(
                   "[LlmEndpoint] " + name + " must be at least " + minimum + ".");
            }
        }

        private static string DefaultEndpoint(BackendKind backend)
        {
            if (backend == BackendKind.OpenAI) return "https://api.openai.com/v1";
            if (backend == BackendKind.Anthropic) return "https://api.anthropic.com";
            return "http://localhost:11434";
        }

        private static string ReadDefaultApiKey(BackendKind backend)
        {
            string variable = backend == BackendKind.OpenAI
               ? "OPENAI_API_KEY"
               : backend == BackendKind.Anthropic ? "ANTHROPIC_API_KEY" : "OLLAMA_API_KEY";
            return Environment.GetEnvironmentVariable(variable) ?? string.Empty;
        }

        private static string ResolvePath(string value, string baseDirectory)
        {
            if (StringUtil.IsBlank(value)) return string.Empty;
            string trimmed = value.Trim();
            return Path.IsPathRooted(trimmed) ? trimmed : Path.Combine(baseDirectory, trimmed);
        }
    }
}
