# Bagatka.Sdk.OpenAI

Sign in with ChatGPT for open-source and self-hosted apps: the OAuth flow whose tokens call the
OpenAI API on a person's ChatGPT plan. OpenAI's official .NET SDK calls the API but doesn't cover this
sign-in.

- **Docs:** [Sign in with ChatGPT, ChatGPT plan usage](https://developers.openai.com/siwc/token-sharing-open-source)
  (registration and sign-in, token reference, errors and recovery, preview limitations), as of
  October 2026.
- **Endpoints:** `GET /api/accounts/authorize` (built as a URL for the person's browser),
  `POST /api/accounts/oauth/token` (code exchange and refresh), `POST /api/accounts/oauth/revoke`, at
  `https://auth.openai.com`.
- **Auth:** a public client with PKCE (S256) and no secret. A first sign-in registers the app with the
  client ID `dynamic_agent_client`, the app's name as `agent_name_hint`, and the installation's
  `ext_agent_host_id`; the callback returns the client ID it issued, which every later call uses. The
  scopes are `openid profile email offline_access resource.invoke chatgpt.tokens.use.direct`, for the
  resource `https://api.openai.com/v1`.
- **Rate limits:** none documented for the authorization server. Plan usage has a five-hour limit on
  Plus, shared with every app, and a weekly cap per app the person sets.
- **No retries:** every call changes state at OpenAI (a code works once, a refresh replaces the
  refresh token), so the client owns one `HttpClient` and retries nothing.

## Quirks

- The redirect must be an HTTP loopback address on `127.0.0.1` with the path `/auth/callback`; only
  the port may change, and `localhost` isn't accepted. A remote host signs in through a browser on
  the person's own computer and takes the tokens over.
- Access tokens last an hour. Refresh tokens last 30 days and are replaced on every refresh; a used
  one is refused (`refresh_token_reused`), so refreshes of one token set must never run at the same
  time. In October 2026 a used one was still accepted once right after its refresh; don't rely on it.
- Token responses carry `earliest_refresh_at`, when OpenAI would like the next refresh: about 54
  minutes into the hour.
- Refusals are OAuth errors with status 400 or 401 and a JSON `error`; the client returns them as
  `openai.<error>` and throws for anything else.
- The ID token isn't checked for its signature here: it comes straight from the token endpoint over
  TLS, which OpenID Connect accepts in place of it. Callers check its issuer, audience, nonce, and
  expiry.

## Tests

Through AgentAccounts' end-to-end tests, against a fake authorization server that is strict where
OpenAI's is (`tests/Bagatka.AiSloth.EndToEndTests/FakeChatGpt.cs`). The client was tried once against
the real server with a real ChatGPT plan, in October 2026.
