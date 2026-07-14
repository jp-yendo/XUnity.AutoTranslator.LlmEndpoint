# XUnity.AutoTranslator.LlmEndpoint

English | [日本語](README-ja.md)

An independent LLM translation endpoint DLL for `XUnity.AutoTranslator`.

Supported backends:

- Ollama native chat API
- OpenAI Chat Completions API and compatible APIs
- Anthropic Messages API and compatible APIs

## Installation

Download the release ZIP that matches your mod loader and runtime, then extract it directly into the game directory. The archive's folder layout overlays onto the game folder, placing the DLL in the correct `Translators` directory automatically.

| Release ZIP | Mod loader / runtime |
|---|---|
| `...-BepInEx-<ver>.zip` | BepInEx 5 (Mono) |
| `...-BepInEx-IL2CPP-<ver>.zip` | BepInEx 6 (IL2CPP) |
| `...-MelonMod-<ver>.zip` | MelonLoader (Mono) |
| `...-MelonMod-IL2CPP-<ver>.zip` | MelonLoader (IL2CPP) |

Alternatively, copy `XUnity.AutoTranslator.LlmEndpoint.dll` into the `Translators` directory of your XUnity.AutoTranslator installation manually (use the IL2CPP build for IL2CPP games and the Mono build for Mono games).

Then configure the endpoint:

1. Open the XUnity.AutoTranslator `Config.ini` file.
2. Select the endpoint in the `[Service]` section:

```ini
[Service]
Endpoint=LlmEndpoint
```

3. Add an `[LlmEndpoint]` section and configure at least the backend and model.

## Minimal configuration

```ini
[LlmEndpoint]
Backend=Ollama
Model=replace-with-an-installed-model
```

Backend defaults:

| Backend | Default base URL | API key environment variable |
|---|---|---|
| `Ollama` | `http://localhost:11434` | `OLLAMA_API_KEY` |
| `OpenAI` | `https://api.openai.com/v1` | `OPENAI_API_KEY` |
| `Anthropic` | `https://api.anthropic.com` | `ANTHROPIC_API_KEY` |

Set `ApiKey` directly when required, or leave it empty to use the environment variable shown above. `EndpointUrl` can override the backend's default base URL.

Example configurations:

- [Ollama](examples/Config.Ollama.ini)
- [OpenAI](examples/Config.OpenAI.ini)
- [Anthropic](examples/Config.Anthropic.ini)

See the [configuration reference](Documents/CONFIGURATION.md) for every setting and recommended values.

## Logging

Logs are written to the host console at `Info` level by default. No log file is created unless `LogFile` is configured.

`LogBatchActivity=true` enables Info-level batch request and response entries containing counts, estimated size, and elapsed time. Individual translation details are available at `Debug` level. API keys and complete prompts or responses are not logged.

## Troubleshooting

- `Model` is required. Initialization fails when it is empty.
- `EndpointUrl` must be an absolute HTTP or HTTPS base URL.
- Check the host console for initialization or connection errors.

## Additional documentation

- [Configuration reference](Documents/CONFIGURATION.md)
- [Third-party notices](THIRD_PARTY_NOTICES.md)
