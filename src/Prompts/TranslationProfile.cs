using System.Collections.Generic;
using XUnity.AutoTranslator.LlmEndpoint.Configuration;

namespace XUnity.AutoTranslator.LlmEndpoint.Prompts
{
    internal abstract class TranslationProfile
    {
        public abstract string Id { get; }
        public abstract bool Matches(string model);
        public virtual DispatchMode DispatchMode { get { return DispatchMode.Batch; } }
        public virtual int FormatAttemptCount { get { return 2; } }

        public abstract PromptEnvelope BuildPrompt(
           IList<PromptItem> items,
           PromptContext context,
           int attempt);

        public abstract ProfileParseResult ParseResponse(
           string output,
           IList<PromptItem> items,
           int attempt);
    }
}
