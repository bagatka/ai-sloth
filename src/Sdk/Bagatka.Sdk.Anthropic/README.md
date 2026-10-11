# Bagatka.Sdk.Anthropic

The part of Anthropic's API a product uses besides model calls, which go through a model gateway as
they are: the models it serves. The official SDK covers this, but it's a large dependency for one
call.

- **Docs:** [List Models](https://docs.anthropic.com/en/api/models-list), version `2023-06-01`, as of
  October 2026.
- **Endpoints:** `GET /v1/models`, on Anthropic's API or another endpoint that speaks it.
- **Auth:** the caller's credentials, such as `x-api-key`.
- **Rate limits:** the account's own; a list is one call.
- **No retries:** the list is cheap for the caller to read again. An unexpected answer throws
  `HttpRequestException` with its status code.

## Quirks

- The list is paged; one page of 1,000 holds every model Anthropic serves.
- Every model it lists can call tools.
