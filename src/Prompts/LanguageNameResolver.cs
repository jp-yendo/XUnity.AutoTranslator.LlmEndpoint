using System;
using System.Collections.Generic;
using XUnity.AutoTranslator.LlmEndpoint.Utilities;

namespace XUnity.AutoTranslator.LlmEndpoint.Prompts
{
    internal static class LanguageNameResolver
    {
        private static readonly Dictionary<string, string> Names = CreateNames();

        public static string Resolve(string value)
        {
            if (StringUtil.IsBlank(value)) return "the target language";
            string trimmed = value.Trim();
            string name;
            if (Names.TryGetValue(trimmed, out name)) return name;
            int separator = trimmed.IndexOf('-');
            if (separator > 0 && Names.TryGetValue(trimmed.Substring(0, separator), out name)) return name;
            return trimmed;
        }

        private static Dictionary<string, string> CreateNames()
        {
            Dictionary<string, string> names = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
            names["auto"] = "the detected source language";
            names["ja"] = "Japanese";
            names["en"] = "English";
            names["ko"] = "Korean";
            names["zh"] = "Chinese";
            names["zh-CN"] = "Simplified Chinese";
            names["zh-Hans"] = "Simplified Chinese";
            names["zh-TW"] = "Traditional Chinese";
            names["zh-Hant"] = "Traditional Chinese";
            names["fr"] = "French";
            names["de"] = "German";
            names["es"] = "Spanish";
            names["it"] = "Italian";
            names["pt"] = "Portuguese";
            names["pt-BR"] = "Brazilian Portuguese";
            names["ru"] = "Russian";
            names["uk"] = "Ukrainian";
            names["pl"] = "Polish";
            names["nl"] = "Dutch";
            names["tr"] = "Turkish";
            names["ar"] = "Arabic";
            names["th"] = "Thai";
            names["vi"] = "Vietnamese";
            names["id"] = "Indonesian";
            return names;
        }
    }
}
