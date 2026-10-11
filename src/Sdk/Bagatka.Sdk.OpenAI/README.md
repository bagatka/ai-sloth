# Bagatka.Sdk.OpenAI

A ChatGPT plan, signed in to the way Codex signs in, and the OpenAI API's models. OpenAI's official
.NET SDK calls the API but covers neither.

- **Codex's sign-in** (`CodexSignInClient`), as `codex login --device-auth` does it, for Codex's own
  OAuth client `app_EMoamEEZ73f0CkXaXp7hrann` at `https://auth.openai.com`:
  `POST /api/accounts/deviceauth/usercode` (a code the person enters at `/codex/device`),
  `POST /api/accounts/deviceauth/token` (403 or 404 until they approve, then an authorization code
  and PKCE verifier), `POST /oauth/token` (the code exchanged, as a form, with the redirect
  `/deviceauth/callback`; renewals as JSON with the scope `openid profile email`). Undocumented:
  Codex's own source (`codex-rs/login`) is the reference.
- **ChatGPT's Codex backend** at `https://chatgpt.com/backend-api/codex`: the Responses API for
  Codex's calls, and `GET /models?client_version=…`, the catalog Codex shows (`models`, each with a
  `slug`, a `display_name`, a `visibility`, its `supported_reasoning_levels`, and its
  `context_window`). Calls carry the plan's token and its ChatGPT account (`ChatGPT-Account-Id`, from
  the ID token's `https://api.openai.com/auth` claim).
- **The plan's usage** (`ChatGptUsageClient`): `GET https://chatgpt.com/backend-api/wham/usage`, as
  Codex's `/status` reads it: `rate_limit.primary_window` and `secondary_window`, each with
  `used_percent`, `limit_window_seconds`, and `reset_at`.
- **Models of the API** (`OpenAIModelsClient`): `GET /models` at `https://api.openai.com/v1`, or
  another endpoint that speaks it, with an API key.
- **No retries:** a code works once and a renewal replaces the refresh token, so every client owns one
  `HttpClient` and retries nothing.

## Quirks

- The code's interval comes as a string; a code lasts fifteen minutes.
- Renewals answer without `expires_in`: the access token's own `exp` claim says when it expires, about
  an hour after it was issued. A renewal may leave out the refresh token, which then stays.
- Refresh tokens are replaced on every renewal and a used one is refused (`refresh_token_reused`), so
  renewals of one sign-in must never run at the same time.
- `GET /models` answers an API key with every model the API serves (`data`, IDs only), for speech,
  images, and embeddings too.
