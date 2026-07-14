using System.Collections.Generic;

namespace XUnity.AutoTranslator.LlmEndpoint.Prompts
{
    internal sealed class PromptItem
    {
        public string Id;
        public string Text;
        public List<string> ContextBefore;
        public List<string> ContextAfter;
    }

    internal sealed class PromptContext
    {
        public string SourceLanguage;
        public string TargetLanguage;
        public string AdditionalInstructions;
    }

    internal sealed class PromptEnvelope
    {
        public string SystemMessage;
        public string UserMessage;
        public bool UseStructuredOutput;
        public List<string> ExpectedIds;
        public int MaxOutputTokens;
    }

    internal sealed class ProfileParseResult
    {
        public ProfileParseResult()
        {
            Translations = new Dictionary<string, string>(System.StringComparer.Ordinal);
        }

        public Dictionary<string, string> Translations;
        public bool IsFormatValid;
        public string Error;
    }
}
