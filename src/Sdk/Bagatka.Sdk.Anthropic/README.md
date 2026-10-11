# Bagatka.Sdk.Anthropic

The part of Anthropic's API a product uses besides model calls, which go through a model gateway as
they are: the models it serves (`AnthropicModelsClient`), and for a Claude plan, Claude Code's own
sign-in (`ClaudeSignInClient`) and the plan's usage windows (`ClaudePlanUsageClient`). The official
SDK covers only the list of models, and it's a large dependency for one call.

- **Docs:** [List Models](https://docs.anthropic.com/en/api/models-list), version `2023-06-01`, as of
  October 2026. Claude Code's sign-in and `/api/oauth/usage` are undocumented: read from Claude Code
  2.1.
- **Endpoints:** `GET /v1/models`, on Anthropic's API or another endpoint that speaks it. Claude
  Code's sign-in: the person approves at `claude.com/cai/oauth/authorize` (PKCE, Claude Code's
  client ID), whose page shows `code#state`; `POST platform.claude.com/v1/oauth/token` exchanges the
  code and renews, as JSON. `GET /api/oauth/usage` with a plan's token.
- **Auth:** the caller's credentials, such as `x-api-key`; a plan's token as a bearer token with the
  `oauth-2025-04-20` beta.
- **Rate limits:** the account's own; each answer is one call.
- **No retries:** each is cheap for the caller to ask again; a renewal must never run twice. An
  unexpected answer throws `HttpRequestException` with its status code; a refused code or renewal is
  a failed `Result`.

## Quirks

- The list is paged; one page of 1,000 holds every model Anthropic serves.
- Every model it lists can call tools.
