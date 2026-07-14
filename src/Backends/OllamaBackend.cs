using System;
using System.Collections.Generic;
using XUnity.AutoTranslator.LlmEndpoint.Configuration;
using XUnity.AutoTranslator.LlmEndpoint.Logging;
using XUnity.AutoTranslator.LlmEndpoint.Prompts;
using XUnity.AutoTranslator.LlmEndpoint.Runtime;
using XUnity.AutoTranslator.LlmEndpoint.Serialization;
using XUnity.AutoTranslator.LlmEndpoint.Utilities;

namespace XUnity.AutoTranslator.LlmEndpoint.Backends
{
    internal sealed class OllamaBackend : LlmBackendBase
    {
        private readonly HttpTransport transport;
        private readonly Uri chatEndpoint;

        public OllamaBackend(LlmSettings settings, EndpointLogger logger) : base(settings, logger)
        {
            transport = new HttpTransport(settings.MaxParallelRequests, ProductInfo.UserAgent);
            chatEndpoint = ProviderEndpoint.OllamaChat(settings.EndpointUrl);
        }

        protected override string GenerateOnce(PromptEnvelope prompt)
        {
            try
            {
                HttpResult result = transport.PostJson(chatEndpoint, BuildHeaders(), BuildBody(prompt));
                if (result.Status < 200 || result.Status >= 300)
                {
                    throw new BackendException(
                       BuildErrorMessage(result.Status, result.Body),
                       IsTransientStatus(result.Status),
                       result.RetryAfterMs,
                       result.Status);
                }
                return ParseSuccess(result.Body);
            }
            catch (HttpTransportException ex)
            {
                throw new BackendException("Ollama transport failed: " + ex.Message, ex, ex.Transient);
            }
            catch (BackendException)
            {
                throw;
            }
            catch (Exception ex)
            {
                throw new BackendException("Ollama response processing failed: " + ex.Message, ex, false);
            }
        }

        private IEnumerable<KeyValuePair<string, string>> BuildHeaders()
        {
            List<KeyValuePair<string, string>> headers = new List<KeyValuePair<string, string>>();
            if (!StringUtil.IsBlank(Settings.ApiKey))
            {
                headers.Add(new KeyValuePair<string, string>("Authorization", "Bearer " + Settings.ApiKey));
            }
            return headers;
        }

        private string BuildBody(PromptEnvelope prompt)
        {
            Dictionary<string, object> root = Object();
            root["model"] = Settings.Model;
            root["stream"] = false;

            List<object> messages = new List<object>();
            if (!StringUtil.IsBlank(prompt.SystemMessage))
            {
                Dictionary<string, object> system = Object();
                system["role"] = "system";
                system["content"] = prompt.SystemMessage;
                messages.Add(system);
            }
            Dictionary<string, object> user = Object();
            user["role"] = "user";
            user["content"] = prompt.UserMessage;
            messages.Add(user);
            root["messages"] = messages;

            return MiniJson.Serialize(root);
        }

        private static string ParseSuccess(string body)
        {
            Dictionary<string, object> root;
            string error;
            if (!MiniJson.TryDeserializeObject(body, out root, out error))
            {
                throw new BackendException("Ollama returned an invalid JSON envelope: " + error, true, 0);
            }

            string providerError = MiniJson.GetString(root, "error");
            if (!StringUtil.IsBlank(providerError))
            {
                throw new BackendException("Ollama returned an error in a successful HTTP response.", true, 0);
            }

            Dictionary<string, object> message = MiniJson.GetObject(root, "message");
            string content = MiniJson.GetString(message, "content");
            if (StringUtil.IsBlank(content))
            {
                throw new BackendException("Ollama response did not contain message.content.", true, 0);
            }
            return content;
        }

        private static string BuildErrorMessage(int status, string body)
        {
            bool hasProviderError = false;
            Dictionary<string, object> root;
            string parseError;
            if (MiniJson.TryDeserializeObject(body, out root, out parseError))
            {
                hasProviderError = !StringUtil.IsBlank(MiniJson.GetString(root, "error"));
            }
            return hasProviderError
               ? "Ollama request failed with HTTP status " + status + " and a provider error."
               : "Ollama request failed with HTTP status " + status + ".";
        }

        private static Dictionary<string, object> Object()
        {
            return new Dictionary<string, object>(StringComparer.Ordinal);
        }
    }
}
