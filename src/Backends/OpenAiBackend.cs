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
    internal sealed class OpenAiBackend : LlmBackendBase
    {
        private readonly HttpTransport transport;
        private readonly Uri chatCompletionsEndpoint;

        public OpenAiBackend(LlmSettings settings, EndpointLogger logger) : base(settings, logger)
        {
            transport = new HttpTransport(settings.MaxParallelRequests, ProductInfo.UserAgent);
            chatCompletionsEndpoint = ProviderEndpoint.OpenAiChatCompletions(settings.EndpointUrl);
        }

        protected override string GenerateOnce(PromptEnvelope prompt)
        {
            try
            {
                HttpResult result = transport.PostJson(chatCompletionsEndpoint, BuildHeaders(), BuildBody(prompt));
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
                throw new BackendException("OpenAI transport failed: " + ex.Message, ex, ex.Transient);
            }
            catch (BackendException)
            {
                throw;
            }
            catch (Exception ex)
            {
                throw new BackendException("OpenAI response processing failed: " + ex.Message, ex, false);
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
                throw new BackendException("OpenAI returned an invalid JSON envelope: " + error, true, 0);
            }

            List<object> choices = MiniJson.GetArray(root, "choices");
            Dictionary<string, object> choice = choices != null && choices.Count > 0
               ? choices[0] as Dictionary<string, object>
               : null;
            if (choice == null)
            {
                throw new BackendException("OpenAI response did not contain a completion choice.", true, 0);
            }

            string finishReason = MiniJson.GetString(choice, "finish_reason");
            if (string.Equals(finishReason, "length", StringComparison.OrdinalIgnoreCase))
            {
                throw new BackendException("OpenAI stopped because the provider output limit was reached.", false, 0);
            }
            if (string.Equals(finishReason, "content_filter", StringComparison.OrdinalIgnoreCase))
            {
                throw new BackendException("OpenAI stopped because the response was filtered.", false, 0);
            }

            Dictionary<string, object> message = MiniJson.GetObject(choice, "message");
            if (message == null)
            {
                throw new BackendException("OpenAI response did not contain an assistant message.", true, 0);
            }
            if (!StringUtil.IsBlank(MiniJson.GetString(message, "refusal")))
            {
                throw new BackendException("The model refused the translation request.", false, 0);
            }

            string content = GetMessageText(message);
            if (StringUtil.IsBlank(content))
            {
                throw new BackendException("OpenAI response did not contain text content.", true, 0);
            }
            return content;
        }

        private static string GetMessageText(Dictionary<string, object> message)
        {
            object content;
            if (!message.TryGetValue("content", out content) || content == null) return null;
            string text = content as string;
            if (text != null) return text;

            List<object> parts = content as List<object>;
            if (parts == null) return null;
            StringBuilder builder = new StringBuilder();
            for (int i = 0; i < parts.Count; i++)
            {
                Dictionary<string, object> part = parts[i] as Dictionary<string, object>;
                if (part == null) continue;
                string type = MiniJson.GetString(part, "type");
                if (!string.Equals(type, "text", StringComparison.Ordinal) &&
                   !string.Equals(type, "output_text", StringComparison.Ordinal))
                {
                    continue;
                }
                string partText = MiniJson.GetString(part, "text");
                if (partText != null) builder.Append(partText);
            }
            return builder.ToString();
        }

        private static string BuildErrorMessage(int status, string body)
        {
            string errorType = null;
            string errorCode = null;
            Dictionary<string, object> root;
            string parseError;
            if (MiniJson.TryDeserializeObject(body, out root, out parseError))
            {
                Dictionary<string, object> error = MiniJson.GetObject(root, "error");
                errorType = MiniJson.GetString(error, "type");
                errorCode = MiniJson.GetString(error, "code");
            }

            string detail = !StringUtil.IsBlank(errorCode) ? errorCode : errorType;
            return StringUtil.IsBlank(detail)
               ? "OpenAI request failed with HTTP status " + status + "."
               : "OpenAI request failed with HTTP status " + status + " (" + detail + ").";
        }

        private static Dictionary<string, object> Object()
        {
            return new Dictionary<string, object>(StringComparer.Ordinal);
        }
    }
}
