# Configuration reference

English | [日本語](CONFIGURATION-ja.md)

The configuration section is `[LlmEndpoint]`. Write key names with the capitalization shown below.

## All settings

| Key | Default | Allowed values or limit | Description |
|---|---:|---|---|
| `Backend` | `Ollama` | `Ollama`, `OpenAI`, `Anthropic` | API family to use. |
| `EndpointUrl` | Backend-specific | Absolute HTTP or HTTPS base URL | The plugin appends the backend API path to this base URL. |
| `ApiKey` | Empty | String | Conditional: set it only for backends that require authentication. When empty, the conventional environment variable for the selected backend is used. |
| `Model` | Empty | Non-empty model ID | **Required.** Selects the provider model. A name containing `hy-mt2` activates its model-specific profile. |
| `BatchIntervalMs` | `300` | Integer `0` or greater | Maximum collection time measured from the first queued item. The batch closes immediately at `MaxBatchSize`. |
| `MaxBatchSize` | `5` | `1`-`50` | Maximum items per request. `MaxRequestTokens` can reduce the actual count. Larger batches take longer; keep it small so a single batch stays well under the host's per-translation timeout (~150 s) on slow backends. |
| `MaxRequestTokens` | `8192` | Integer `1` or greater | Conservative estimated token budget used only to assemble a request. It is not an LLM context setting and must not exceed the actual provider context limit. |
| `MaxParallelRequests` | `1` | `1`-`50` | Maximum real HTTP requests in progress. Increase only when the backend supports useful concurrent generation. |
| `MaxConcurrency` | `10` | `1`-`50` | Translations buffered in flight from XUnity (`ITranslateEndpoint.MaxConcurrency`). **Do not set this too high.** On a slow backend, a large buffer makes trailing items wait past the host's per-translation timeout (~150 s); they are then discarded even though they were translated. Keep it in line with throughput (roughly under `150 s × MaxBatchSize ÷ seconds-per-batch`). |
| `RequestTimeoutMs` | `300000` | Integer `0` or greater | Timeout in milliseconds for a whole request (connect, generation wait, and response read). `0` disables it (unbounded); when disabled, a network drop without FIN/RST can block a request forever. |
| `RetryCount` | `1` | Integer `0` or greater | Retries after the initial attempt for transient HTTP or transport failures. |
| `LogLevel` | `Info` | `Debug`, `Info`, `Warn`, `Error`, `Off` | Minimum log level. Logging is emitted only when `LogFile` is set. |
| `LogBatchActivity` | `true` | `true`, `false` | Enables Info-level batch request and response logging. Requires `LogFile` (nothing is written without it). |
| `LogTranslationItems` | `false` | `true`, `false` | Enables Info-level per-item logging of each request source text and each resulting translation. Verbose; requires `LogFile`. |
| `LogFile` | Empty | File path | Empty disables all logging (nothing is written to the console either). Relative paths use the `Translators` directory. The actual file is split per day: `…\LLM.log` is written as `…\LLM-yyyyMMdd.log` (local date). If the target is locked, a numeric fallback such as `…\LLM-yyyyMMdd-1.log` is used. |
| `LogRetentionDays` | `7` | Integer `0` or greater | Retention window for the dated log files. Files dated older than this are deleted. `0` keeps them forever. |
| `AppSummary` | Empty | String | Optional background about the app or game (setting, era, genre) added to the system message to improve translation nuance. No hard length limit; keep it within about 200 characters to save context and avoid model distraction. |
| `AdditionalInstructions` | Empty | String | Trusted translation guidance appended to the system message and kept separate from source text. No hard length limit; keep it within about 200 characters for the same reason. |

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
MaxBatchSize=5
MaxRequestTokens=8192
MaxParallelRequests=1
MaxConcurrency=10
RequestTimeoutMs=300000
RetryCount=1
LogLevel=Info
LogBatchActivity=true
LogTranslationItems=false
LogFile=
LogRetentionDays=7
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
MaxBatchSize=8
MaxRequestTokens=16384
MaxParallelRequests=1
MaxConcurrency=10
RequestTimeoutMs=300000
RetryCount=1
LogLevel=Info
LogBatchActivity=true
LogTranslationItems=false
LogFile=
LogRetentionDays=7
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

| Scenario | MaxRequestTokens | MaxBatchSize | MaxParallelRequests | MaxConcurrency |
|---|---:|---:|---:|---:|
| 8 GB GPU, constrained (slow) | `4096` | `3` | `1` | `6` |
| 8 GB GPU, standard | `8192` | `5` | `1` | `10` |
| 16 GB GPU, standard | `16384` | `8` | `1` | `12` |
| Remote backend with confirmed concurrency | `16384` | `8` | `2` | `16` |

Size `MaxBatchSize` and `MaxConcurrency` against the host's per-translation timeout (~150 s): keep `MaxBatchSize` small enough that a single batch finishes well under 150 s, and `MaxConcurrency` small enough that items at the back of the buffer are processed within 150 s. Measure your actual seconds-per-batch and keep both conservative (smaller for slower models) — oversizing means translations are produced but discarded before they can be shown.

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
