using System;
using System.Collections.Generic;
using System.Text;
using System.Text.RegularExpressions;
using XUnity.AutoTranslator.LlmEndpoint.Utilities;

namespace XUnity.AutoTranslator.LlmEndpoint.Prompts
{
    // Default profile for every model without a dedicated one.
    //
    // The wire format is a plain XML <request>/<response> shape that small local
    // models (even ~12B) handle reliably: the request is XML and the response is
    // read back with regular expressions.
    //
    // Items are matched by their ORIGINAL text (the model echoes <original>), not
    // by position, so a reordered or partially returned response still aligns.
    //
    // Matching is two-pass, each pass more lenient than the last, so that markup
    // the model reflows or mangles when echoing the original does not knock the
    // whole batch out of alignment:
    //   Pass 1 - compare after removing control characters and whitespace.
    //   Pass 2 - for whatever is still unmatched, ALSO strip markup tags
    //            (<...> and </...>, keeping the inner text) before comparing.
    // An item confirmed in an earlier pass is never re-mapped.
    internal sealed class DefaultTranslationProfile : TranslationProfile
    {
        private static readonly Regex ItemPattern = new Regex(
           "<item>(.*?)</item>",
           RegexOptions.IgnoreCase | RegexOptions.Singleline | RegexOptions.CultureInvariant | RegexOptions.Compiled);
        private static readonly Regex OriginalPattern = new Regex(
           "<original>(.*?)</original>",
           RegexOptions.IgnoreCase | RegexOptions.Singleline | RegexOptions.CultureInvariant | RegexOptions.Compiled);
        private static readonly Regex TranslatedPattern = new Regex(
           "<translated>(.*?)</translated>",
           RegexOptions.IgnoreCase | RegexOptions.Singleline | RegexOptions.CultureInvariant | RegexOptions.Compiled);
        // Control characters and every kind of whitespace (spaces, tabs, CR/LF).
        private static readonly Regex ControlAndSpacePattern = new Regex(
           "[\\s\\p{C}]+",
           RegexOptions.CultureInvariant | RegexOptions.Compiled);
        // A single markup tag: <...> or </...>. The inner text is kept.
        private static readonly Regex TagPattern = new Regex(
           "</?[^<>]*>",
           RegexOptions.CultureInvariant | RegexOptions.Compiled);

        public override string Id { get { return "default-xml-batch"; } }

        public override bool Matches(string model)
        {
            return true;
        }

        public override PromptEnvelope BuildPrompt(IList<PromptItem> items, PromptContext context, int attempt)
        {
            string source = LanguageNameResolver.Resolve(context.SourceLanguage);
            string target = LanguageNameResolver.Resolve(context.TargetLanguage);

            StringBuilder system = new StringBuilder();
            system.Append("You are a precise translation engine. Translate each <item> in the XML request from ");
            system.Append(source).Append(" to ").Append(target).Append(".");
            system.Append("\n- Respond ONLY with XML and nothing else - no explanations, comments, or extra text.");
            system.Append("\n- For every <item> in <request>, return one <item> in <response> where <original> is the original text and <translated> is its translation.");
            system.Append("\n- Keep all tags, attributes, placeholders, whitespace, and line breaks exactly as in the original.");
            system.Append("\n- Do not translate programming code, API calls, placeholders, or other technical snippets; copy them exactly.");
            system.Append("\n- Keep terms already established in the target language region unchanged (e.g., in Japan: ATK, HP, MP, ID).");
            system.Append("\n\nResponse schema:\n\n<response>\n<item>\n<original>...</original>\n<translated>...</translated>\n</item>\n</response>");

            if (!StringUtil.IsBlank(context.AppSummary))
            {
                system.Append("\n\n<app_context>\nBackground information about the application (NOT for translation - use it only to choose appropriate terminology):\n");
                system.Append(context.AppSummary.Trim());
                system.Append("\n</app_context>");
            }
            if (!StringUtil.IsBlank(context.AdditionalInstructions))
            {
                system.Append("\n\nTrusted additional translation instructions:\n");
                system.Append(context.AdditionalInstructions.Trim());
            }
            if (attempt > 0)
            {
                system.Append("\n\nA previous response was invalid or incomplete. Respond only with the <response> XML and include one <item> for every requested <item>.");
            }

            StringBuilder request = new StringBuilder();
            request.Append("<request>\n");
            for (int i = 0; i < items.Count; i++)
            {
                request.Append("<item>").Append(EscapeXml(items[i].Text)).Append("</item>\n");
            }
            request.Append("</request>");

            PromptEnvelope envelope = new PromptEnvelope();
            envelope.SystemMessage = system.ToString();
            envelope.UserMessage = request.ToString();
            return envelope;
        }

