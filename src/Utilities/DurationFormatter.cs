using System;
using System.Globalization;

namespace XUnity.AutoTranslator.LlmEndpoint.Utilities
{
    internal static class DurationFormatter
    {
        public static string Format(TimeSpan duration)
        {
            double milliseconds = Math.Max(0.0, duration.TotalMilliseconds);
            if (milliseconds < 1000.0)
            {
                return milliseconds.ToString("0", CultureInfo.InvariantCulture) + " ms";
            }
            if (milliseconds < 60000.0)
            {
                return (milliseconds / 1000.0).ToString("0.00", CultureInfo.InvariantCulture) + " s";
            }
            return (milliseconds / 60000.0).ToString("0.00", CultureInfo.InvariantCulture) + " m";
        }
    }
}
