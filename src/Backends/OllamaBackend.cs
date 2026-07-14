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
    internal sealed class OllamaBackend : LlmBackendBase
    {
        private readonly HttpClient client;
        private readonly Uri chatEndpoint;

        public OllamaBackend(LlmSettings settings, EndpointLogger logger) : base(settings, logger)
        {
            HttpClientHandler handler = new HttpClientHandler();
            handler.AutomaticDecompression = DecompressionMethods.GZip | DecompressionMethods.Deflate;
            handler.MaxConnectionsPerServer = settings.MaxParallelRequests;
            client = new HttpClient(handler);
            client.Timeout = Timeout.InfiniteTimeSpan;
            client.DefaultRequestHeaders.UserAgent.ParseAdd(ProductInfo.UserAgent);
            chatEndpoint = ProviderEndpoint.OllamaChat(settings.EndpointUrl);
        }

        protected override string GenerateOnce(PromptEnvelope prompt)
        {
            try
            {
                try
                {
                    return SendOnce(prompt, prompt.UseStructuredOutput, CancellationToken.None);
                }
                catch (BackendException ex)
                {
                    if (ex.StatusCode != 400 || !prompt.UseStructuredOutput) throw;
                    Logger.Warn("Ollama rejected the JSON Schema; retrying with JSON mode.");
                    return SendOnce(prompt, false, CancellationToken.None);
                }
            }
            catch (OperationCanceledException ex)
            {
                throw new BackendException("Ollama request was canceled by the HTTP transport.", ex, true);
            }
            catch (HttpRequestException ex)
            {
                int status = ex.StatusCode.HasValue ? (int)ex.StatusCode.Value : 0;
                throw new BackendException(
                   status == 0
                      ? "Ollama transport failed before an HTTP response was received."
                      : "Ollama transport failed with HTTP status " + status + ".",
                   ex,
                   status == 0 || IsTransientStatus(status));
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

        private string SendOnce(
           PromptEnvelope prompt,
           bool useJsonSchema,
           CancellationToken cancellationToken)
        {
            using (HttpRequestMessage request = BuildRequest(prompt, useJsonSchema))
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

        private HttpRequestMessage BuildRequest(PromptEnvelope prompt, bool useJsonSchema)
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

            if (prompt.UseStructuredOutput)
            {
                root["format"] = useJsonSchema
                   ? (object)JsonSchemaFactory.CreateTranslationSchema(prompt.ExpectedIds)
                   : "json";
            }

            HttpRequestMessage request = new HttpRequestMessage(HttpMethod.Post, chatEndpoint);
            request.Headers.Accept.Add(new MediaTypeWithQualityHeaderValue("application/json"));
            if (!StringUtil.IsBlank(Settings.ApiKey))
            {
                request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", Settings.ApiKey);
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
