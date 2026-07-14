using System.Collections.Generic;
using System.Text;
using XUnity.AutoTranslator.LlmEndpoint.Prompts;

namespace XUnity.AutoTranslator.LlmEndpoint.Dispatch
{
    internal static class PromptBudgetEstimator
    {
        private const int ProviderEnvelopeReserveTokens = 128;
        private const int MinimumOutputReserveTokens = 128;
        private const int OutputEnvelopeReserveTokens = 32;

        public static int EstimateUpperBoundTokens(PromptEnvelope prompt, IList<PromptItem> items)
        {
            long inputTokens = ByteCount(prompt.SystemMessage) + (long)ByteCount(prompt.UserMessage);
            long outputTokens = EstimateOutputUpperBoundTokens(items);
            return Saturate(inputTokens + outputTokens + ProviderEnvelopeReserveTokens);
        }

        public static int EstimateOutputUpperBoundTokens(IList<PromptItem> items)
        {
            long outputTokens = OutputEnvelopeReserveTokens;
            for (int i = 0; i < items.Count; i++) outputTokens += ByteCount(items[i].Text);
            if (outputTokens < MinimumOutputReserveTokens) return MinimumOutputReserveTokens;
            return outputTokens >= int.MaxValue ? int.MaxValue : (int)outputTokens;
        }

        private static int Saturate(long value)
        {
            return value >= int.MaxValue ? int.MaxValue : (int)value;
        }

        private static int ByteCount(string value)
        {
            return string.IsNullOrEmpty(value) ? 0 : Encoding.UTF8.GetByteCount(value);
        }
    }
}
