using System;
using System.IO;
using System.Text;
using XUnity.AutoTranslator.LlmEndpoint.Configuration;
using XUnity.AutoTranslator.LlmEndpoint.Utilities;

namespace XUnity.AutoTranslator.LlmEndpoint.Logging
{
    internal sealed class EndpointLogger
    {
        private readonly object sync = new object();
        private readonly LogLevel minimumLevel;
        private readonly string logFile;
        private readonly string secret;

        public EndpointLogger(LogLevel minimumLevel, string logFile, string secret)
        {
            this.minimumLevel = minimumLevel;
            this.logFile = logFile;
            this.secret = secret ?? string.Empty;
        }

        public void Debug(string message) { Write(LogLevel.Debug, message, null); }
        public void Info(string message) { Write(LogLevel.Info, message, null); }
        public void Warn(string message) { Write(LogLevel.Warn, message, null); }
        public void Error(string message, Exception exception) { Write(LogLevel.Error, message, exception); }
        public void BatchActivity(bool enabled, string message)
        {
            if (!enabled) return;
            Write(LogLevel.Info, message, null);
        }

        private void Write(LogLevel level, string message, Exception exception)
        {
            if (minimumLevel == LogLevel.Off || level < minimumLevel) return;
            string line = DateTime.UtcNow.ToString("yyyy-MM-ddTHH:mm:ss.fffZ") +
               " [" + level.ToString().ToUpperInvariant() + "] " + Redact(message);
            if (exception != null)
            {
                line += " | " + exception.GetType().Name + ": " + Redact(exception.Message);
            }

            lock (sync)
            {
                try
                {
                    Console.WriteLine("[LlmEndpoint] " + line);
                    if (!StringUtil.IsBlank(logFile))
                    {
                        string directory = Path.GetDirectoryName(logFile);
                        if (!StringUtil.IsBlank(directory) && !Directory.Exists(directory)) Directory.CreateDirectory(directory);
                        using (StreamWriter writer = new StreamWriter(logFile, true, new UTF8Encoding(false)))
                        {
                            writer.WriteLine(line);
                        }
                    }
                }
                catch
                {
                    // Logging must never break translation processing.
                }
            }
        }

        private string Redact(string value)
        {
            if (value == null) return string.Empty;
            if (!StringUtil.IsBlank(secret)) return value.Replace(secret, "[REDACTED]");
            return value;
        }
    }
}