        public override ProfileParseResult ParseResponse(string output, IList<PromptItem> items, int attempt)
        {
            ProfileParseResult result = new ProfileParseResult();
            if (output == null || output.IndexOf("<item", StringComparison.OrdinalIgnoreCase) < 0)
            {
                result.Error = "The response did not contain any <item> element.";
                return result;
            }

            // Parse every <item> in order into its echoed original and translation.
            List<string> originals = new List<string>();
            List<string> translations = new List<string>();
            foreach (Match itemMatch in ItemPattern.Matches(output))
            {
                string block = itemMatch.Groups[1].Value;
                Match originalMatch = OriginalPattern.Match(block);
                Match translatedMatch = TranslatedPattern.Match(block);
                if (!originalMatch.Success || !translatedMatch.Success) continue;

                string original = UnescapeXml(originalMatch.Groups[1].Value);
                // Do not trim the value: whitespace, line breaks, and markup must survive.
                string translated = UnescapeXml(translatedMatch.Groups[1].Value);
                if (StringUtil.IsBlank(original) || StringUtil.IsBlank(translated)) continue;

                originals.Add(original);
                translations.Add(translated);
            }

            bool[] itemMatched = new bool[items.Count];
            bool[] responseUsed = new bool[originals.Count];

            // Pass 1: control characters and whitespace removed.
            MatchPass(items, originals, translations, itemMatched, responseUsed, result, StripControl);
            // Pass 2: fallback for anything still unmatched - also drop markup tags.
            MatchPass(items, originals, translations, itemMatched, responseUsed, result, StripControlAndTags);

            result.IsFormatValid = result.Translations.Count == items.Count;
            if (!result.IsFormatValid)
            {
                result.Error = "The response did not return a translation for every requested item.";
            }
            return result;
        }

        // Assign each still-free response to the first still-free source item whose
        // key matches under the given normalization. Confirmed items are skipped, so
        // an earlier (stricter) pass is never overturned by a later (looser) one.
        private static void MatchPass(
           IList<PromptItem> items,
           List<string> originals,
           List<string> translations,
           bool[] itemMatched,
           bool[] responseUsed,
           ProfileParseResult result,
           Func<string, string> normalize)
        {
            for (int r = 0; r < originals.Count; r++)
            {
                if (responseUsed[r]) continue;
                string responseKey = normalize(originals[r]);
                if (responseKey.Length == 0) continue;

                for (int s = 0; s < items.Count; s++)
                {
                    if (itemMatched[s]) continue;
                    if (!string.Equals(normalize(items[s].Text), responseKey, StringComparison.Ordinal)) continue;

                    result.Translations[items[s].Id] = translations[r];
                    itemMatched[s] = true;
                    responseUsed[r] = true;
                    break;
                }
            }
        }

        // Pass 1 key: remove control characters and all whitespace, so a reflowed or
        // re-wrapped echo of a multi-line original still matches its source.
        private static string StripControl(string text)
        {
            if (text == null) return string.Empty;
            return ControlAndSpacePattern.Replace(text, string.Empty);
        }

        // Pass 2 key: also remove markup tags (keeping the inner text), so an item
        // whose tags the model dropped or altered still matches on its plain text.
        private static string StripControlAndTags(string text)
        {
            if (text == null) return string.Empty;
            return StripControl(TagPattern.Replace(text, string.Empty));
        }

        private static string EscapeXml(string text)
        {
            if (StringUtil.IsBlank(text)) return text ?? string.Empty;
            return text
               .Replace("&", "&amp;")
               .Replace("<", "&lt;")
               .Replace(">", "&gt;")
               .Replace("\"", "&quot;")
               .Replace("'", "&apos;");
        }

        private static string UnescapeXml(string text)
        {
            if (text == null) return string.Empty;
            return text
               .Replace("&apos;", "'")
               .Replace("&quot;", "\"")
               .Replace("&gt;", ">")
               .Replace("&lt;", "<")
               .Replace("&amp;", "&");
        }
    }
}
