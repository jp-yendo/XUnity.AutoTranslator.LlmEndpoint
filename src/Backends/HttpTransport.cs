using System;
using System.Collections.Generic;
using System.IO;
using System.Net;
using System.Text;
#if !NETFRAMEWORK
using System.Net.Http;
using System.Net.Http.Headers;
using System.Threading;
#endif

namespace XUnity.AutoTranslator.LlmEndpoint.Backends
{
    // Outcome of a completed HTTP exchange, whatever the status code.
    internal sealed class HttpResult
    {
        public int Status;
        public string Body;
        public int RetryAfterMs;
    }

    // Raised when no HTTP response was received (DNS, TCP, TLS, or a canceled
    // transport). Carries a transient hint so the caller can decide about retries.
    internal sealed class HttpTransportException : Exception
    {
        public HttpTransportException(string message, Exception inner, bool transient) : base(message, inner)
        {
            Transient = transient;
        }

        public bool Transient { get; private set; }
    }

    // A single POST-JSON transport shared by every backend. All framework-specific
    // HTTP lives here so the backends themselves compile unchanged on both the
    // net6.0 (IL2CPP) and net35 (Mono) targets: net6.0 uses HttpClient, net35 uses
    // the synchronous HttpWebRequest that its runtime provides.
    internal sealed class HttpTransport : IDisposable
    {
        private const string JsonMediaType = "application/json";
        private readonly string userAgent;
#if NETFRAMEWORK
        private readonly int maxConnections;
        private readonly int timeoutMs;
#else
        private readonly HttpClient client;
#endif

        // timeoutMs bounds a single request end to end (connect, generation wait, and
        // body read). A value of 0 or less disables the timeout entirely, in which case
        // a silent network drop (no FIN/RST) can block the request indefinitely.
        public HttpTransport(int maxConnections, int timeoutMs, string userAgent)
        {
            this.userAgent = userAgent;
#if NETFRAMEWORK
            this.maxConnections = maxConnections < 1 ? 1 : maxConnections;
            this.timeoutMs = timeoutMs > 0 ? timeoutMs : System.Threading.Timeout.Infinite;
#else
            HttpClientHandler handler = new HttpClientHandler();
            handler.AutomaticDecompression = DecompressionMethods.GZip | DecompressionMethods.Deflate;
            handler.MaxConnectionsPerServer = maxConnections;
            client = new HttpClient(handler);
            client.Timeout = timeoutMs > 0 ? TimeSpan.FromMilliseconds(timeoutMs) : Timeout.InfiniteTimeSpan;
            client.DefaultRequestHeaders.UserAgent.ParseAdd(userAgent);
#endif
        }

        // Sends a JSON POST. The transport always sets Accept and Content-Type to
        // application/json; callers pass only authentication and custom headers.
        public HttpResult PostJson(Uri url, IEnumerable<KeyValuePair<string, string>> headers, string jsonBody)
        {
#if NETFRAMEWORK
            return PostJsonWebRequest(url, headers, jsonBody);
#else
            return PostJsonHttpClient(url, headers, jsonBody);
#endif
        }

        public void Dispose()
        {
#if !NETFRAMEWORK
            client.Dispose();
#endif
        }

#if NETFRAMEWORK
        private HttpResult PostJsonWebRequest(Uri url, IEnumerable<KeyValuePair<string, string>> headers, string jsonBody)
        {
            HttpWebRequest request = (HttpWebRequest)WebRequest.Create(url);
            request.Method = "POST";
            request.Accept = JsonMediaType;
            request.ContentType = JsonMediaType;
            request.UserAgent = userAgent;
            request.KeepAlive = true;
            request.Timeout = timeoutMs;
            request.ReadWriteTimeout = timeoutMs;
            request.ServicePoint.ConnectionLimit = maxConnections;
            try { request.AutomaticDecompression = DecompressionMethods.GZip | DecompressionMethods.Deflate; }
            catch (NotImplementedException) { }
            if (headers != null)
            {
                foreach (KeyValuePair<string, string> header in headers)
                {
                    request.Headers[header.Key] = header.Value;
                }
            }

            byte[] payload = Encoding.UTF8.GetBytes(jsonBody ?? string.Empty);
            try
            {
                using (Stream stream = request.GetRequestStream())
                {
                    stream.Write(payload, 0, payload.Length);
                }
            }
            catch (WebException ex)
            {
                throw new HttpTransportException("The request body could not be sent: " + ex.Message, ex, true);
            }

            HttpWebResponse response;
            try
            {
                response = (HttpWebResponse)request.GetResponse();
            }
            catch (WebException ex)
            {
                // An HTTP error status (4xx/5xx) surfaces here with the error response
                // attached; read its body so the backend can inspect the status code.
                // A null response means the exchange never produced a response at all
                // (timeout, connect/receive failure, silently dropped connection).
                HttpWebResponse errorResponse = ex.Response as HttpWebResponse;
                if (errorResponse == null)
                {
                    throw new HttpTransportException("No HTTP response was received: " + ex.Message, ex, true);
                }
                using (errorResponse)
                {
                    return ReadErrorResponse(errorResponse);
                }
            }

            using (response)
            {
                try
                {
                    return ReadResponse(response);
                }
                catch (Exception ex)
                {
                    // The response began but the body could not be fully read (for
                    // example the connection dropped mid-transfer). Treat it as a
                    // transient transport failure so the retry policy can apply.
                    throw new HttpTransportException("The response body could not be read: " + ex.Message, ex, true);
                }
            }
        }

