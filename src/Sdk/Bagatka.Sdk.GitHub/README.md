# Bagatka.Sdk.GitHub

The parts of GitHub a GitHub App acting as a person uses: the device flow that signs the person in,
renewing their user access token, their installations and repositories, and pull requests.
Octokit.NET covers these, but it is a large, reflection-based library for the nine calls used here;
this client keeps to them, with source-generated JSON.

- **Docs:** [Generating a user access token for a GitHub App](https://docs.github.com/apps/creating-github-apps/authenticating-with-a-github-app/generating-a-user-access-token-for-a-github-app)
  (device flow and refreshing), [Refreshing user access tokens](https://docs.github.com/apps/creating-github-apps/authenticating-with-a-github-app/refreshing-user-access-tokens),
  and the REST API's installations, repositories, users, and pulls endpoints, version `2022-11-28`,
  as of October 2026.
- **Endpoints:** `POST /login/device/code` and `POST /login/oauth/access_token` on the site;
  `GET /user`, `GET /users/{login}`, `GET /user/installations`,
  `GET /user/installations/{id}/repositories`, `GET /repos/{owner}/{repo}`, and
  `GET`/`POST /repos/{owner}/{repo}/pulls` on the API.
- **Auth:** the device flow takes only the app's client ID; renewing takes its client secret too.
  REST calls carry the person's token as `Authorization: Bearer`.
- **Rate limits:** 5,000 requests an hour per person's token; the device flow's polling interval,
  which `slow_down` lengthens.
- **No retries:** the OAuth calls change state at GitHub, and the reads are cheap for the caller to
  repeat. An unexpected answer throws `HttpRequestException` with its status code.

## Quirks

- The device flow and the token endpoint answer errors with HTTP 200 and an `error` field.
- User access tokens last 8 hours and refresh tokens 6 months when the app's "Expire user
  authorization tokens" setting is on (the default); each renewal replaces the refresh token. With it
  off, tokens never expire and come without a refresh token.
- A user access token reaches only repositories both the person and the app's installations can.
- An app's bot is the user `<slug>[bot]`; its ID makes the no-reply address commits credit it by.
- The device flow must be turned on in the app's settings; the manifest flow can't.
