namespace XUnity.AutoTranslator.LlmEndpoint.Runtime
{
    internal static class ProductInfo
    {
        public static readonly string UserAgent =
           "XUnity.AutoTranslator.LlmEndpoint/" +
           typeof(ProductInfo).Assembly.GetName().Version.ToString(3);
    }
}
