namespace XUnity.AutoTranslator.LlmEndpoint.Configuration
{
    internal enum BackendKind
    {
        Ollama,
        OpenAI,
        Anthropic
    }

    internal enum LogLevel
    {
        Debug = 0,
        Info = 1,
        Warn = 2,
        Error = 3,
        Off = 4
    }

    internal enum DispatchMode
    {
        Batch,
        Single
    }
}
