using System;
using System.Collections.Generic;
using System.Text;
using XUnity.AutoTranslator.LlmEndpoint.Configuration;
using XUnity.AutoTranslator.LlmEndpoint.Logging;
using XUnity.AutoTranslator.LlmEndpoint.Prompts;
using XUnity.AutoTranslator.LlmEndpoint.Runtime;
using XUnity.AutoTranslator.LlmEndpoint.Serialization;
using XUnity.AutoTranslator.LlmEndpoint.Utilities;

namespace XUnity.AutoTranslator.LlmEndpoint.Backends
{
    internal sealed class AnthropicBackend : LlmBackendBase
    {
        private readonly HttpTransport transport;
        private readonly Uri messagesEndpoint;

        public AnthropicBackend(LlmSettings settings, EndpointLogger logger) : base(settings, logger)
        {
            transport = new HttpTransport(settings.MaxParallelRequests, settings.RequestTimeoutMs, ProductInfo.UserAgent);
            messagesEndpoint = ProviderEndpoint.AnthropicMessages(settings.EndpointUrl);
        }

        protected override string GenerateOnce(PromptEnvelope prompt)
        {
            try
            {
                HttpResult result = transport.PostJson(messagesEndpoint, BuildHeaders(), BuildBody(prompt));
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
                throw new BackendException("Anthropic transport failed: " + ex.Message, ex, ex.Transient);
            }
            catch (BackendException)
            {
                throw;
            }
            catch (Exception ex)
            {
                throw new BackendException("Anthropic response processing failed: " + ex.Message, ex, false);
            }
        }

        private IEnumerable<KeyValuePair<string, string>> BuildHeaders()
        {
            List<KeyValuePair<string, string>> headers = new List<KeyValuePair<string, string>>();
            headers.Add(new KeyValuePair<string, string>("anthropic-version", LlmSettings.AnthropicApiVersion));
            if (!StringUtil.IsBlank(Settings.ApiKey))
            {
                headers.Add(new KeyValuePair<string, string>("x-api-key", Settings.ApiKey));
            }
            return headers;
        }

        private string BuildBody(PromptEnvelope prompt)
        {
            Dictionary<string, object> root = Object();
            root["model"] = Settings.Model;
            root["max_tokens"] = LlmSettings.AnthropicMaxOutputTokens;
            if (!StringUtil.IsBlank(prompt.SystemMessage)) root["system"] = prompt.SystemMessage;

            List<object> messages = new List<object>();
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
                throw new BackendException("Anthropic returned an invalid JSON envelope: " + error, true, 0);
            }

            string stopReason = MiniJson.GetString(root, "stop_reason");
            if (string.Equals(stopReason, "max_tokens", StringComparison.OrdinalIgnoreCase))
            {
                throw new BackendException("Anthropic stopped because the output token limit was reached.", false, 0);
            }
            if (string.Equals(stopReason, "refusal", StringComparison.OrdinalIgnoreCase))
            {
                throw new BackendException("The model refused the translation request.", false, 0);
            }

            List<object> content = MiniJson.GetArray(root, "content");
            StringBuilder builder = new StringBuilder();
            if (content != null)
            {
                for (int i = 0; i < content.Count; i++)
                {
                    Dictionary<string, object> block = content[i] as Dictionary<string, object>;
                    if (block == null || !string.Equals(MiniJson.GetString(block, "type"), "text", StringComparison.Ordinal))
                    {
                        continue;
                    }
                    string text = MiniJson.GetString(block, "text");
                    if (text != null) builder.Append(text);
                }
            }
            if (builder.Length == 0)
            {
                throw new BackendException("Anthropic response did not contain a text content block.", true, 0);
            }
            return builder.ToString();
        }

        private static string BuildErrorMessage(int status, string body)
        {
            string errorType = null;
            Dictionary<string, object> root;
            string parseError;
            if (MiniJson.TryDeserializeObject(body, out root, out parseError))
            {
                Dictionary<string, object> error = MiniJson.GetObject(root, "error");
                errorType = MiniJson.GetString(error, "type");
            }
            return StringUtil.IsBlank(errorType)
               ? "Anthropic request failed with HTTP status " + status + "."
               : "Anthropic request failed with HTTP status " + status + " (" + errorType + ").";
        }

        private static Dictionary<string, object> Object()
        {
            return new Dictionary<string, object>(StringComparer.Ordinal);
        }
    }
}
