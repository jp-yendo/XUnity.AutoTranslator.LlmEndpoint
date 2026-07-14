using System;

namespace XUnity.AutoTranslator.LlmEndpoint.Backends
{
    internal sealed class BackendException : Exception
    {
        public BackendException(string message, bool transient, int retryAfterMs)
           : base(message)
        {
            IsTransient = transient;
            RetryAfterMs = retryAfterMs;
        }

        public BackendException(string message, bool transient, int retryAfterMs, int statusCode)
           : this(message, transient, retryAfterMs)
        {
            StatusCode = statusCode;
        }

        public BackendException(string message, Exception inner, bool transient)
           : base(message, inner)
        {
            IsTransient = transient;
        }

        public bool IsTransient { get; private set; }
        public int RetryAfterMs { get; private set; }
        public int StatusCode { get; private set; }
    }
}
