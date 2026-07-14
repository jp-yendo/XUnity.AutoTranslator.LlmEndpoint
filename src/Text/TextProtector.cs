using System;
using System.Collections.Generic;
using System.Text.RegularExpressions;

namespace XUnity.AutoTranslator.LlmEndpoint.Text
{
    internal sealed class ProtectedText
    {
        private readonly List<ProtectedToken> tokens;
        private readonly string markerPrefix;

        public ProtectedText(string original, string value, List<ProtectedToken> tokens, string markerPrefix)
        {
            Original = original;
            Value = value;
            this.tokens = tokens;
            this.markerPrefix = markerPrefix;
        }

        public string Original { get; private set; }
        public string Value { get; private set; }

        public bool TryRestore(string translated, out string restored, out string error)
        {
            restored = null;
            error = null;
            if (translated == null)
            {
                error = "The translated text was null.";
                return false;
            }

            int previousPosition = -1;
            for (int i = 0; i < tokens.Count; i++)
            {
                ProtectedToken token = tokens[i];
                int first = translated.IndexOf(token.Marker, StringComparison.Ordinal);
                if (first < 0)
                {
                    error = "A protected token was missing.";
                    return false;
                }
                if (translated.IndexOf(token.Marker, first + token.Marker.Length, StringComparison.Ordinal) >= 0)
                {
                    error = "A protected token was duplicated.";
                    return false;
                }
                if (first <= previousPosition)
                {
                    error = "Protected tokens were reordered.";
                    return false;
                }
                previousPosition = first;
            }

            string value = translated;
            for (int i = 0; i < tokens.Count; i++)
            {
                value = value.Replace(tokens[i].Marker, tokens[i].Original);
            }
            if (value.IndexOf(markerPrefix, StringComparison.Ordinal) >= 0)
            {
                error = "An unknown protected token was returned.";
                return false;
            }
            restored = value;
            return true;
        }
    }

    internal sealed class ProtectedToken
    {
        public ProtectedToken(string marker, string original)
        {
            Marker = marker;
            Original = original;
        }

        public string Marker { get; private set; }
        public string Original { get; private set; }
    }

    internal static class TextProtector
    {
        private static readonly Regex ProtectedPattern = new Regex(
           "(<[^<>]+?>)|(\\{\\{[^{}\\r\\n]+\\}\\})|(\\{[0-9A-Za-z_][^{}\\r\\n]*\\})|" +
           "(%(?:[0-9]+\\$)?[-+#0 ]*[0-9]*(?:\\.[0-9]+)?[diuoxXfFeEgGaAcspn%])|" +
           "(\\\\[nrt])|(\\r\\n|\\r|\\n)",
           RegexOptions.Compiled | RegexOptions.CultureInvariant);

        public static ProtectedText Protect(string text)
        {
            string original = text ?? string.Empty;
            string nonce = Guid.NewGuid().ToString("N");
            string markerPrefix = "__XUA_" + nonce + "_";
            List<ProtectedToken> tokens = new List<ProtectedToken>();
            string protectedValue = ProtectedPattern.Replace(original, delegate (Match match)
            {
                string marker = markerPrefix + tokens.Count + "__";
                tokens.Add(new ProtectedToken(marker, match.Value));
                return marker;
            });
            return new ProtectedText(original, protectedValue, tokens, markerPrefix);
        }

    }
}
