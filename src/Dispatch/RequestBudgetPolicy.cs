namespace XUnity.AutoTranslator.LlmEndpoint.Dispatch
{
    internal static class RequestBudgetPolicy
    {
        public static bool CanAddItem(int existingItemCount, int estimatedTokens, int maxRequestTokens)
        {
            return existingItemCount == 0 || estimatedTokens <= maxRequestTokens;
        }
    }
}
