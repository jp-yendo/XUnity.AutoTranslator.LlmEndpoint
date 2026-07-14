using System;

namespace XUnity.AutoTranslator.LlmEndpoint.Backends
{
    internal static class ProviderEndpoint
    {
        public static Uri OllamaChat(string endpointUrl)
        {
            UriBuilder builder = CreateBuilder(endpointUrl);
            builder.Path = builder.Path.TrimEnd('/') + "/";
            return new Uri(builder.Uri, "api/chat");
        }

        public static Uri OpenAiChatCompletions(string endpointUrl)
        {
            UriBuilder builder = CreateBuilder(endpointUrl);
            builder.Path = builder.Path.TrimEnd('/') + "/";
            return new Uri(builder.Uri, "chat/completions");
        }

        public static Uri AnthropicMessages(string endpointUrl)
        {
            UriBuilder builder = CreateBuilder(endpointUrl);
            builder.Path = builder.Path.TrimEnd('/') + "/";
            return new Uri(builder.Uri, "v1/messages");
        }

        private static UriBuilder CreateBuilder(string endpointUrl)
        {
            UriBuilder builder = new UriBuilder(new Uri(endpointUrl, UriKind.Absolute));
            builder.Query = string.Empty;
            builder.Fragment = string.Empty;
            return builder;
        }
    }
}
