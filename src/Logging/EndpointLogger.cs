using System;
using System.Globalization;
using System.IO;
using System.Text;
using XUnity.AutoTranslator.LlmEndpoint.Configuration;
using XUnity.AutoTranslator.LlmEndpoint.Utilities;

namespace XUnity.AutoTranslator.LlmEndpoint.Logging
{
    internal sealed class EndpointLogger
    {
        // Upper bound on the lock-avoidance suffix so a permanently locked target
        // cannot spin forever while probing alternative file names.
        private const int MaxLockProbe = 100;

        private readonly object sync = new object();
        private readonly LogLevel minimumLevel;
        private readonly string secret;
        private readonly bool loggingEnabled;
        private readonly string directory;
        private readonly string baseName;
        private readonly string extension;
        private readonly int retentionDays;

        // Guarded by sync. currentDate is the local day the cached path belongs to;
        // currentPath is the dated file (optionally lock-suffixed) in use for it.
        private string currentDate;
        private string currentPath;

        public EndpointLogger(LogLevel minimumLevel, string logFile, int retentionDays, string secret)
        {
            this.minimumLevel = minimumLevel;
            this.secret = secret ?? string.Empty;
            this.retentionDays = retentionDays;

            if (StringUtil.IsBlank(logFile))
            {
                loggingEnabled = false;
                return;
            }

            string full = logFile.Trim();
            directory = Path.GetDirectoryName(full);
            extension = Path.GetExtension(full);
            baseName = Path.GetFileNameWithoutExtension(full);
            loggingEnabled = !StringUtil.IsBlank(baseName);
        }

        // Cheap pre-check so callers can skip building per-item log strings when the
        // level is not active.
        public bool IsEnabled(LogLevel level)
        {
            return loggingEnabled && minimumLevel != LogLevel.Off && level >= minimumLevel;
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
            // No LogFile means logging is disabled entirely; the host console is
            // not a usable sink under the mod loaders we target, so nothing is written.
            if (!loggingEnabled) return;
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
                    string date = DateTime.Now.ToString("yyyyMMdd", CultureInfo.InvariantCulture);
                    if (date != currentDate)
                    {
                        currentDate = date;
                        currentPath = ResolveWritablePath(date);
                        CleanupOldLogs(date);
                    }
                    if (!AppendLine(currentPath, line))
                    {
                        // The cached target became unavailable (for example a viewer
                        // locked it); re-resolve to a fresh suffix and retry once.
                        currentPath = ResolveWritablePath(date);
                        AppendLine(currentPath, line);
                    }
                }
                catch
                {
                    // Logging must never break translation processing.
                }
            }
        }

        // Builds the dated file name. sequence 0 is the plain dated file; 1+ append a
        // numeric suffix used only when a lower one is locked.
        private string BuildPath(string date, int sequence)
        {
            string name = sequence <= 0
               ? baseName + "-" + date + extension
               : baseName + "-" + date + "-" + sequence.ToString(CultureInfo.InvariantCulture) + extension;
            return StringUtil.IsBlank(directory) ? name : Path.Combine(directory, name);
        }

        private string ResolveWritablePath(string date)
        {
            if (!StringUtil.IsBlank(directory) && !Directory.Exists(directory))
            {
                try { Directory.CreateDirectory(directory); }
                catch { }
            }
            for (int sequence = 0; sequence <= MaxLockProbe; sequence++)
            {
                string candidate = BuildPath(date, sequence);
                if (CanOpenForAppend(candidate)) return candidate;
            }
            return BuildPath(date, 0);
        }

        private static bool CanOpenForAppend(string path)
        {
            try
            {
                using (new FileStream(path, FileMode.Append, FileAccess.Write, FileShare.Read))
                {
                }
                return true;
            }
            catch (IOException)
            {
                return false;
            }
            catch (UnauthorizedAccessException)
            {
                return false;
            }
        }

        private static bool AppendLine(string path, string line)
        {
            if (StringUtil.IsBlank(path)) return false;
            try
            {
                using (FileStream stream = new FileStream(path, FileMode.Append, FileAccess.Write, FileShare.Read))
                using (StreamWriter writer = new StreamWriter(stream, new UTF8Encoding(false)))
                {
                    writer.WriteLine(line);
                }
                return true;
            }
            catch (IOException)
            {
                return false;
            }
            catch (UnauthorizedAccessException)
            {
                return false;
            }
        }

        // Deletes dated files whose day is older than the retention window. A value of
        // 0 (or less) keeps files forever. The undated template file is never matched.
        private void CleanupOldLogs(string today)
        {
            if (retentionDays <= 0) return;
            try
            {
                string dir = StringUtil.IsBlank(directory) ? "." : directory;
                if (!Directory.Exists(dir)) return;

                DateTime todayDate = DateTime.ParseExact(today, "yyyyMMdd", CultureInfo.InvariantCulture);
                DateTime cutoff = todayDate.AddDays(-(retentionDays - 1));
                string prefix = baseName + "-";

                foreach (string file in Directory.GetFiles(dir))
                {
                    string name = Path.GetFileName(file);
                    if (name == null) continue;
                    if (!name.StartsWith(prefix, StringComparison.OrdinalIgnoreCase)) continue;
                    if (!StringUtil.IsBlank(extension) &&
                       !name.EndsWith(extension, StringComparison.OrdinalIgnoreCase)) continue;

                    string token = name.Substring(prefix.Length);
                    if (token.Length < 8) continue;
                    DateTime fileDate;
                    if (!DateTime.TryParseExact(
                       token.Substring(0, 8),
                       "yyyyMMdd",
                       CultureInfo.InvariantCulture,
                       DateTimeStyles.None,
                       out fileDate)) continue;

                    if (fileDate < cutoff)
                    {
                        try { File.Delete(file); }
                        catch { }
                    }
                }
            }
            catch
            {
                // Retention cleanup is best-effort and must never break logging.
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
