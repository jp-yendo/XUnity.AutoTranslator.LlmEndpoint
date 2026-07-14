using System;
using System.Collections.Generic;
using System.Net;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Text;
using System.Threading;
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
        private readonly HttpClient client;
        private readonly Uri messagesEndpoint;

        public AnthropicBackend(LlmSettings settings, EndpointLogger logger) : base(settings, logger)
        {
            HttpClientHandler handler = new HttpClientHandler();
            handler.AutomaticDecompression = DecompressionMethods.GZip | DecompressionMethods.Deflate;
            handler.MaxConnectionsPerServer = settings.MaxParallelRequests;
            client = new HttpClient(handler);
            client.Timeout = Timeout.InfiniteTimeSpan;
            client.DefaultRequestHeaders.UserAgent.ParseAdd(ProductInfo.UserAgent);
            messagesEndpoint = ProviderEndpoint.AnthropicMessages(settings.EndpointUrl);
        }

        protected override string GenerateOnce(PromptEnvelope prompt)
        {
            try
            {
                return SendOnce(prompt, CancellationToken.None);
            }
            catch (OperationCanceledException ex)
            {
                throw new BackendException("Anthropic request was canceled by the HTTP transport.", ex, true);
            }
            catch (HttpRequestException ex)
            {
                int status = ex.StatusCode.HasValue ? (int)ex.StatusCode.Value : 0;
                throw new BackendException(
                   status == 0
                      ? "Anthropic transport failed before an HTTP response was received."
                      : "Anthropic transport failed with HTTP status " + status + ".",
                   ex,
                   status == 0 || IsTransientStatus(status));
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

        private string SendOnce(
           PromptEnvelope prompt,
           CancellationToken cancellationToken)
        {
            using (HttpRequestMessage request = BuildRequest(prompt))
            using (HttpResponseMessage response = client.SendAsync(
               request,
               HttpCompletionOption.ResponseContentRead,
               cancellationToken).GetAwaiter().GetResult())
            {
                string body = response.Content.ReadAsStringAsync(cancellationToken).GetAwaiter().GetResult();
                int status = (int)response.StatusCode;
                if (!response.IsSuccessStatusCode)
                {
                    throw new BackendException(
                       BuildErrorMessage(status, body),
                       IsTransientStatus(status),
                       GetRetryAfterMilliseconds(response.Headers.RetryAfter),
                       status);
                }
                return ParseSuccess(body);
            }
        }

        private HttpRequestMessage BuildRequest(PromptEnvelope prompt)
        {
            Dictionary<string, object> root = Object();
            root["model"] = Settings.Model;
            root["max_tokens"] = Math.Max(1, prompt.MaxOutputTokens);
            if (!StringUtil.IsBlank(prompt.SystemMessage)) root["system"] = prompt.SystemMessage;

            List<object> messages = new List<object>();
            Dictionary<string, object> user = Object();
            user["role"] = "user";
            user["content"] = prompt.UserMessage;
            messages.Add(user);
            root["messages"] = messages;

            HttpRequestMessage request = new HttpRequestMessage(HttpMethod.Post, messagesEndpoint);
            request.Headers.Accept.Add(new MediaTypeWithQualityHeaderValue("application/json"));
            request.Headers.TryAddWithoutValidation("anthropic-version", LlmSettings.AnthropicApiVersion);
            if (!StringUtil.IsBlank(Settings.ApiKey))
            {
                request.Headers.TryAddWithoutValidation("x-api-key", Settings.ApiKey);
            }
            request.Content = new StringContent(MiniJson.Serialize(root), Encoding.UTF8, "application/json");
            return request;
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

        private static int GetRetryAfterMilliseconds(RetryConditionHeaderValue retryAfter)
        {
            if (retryAfter == null) return 0;
            if (retryAfter.Delta.HasValue)
            {
                double milliseconds = retryAfter.Delta.Value.TotalMilliseconds;
                return milliseconds >= int.MaxValue ? int.MaxValue : Math.Max(0, (int)milliseconds);
            }
            if (retryAfter.Date.HasValue)
            {
                double milliseconds = (retryAfter.Date.Value - DateTimeOffset.UtcNow).TotalMilliseconds;
                return milliseconds >= int.MaxValue ? int.MaxValue : Math.Max(0, (int)milliseconds);
            }
            return 0;
        }

        private static Dictionary<string, object> Object()
        {
            return new Dictionary<string, object>(StringComparer.Ordinal);
        }
    }
}
