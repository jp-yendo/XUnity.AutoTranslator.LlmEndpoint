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
    }
}
