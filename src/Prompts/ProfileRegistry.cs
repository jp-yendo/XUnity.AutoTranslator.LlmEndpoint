using System.Collections.Generic;
using XUnity.AutoTranslator.LlmEndpoint.Prompts.Profiles;

namespace XUnity.AutoTranslator.LlmEndpoint.Prompts
{
    internal sealed class ProfileRegistry
    {
        private readonly List<TranslationProfile> profiles;

        public ProfileRegistry()
        {
            profiles = new List<TranslationProfile>();
            profiles.Add(new HyMt2TranslationProfile());
            profiles.Add(new DefaultTranslationProfile());
        }

        public TranslationProfile Resolve(string model)
        {
            for (int i = 0; i < profiles.Count; i++)
            {
                if (profiles[i].Matches(model)) return profiles[i];
            }
            return profiles[profiles.Count - 1];
        }
    }
}
