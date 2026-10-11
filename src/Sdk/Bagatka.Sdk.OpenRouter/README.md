# Bagatka.Sdk.OpenRouter

The part of OpenRouter's API a product uses besides model calls, which go through a model gateway as
they are: the catalog of the models OpenRouter offers. There is no official .NET SDK.

- **Docs:** [List all models](https://openrouter.ai/docs/api-reference/list-available-models), as of
  October 2026.
- **Endpoints:** `GET /v1/models`.
- **Auth:** none; the catalog is public.
- **Rate limits:** none published for the catalog.
- **No retries:** the catalog is cheap for the caller to read again. An unexpected answer throws
  `HttpRequestException` with its status code.

## Quirks

- The catalog lists about 450 models in about 750 KB, and changes often; it isn't paged.
- Prices are dollars per token, as decimal strings; its router's are -1, as it prices each call.
- Models come in variants with a suffix, such as `:batch` for batch processing and `:free`, each
  listed as a model of its own.
- Names start with the vendor, such as `OpenAI: GPT-6 Luna`.
- IDs starting with `~`, such as `~anthropic/claude-sonnet-latest`, follow the vendor's newest model.