        private static HttpResult ReadErrorResponse(HttpWebResponse response)
        {
            try
            {
                return ReadResponse(response);
            }
            catch (Exception ex)
            {
                throw new HttpTransportException("An HTTP error response could not be read: " + ex.Message, ex, true);
            }
        }

        private static HttpResult ReadResponse(HttpWebResponse response)
        {
            string body;
            using (Stream stream = response.GetResponseStream())
            using (StreamReader reader = new StreamReader(stream, Encoding.UTF8))
            {
                body = reader.ReadToEnd();
            }

            HttpResult result = new HttpResult();
            result.Status = (int)response.StatusCode;
            result.Body = body;
            result.RetryAfterMs = ParseRetryAfter(response.Headers["Retry-After"]);
            return result;
        }

        private static int ParseRetryAfter(string value)
        {
            if (string.IsNullOrEmpty(value)) return 0;
            int seconds;
            if (int.TryParse(value.Trim(), out seconds))
            {
                return seconds > 0 ? SaturateSeconds(seconds) : 0;
            }
            DateTime when;
            if (DateTime.TryParse(
               value,
               System.Globalization.CultureInfo.InvariantCulture,
               System.Globalization.DateTimeStyles.AdjustToUniversal,
               out when))
            {
                double milliseconds = (when - DateTime.UtcNow).TotalMilliseconds;
                return milliseconds > 0 ? Saturate(milliseconds) : 0;
            }
            return 0;
        }

        private static int SaturateSeconds(int seconds)
        {
            return seconds > int.MaxValue / 1000 ? int.MaxValue : seconds * 1000;
        }
#else
        private HttpResult PostJsonHttpClient(Uri url, IEnumerable<KeyValuePair<string, string>> headers, string jsonBody)
        {
            using (HttpRequestMessage request = new HttpRequestMessage(HttpMethod.Post, url))
            {
                request.Headers.Accept.ParseAdd(JsonMediaType);
                if (headers != null)
                {
                    foreach (KeyValuePair<string, string> header in headers)
                    {
                        request.Headers.TryAddWithoutValidation(header.Key, header.Value);
                    }
                }
                request.Content = new StringContent(jsonBody ?? string.Empty, Encoding.UTF8, JsonMediaType);

                try
                {
                    using (HttpResponseMessage response = client.SendAsync(
                       request,
                       HttpCompletionOption.ResponseContentRead,
                       CancellationToken.None).GetAwaiter().GetResult())
                    {
                        HttpResult result = new HttpResult();
                        result.Body = response.Content.ReadAsStringAsync().GetAwaiter().GetResult();
                        result.Status = (int)response.StatusCode;
                        result.RetryAfterMs = ParseRetryAfter(response.Headers.RetryAfter);
                        return result;
                    }
                }
                catch (HttpRequestException ex)
                {
                    throw new HttpTransportException("No HTTP response was received: " + ex.Message, ex, true);
                }
                catch (OperationCanceledException ex)
                {
                    throw new HttpTransportException("The request was canceled by the HTTP transport.", ex, true);
                }
            }
        }

        private static int ParseRetryAfter(RetryConditionHeaderValue retryAfter)
        {
            if (retryAfter == null) return 0;
            if (retryAfter.Delta.HasValue)
            {
                return Saturate(retryAfter.Delta.Value.TotalMilliseconds);
            }
            if (retryAfter.Date.HasValue)
            {
                return Saturate((retryAfter.Date.Value - DateTimeOffset.UtcNow).TotalMilliseconds);
            }
            return 0;
        }
#endif

        private static int Saturate(double milliseconds)
        {
            if (milliseconds <= 0) return 0;
            return milliseconds >= int.MaxValue ? int.MaxValue : (int)milliseconds;
        }
    }
}
