using System;
using System.Globalization;

namespace XUnity.AutoTranslator.LlmEndpoint.Utilities
{
    internal static class StringUtil
    {
        public static bool IsBlank(string value)
        {
            if (value == null) return true;
            for (int i = 0; i < value.Length; i++)
            {
                if (!char.IsWhiteSpace(value[i])) return false;
            }
            return true;
        }

        public static T ParseEnum<T>(string value, T defaultValue) where T : struct
        {
            if (IsBlank(value)) return defaultValue;
            string[] names = Enum.GetNames(typeof(T));
            for (int i = 0; i < names.Length; i++)
            {
                if (string.Equals(names[i], value.Trim(), StringComparison.OrdinalIgnoreCase))
                {
                    return (T)Enum.Parse(typeof(T), names[i], false);
                }
            }
            throw new FormatException("Unsupported " + typeof(T).Name + " value: " + value);
        }

        public static string Invariant(int value)
        {
            return value.ToString(CultureInfo.InvariantCulture);
        }

        public static string Invariant(double value)
        {
            return value.ToString("R", CultureInfo.InvariantCulture);
        }

        public static int Clamp(int value, int minimum, int maximum)
        {
            if (value < minimum) return minimum;
            if (value > maximum) return maximum;
            return value;
        }

        /// <summary>
        /// Converts fullwidth ASCII variants (U+FF01-FF5E, i.e. fullwidth digits,
        /// letters, and symbols) and the ideographic space (U+3000) to their halfwidth
        /// equivalents. Intended for normalizing text before pattern checks; every other
        /// character is left unchanged and a copy is allocated only when needed.
        /// </summary>
        public static string ToHalfwidth(string value)
        {
            if (string.IsNullOrEmpty(value)) return value ?? string.Empty;
            char[] buffer = null;
            for (int i = 0; i < value.Length; i++)
            {
                char c = value[i];
                char converted = c;
                if (c >= '！' && c <= '～') converted = (char)(c - 0xFEE0);
                else if (c == '　') converted = ' ';
                if (converted != c)
                {
                    if (buffer == null) buffer = value.ToCharArray();
                    buffer[i] = converted;
                }
            }
            return buffer == null ? value : new string(buffer);
        }

        /// <summary>
        /// Converts the literal escape sequences \n, \r, \t, and \\ into their
        /// actual characters so single-line INI values can carry line breaks.
        /// Unknown sequences keep both characters unchanged.
        /// </summary>
        public static string UnescapeSequences(string value)
        {
            if (value == null || value.IndexOf('\\') < 0) return value;
            System.Text.StringBuilder builder = new System.Text.StringBuilder(value.Length);
            for (int i = 0; i < value.Length; i++)
            {
                char current = value[i];
                if (current == '\\' && i + 1 < value.Length)
                {
                    char next = value[i + 1];
                    if (next == 'n') { builder.Append('\n'); i++; continue; }
                    if (next == 'r') { builder.Append('\r'); i++; continue; }
                    if (next == 't') { builder.Append('\t'); i++; continue; }
                    if (next == '\\') { builder.Append('\\'); i++; continue; }
                }
                builder.Append(current);
            }
            return builder.ToString();
        }
    }
}
