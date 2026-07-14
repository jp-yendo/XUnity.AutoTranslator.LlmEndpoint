using XUnity.AutoTranslator.LlmEndpoint.Prompts;

namespace XUnity.AutoTranslator.LlmEndpoint.Backends
{
    internal interface ILlmBackend
    {
        string Generate(PromptEnvelope prompt);
    }
}
