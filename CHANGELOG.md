# Changelog

All notable changes to this project will be documented in this file.

The format is based on Keep a Changelog, and this project adheres to Semantic Versioning.

## [Unreleased]

### Added

- Added an 8 GB GPU baseline profile with an 8K request budget, eight-item batch cap, and one active request.
- Added configurable context, batch-count, and parallel-request limits without splitting Japanese or Chinese input strings.
- Added automatic use of conventional provider API key environment variables.
- Added an optional log level with Info as the default and per-translation details at Debug.
- Added an independent, default-off batch activity switch with correlated request/response metadata and auto-scaled elapsed times.

### Changed

- Simplified the public configuration to connection, language, batching, retry, and translation-quality settings.
- Made structured output, safe failure fallback, logging, provider headers, and optional SDK parameters automatic.
- Changed logging to use the console only by default, with optional file output sharing the same log level.
- Removed the independent character-count batch limit; batches now admit at least one whole item and add further items while the estimated request fits `MaxRequestTokens`.
- Stopped sending Ollama `num_ctx`; `MaxRequestTokens` controls only the plugin's internal request budget and must not exceed the provider's actual context limit.
- Removed endpoint-specific source and target language overrides; language values now always come directly from XUnity.AutoTranslator.
- Removed the separate glossary input; `AdditionalInstructions` is the only prompt customization setting.
- Set the default batch collection interval to 300 ms to coalesce sequential messages while keeping the idle-request delay bounded.
- Split the README and configuration reference into mutually linked English and Japanese editions.
- Documented 8 GB and 16 GB capacity references while distinguishing the plugin request budget from provider context and KV cache VRAM use.
- Changed `EndpointUrl` to a provider base URL, with defaults of `http://localhost:11434`, `https://api.openai.com/v1`, and `https://api.anthropic.com`.
- Removed plugin-side HTTP response and overall-operation timeouts so long-running local generation can complete.
- Removed arbitrary upper limits from batch interval, request budget, retry count, and additional instructions; limits that are structurally tied to endpoint concurrency remain.
- Stopped truncating neighboring context by character count and now keeps the selected context strings whole.
- Replaced Anthropic's fixed 1024-token output limit with a per-request output estimate.
- Replaced provider SDK dependencies with direct HTTP implementations so all three backends follow their current API specifications without requiring outdated SDK versions.
- Reworked the README files as user-facing installation and configuration guides.
