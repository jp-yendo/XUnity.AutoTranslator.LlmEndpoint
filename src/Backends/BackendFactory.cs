using XUnity.AutoTranslator.LlmEndpoint.Configuration;
using XUnity.AutoTranslator.LlmEndpoint.Logging;

namespace XUnity.AutoTranslator.LlmEndpoint.Backends
{
    internal static class BackendFactory
    {
        public static ILlmBackend Create(LlmSettings settings, EndpointLogger logger)
        {
            if (settings.Backend == BackendKind.OpenAI) return new OpenAiBackend(settings, logger);
            if (settings.Backend == BackendKind.Anthropic) return new AnthropicBackend(settings, logger);
            return new OllamaBackend(settings, logger);
        }
    }
}
