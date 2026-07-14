using System;

namespace XUnity.AutoTranslator.LlmEndpoint.Dispatch
{
    internal sealed class PendingOperation
    {
        private readonly object sync = new object();
        private readonly string[] originals;
        private readonly string[] results;
        private readonly string[] errors;
        private int remaining;

        public PendingOperation(string[] originals)
        {
            this.originals = originals;
            results = new string[originals.Length];
            errors = new string[originals.Length];
            remaining = originals.Length;
        }

        public bool IsComplete
        {
            get { lock (sync) return remaining == 0; }
        }

        public bool HasFailures
        {
            get
            {
                lock (sync)
                {
                    for (int i = 0; i < errors.Length; i++) if (errors[i] != null) return true;
                    return false;
                }
            }
        }

        public void CompleteItem(int index, string translation)
        {
            lock (sync)
            {
                if (results[index] != null || errors[index] != null) return;
                results[index] = translation;
                remaining--;
            }
        }

        public void FailItem(int index, string error)
        {
            lock (sync)
            {
                if (results[index] != null || errors[index] != null) return;
                errors[index] = string.IsNullOrEmpty(error) ? "Translation failed." : error;
                remaining--;
            }
        }

        public string[] GetResults(bool returnOriginalForFailures)
        {
            lock (sync)
            {
                string[] copy = new string[results.Length];
                for (int i = 0; i < copy.Length; i++)
                {
                    copy[i] = results[i];
                    if (copy[i] == null && returnOriginalForFailures) copy[i] = originals[i];
                }
                return copy;
            }
        }

        public string GetErrorSummary()
        {
            lock (sync)
            {
                for (int i = 0; i < errors.Length; i++)
                {
                    if (errors[i] != null) return errors[i];
                }
                return "Translation failed.";
            }
        }
    }

    internal sealed class PendingItem
    {
        public PendingOperation Operation;
        public int Index;
        public string Id;
        public string Source;
        public string SourceLanguage;
        public string TargetLanguage;
        public DateTime EnqueuedUtc;
    }
}
