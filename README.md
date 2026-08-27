# AI Sloth

On-demand, disposable cloud development environments for AI coding agents.

## Development

Enter the Nix development environment:

```sh
./dev
```

From `src/sandbox`, `bun run dev` applies D1 migrations to the local database and starts Wrangler.

## Deploy the sandbox

Create a [Cloudflare API token][api-tokens] with these account permissions:

- Account Settings: Read
- Workers Scripts: Edit
- Containers: Edit
- D1: Edit
- Workers R2 Storage: Edit

Then configure credentials and deploy:

```sh
cd src/sandbox
bun install
bun run setup
bun run deploy
```

`setup` validates the account ID and token, then writes them to the ignored `.env` file. `deploy` runs the checks, creates the D1 database and R2 bucket when missing, applies D1 migrations, and deploys the Worker and sandbox. Repeated deployments use the existing storage. Deployment requires a running Docker-compatible daemon.

Configure the runtime secrets after the first deployment:

```sh
bun run secret:openrouter
bun run secret:trigger
```

The first command stores the OpenRouter API key. The second stores the bearer token required to invoke the sandbox.

## Observe the sandbox

Deployments persist all Worker logs and automatic traces in **Workers & Pages → ai-sloth-sandbox → Observability**. For live debugging, run:

```sh
bunx wrangler tail ai-sloth-sandbox --format pretty
```

Tracing automatically covers Worker handlers, outbound requests, Durable Object calls, D1, and R2. Prompts and session contents are not logged by the application.

## Start a session

Set `SANDBOX_API_TOKEN` to the trigger token, then run:

```nu
(http post
  --content-type "application/json"
  --headers {Authorization: $"Bearer ($env.SANDBOX_API_TOKEN)"}
  https://ai-sloth-sandbox.<subdomain>.workers.dev/sessions
  {
    repositoryUrl: "https://github.com/owner/repository"
    branch: "main"
    prompt: "Review this repository and report the most important issue."
  }
)
```

A successful response contains the durable session ID and repository commit:

```json
{
  "sessionId": "b47f6e35-b7f3-4c6f-91f6-93f0479ec15b",
  "revision": 1,
  "commitSha": "0123456789abcdef0123456789abcdef01234567",
  "output": "...",
  "truncated": false
}
```

## Continue a session

Only the prompt is accepted when continuing. The Worker restores the latest Pi session and checks out the original commit even if the requested branch has moved:

```nu
(http post
  --content-type "application/json"
  --headers {Authorization: $"Bearer ($env.SANDBOX_API_TOKEN)"}
  https://ai-sloth-sandbox.<subdomain>.workers.dev/sessions/b47f6e35-b7f3-4c6f-91f6-93f0479ec15b/messages
  {
    prompt: "Where is that behavior tested?"
  }
)
```

Each successful message stores a new immutable Pi JSONL snapshot in R2 and records it in D1. The API returns success only after both writes complete. Concurrent messages based on the same revision cannot both commit; one receives `409 Conflict`.

Each request uses a fresh sandbox. Pi has the `read` and `bash` tools. Filesystem changes made through `bash` are disposable and are not restored by the next message; persisted sessions currently support conversation continuity against the original commit, not workspace continuity.

Repository checkout and Pi each have a five-minute timeout. Prompts are limited to 16 KiB, collected Git output to 64 KiB, response output to 1 MiB, Pi session snapshots to 16 MiB, and sessions to 100 revisions.

[api-tokens]: https://dash.cloudflare.com/?to=/:account/api-tokens&permissionGroupKeys=%5B%7B%22key%22%3A%22account_settings%22%2C%22type%22%3A%22read%22%7D%2C%7B%22key%22%3A%22workers_scripts%22%2C%22type%22%3A%22edit%22%7D%2C%7B%22key%22%3A%22containers%22%2C%22type%22%3A%22edit%22%7D%2C%7B%22key%22%3A%22d1%22%2C%22type%22%3A%22edit%22%7D%2C%7B%22key%22%3A%22workers_r2%22%2C%22type%22%3A%22edit%22%7D%5D&name=AI%20Sloth%20Sandbox%20Deploy
