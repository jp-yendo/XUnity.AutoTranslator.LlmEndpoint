# Configuration reference

English | [日本語](CONFIGURATION-ja.md)

The configuration section is `[LlmEndpoint]`. Write key names with the capitalization shown below.

## All settings

| Key | Default | Required | Allowed values or limit | 8 GB reference | 16 GB reference | Description |
|---|---:|---|---|---|---|---|
| `Backend` | `Ollama` | No | `Ollama`, `OpenAI`, `Anthropic` | `Ollama` | `Ollama` | API family to use. |
| `EndpointUrl` | Backend-specific | No | Absolute HTTP or HTTPS base URL | Backend default | Same | The plugin appends the backend API path to this base URL. |
| `ApiKey` | Empty | Conditional | String | Empty | Empty | When empty, the conventional environment variable for the selected backend is used. |
| `Model` | Empty | Yes | Non-empty model ID | An installed model | An installed model | Selects the provider model. A name containing `hy-mt2` activates its model-specific profile. |
| `BatchIntervalMs` | `300` | No | Integer `0` or greater | `300` | `300` | Maximum collection time measured from the first queued item. The batch closes immediately at `MaxBatchSize`. |
| `MaxBatchSize` | `8` | No | `1`-`50` | `8` | `16` | Maximum items per request. `MaxRequestTokens` can reduce the actual count. |
| `MaxRequestTokens` | `8192` | No | Integer `1` or greater | `8192` | `16384` | Conservative estimated token budget used only to assemble a request. It is not an LLM context setting and must not exceed the actual provider context limit. |
| `MaxParallelRequests` | `1` | No | `1`-`50` | `1` | `1` | Maximum real HTTP requests in progress. Increase only when the backend supports useful concurrent generation. |
| `RetryCount` | `1` | No | Integer `0` or greater | `1` | `1` | Retries after the initial attempt for transient HTTP or transport failures. |
| `LogLevel` | `Info` | No | `Debug`, `Info`, `Warn`, `Error`, `Off` | `Info` | `Info` | Shared minimum level for console and file output. |
| `LogBatchActivity` | `false` | No | `true`, `false` | `false` | `false` | Enables Info-level batch request and response logging. |
| `LogFile` | Empty | No | File path | Empty | Empty | Empty disables file output. Relative paths use the `Translators` directory. |
| `AppSummary` | Empty | No | String | Empty | Empty | Optional background about the app or game (setting, era, genre) added to the system message to improve translation nuance. No hard length limit; keep it within about 200 characters to save context and avoid model distraction. |
| `AdditionalInstructions` | Empty | No | String | Empty | Empty | Trusted translation guidance appended to the system message and kept separate from source text. No hard length limit; keep it within about 200 characters for the same reason. |

Backend-specific `EndpointUrl` defaults:

| Backend | Default EndpointUrl |
|---|---|
| `Ollama` | `http://localhost:11434` |
| `OpenAI` | `https://api.openai.com/v1` |
| `Anthropic` | `https://api.anthropic.com` |

When `ApiKey` is empty, the endpoint reads the following conventional environment variable. There is no setting for choosing another environment-variable name.

| Backend | Environment variable |
|---|---|
| `Ollama` | `OLLAMA_API_KEY` |
| `OpenAI` | `OPENAI_API_KEY` |
| `Anthropic` | `ANTHROPIC_API_KEY` |

## Complete example for an 8 GB GPU

```ini
[LlmEndpoint]
Backend=Ollama
EndpointUrl=http://localhost:11434
ApiKey=
Model=replace-with-an-installed-ollama-model
BatchIntervalMs=300
MaxBatchSize=8
MaxRequestTokens=8192
MaxParallelRequests=1
RetryCount=1
LogLevel=Info
LogBatchActivity=false
LogFile=
AppSummary=
AdditionalInstructions=
```

## Complete example for a 16 GB GPU

```ini
[LlmEndpoint]
Backend=Ollama
EndpointUrl=http://localhost:11434
ApiKey=
Model=replace-with-an-installed-ollama-model
BatchIntervalMs=300
MaxBatchSize=16
MaxRequestTokens=16384
MaxParallelRequests=1
RetryCount=1
LogLevel=Info
LogBatchActivity=false
LogFile=
AppSummary=
AdditionalInstructions=
```

## AppSummary examples

`AppSummary` gives the model background about what it is translating so it can pick appropriate tone and terminology. It is optional and has no hard length limit, but keep it within about 200 characters to conserve context and avoid distracting the model.

```ini
; A period drama
AppSummary=A visual novel set in Taisho-era Japan; use slightly formal, period-appropriate wording.

; A sci-fi action game
AppSummary=A fast-paced near-future sci-fi shooter; keep UI and combat terms punchy and modern.
```

To include a line break, write `\n` in the value; it is replaced with a real newline in the prompt. `\r`, `\t`, and `\\` (a literal backslash) work the same way. This replacement applies to both `AppSummary` and `AdditionalInstructions`.

```ini
AdditionalInstructions=Keep proper nouns in the original language.\nDo not change numeric units.
```

## Tuning examples

The following capacity-based values are starting points. Confirm the actual model or server context limit and measure request latency before increasing them.

| Scenario | MaxRequestTokens | MaxBatchSize | MaxParallelRequests |
|---|---:|---:|---:|
| 8 GB GPU, constrained | `4096` | `4` | `1` |
| 8 GB GPU, standard | `8192` | `8` | `1` |
| 16 GB GPU, standard | `16384` | `16` | `1` |
| Remote backend with confirmed concurrency | `16384` | `16` | `2` |

`MaxRequestTokens` does not change the provider's context limit or allocate VRAM. However, increasing the actual context configured in Ollama can increase KV cache VRAM use. Configure the provider context outside this plugin, and keep `MaxRequestTokens` at or below that actual limit. Keep `MaxParallelRequests=1` unless concurrent requests have been verified to improve end-to-end latency without overloading the backend.

## LogFile examples

```ini
; No file output
LogFile=

; Relative to the Translators directory
LogFile=LlmEndpoint.log

; Absolute path
LogFile=D:\Logs\LlmEndpoint.log
```

Relative and absolute paths are accepted. A missing parent directory is created on the first file write.
