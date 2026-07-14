using System.Text.RegularExpressions;
using XUnity.AutoTranslator.LlmEndpoint.Utilities;

namespace XUnity.AutoTranslator.LlmEndpoint.Dispatch
{
    // Decides whether a source text needs no translation and can be returned
    // unchanged without ever entering the queue or reaching the backend. Every check
    // runs on a halfwidth-normalized, trimmed copy; the caller always returns the
    // ORIGINAL text (untrimmed).
    internal static class PassthroughFilter
    {
        // Technical shapes that contain two or more Latin letters but are not natural
        // language. Single-letter shapes (e.g. "v0", "1920x1080") are already covered
        // by the letter-count rule below. Whitespace between tokens is optional.
        private static readonly Regex[] TechnicalShapes = new Regex[]
        {
            // Durations: 45s, 4m3s, 1h 2m 3s (units h/m/s in any order).
            new Regex(
               @"^\d+\s*[hms](\s*\d+\s*[hms])*$",
               RegexOptions.IgnoreCase | RegexOptions.CultureInvariant | RegexOptions.Compiled),
            // Frames per second: FPS 340, 340fps.
            new Regex(
               @"^(fps\s*\d+|\d+\s*fps)$",
               RegexOptions.IgnoreCase | RegexOptions.CultureInvariant | RegexOptions.Compiled),
            // Version: v0, v1.2.3, ver 0.0.0, version 1.0.
            new Regex(
               @"^(version|ver|v)\s*\d+(\.\d+)*$",
               RegexOptions.IgnoreCase | RegexOptions.CultureInvariant | RegexOptions.Compiled),
            // Resolution: 1920x1080, 1280 x 720, 1280×720.
            new Regex(
               @"^\d+\s*[x×]\s*\d+$",
               RegexOptions.IgnoreCase | RegexOptions.CultureInvariant | RegexOptions.Compiled),
        };

        public static bool ShouldPassthrough(string original)
        {
            if (original == null) return true;
            string normalized = StringUtil.ToHalfwidth(original).Trim();
            if (normalized.Length == 0) return true;

            // Rule 1: no letters from a script other than Latin, and at most one Latin
            // letter. This covers numbers-only, symbols-only, a single Latin letter, and
            // a single Latin letter surrounded by digits/symbols (e.g. "(A)", "A+").
            // A CJK/Hangul/Cyrillic/etc. character is a non-Latin letter and therefore
            // always routes to translation, so a single meaningful CJK glyph is kept.
            int latinLetters = 0;
            bool onlyLatinLetters = true;
            for (int i = 0; i < normalized.Length; i++)
            {
                char c = normalized[i];
                if (!char.IsLetter(c)) continue;
                if ((c >= 'a' && c <= 'z') || (c >= 'A' && c <= 'Z'))
                {
                    latinLetters++;
                }
                else
                {
                    onlyLatinLetters = false;
                    break;
                }
            }
            if (onlyLatinLetters && latinLetters <= 1) return true;

            // Rule 2: known technical shapes with two or more Latin letters.
            for (int i = 0; i < TechnicalShapes.Length; i++)
            {
                if (TechnicalShapes[i].IsMatch(normalized)) return true;
            }
            return false;
        }
    }
}
