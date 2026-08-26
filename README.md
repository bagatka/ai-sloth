# AI Sloth

On-demand, disposable cloud development environments for AI coding agents.

## Development

Enter the Nix development environment:

```sh
./dev
```

## Deploy the sandbox

Create a [Cloudflare deployment token][deploy-token] with these permissions:

- Account Settings: Read
- Workers Scripts: Edit
- Containers: Edit

Then run from the repository root:

```sh
cd src/sandbox
bun install
bun run setup
bun run deploy
```

`setup` validates the account ID and token, then writes them to the ignored `.env` file. Deployment requires a running Docker-compatible daemon.

Configure the runtime secrets after the first deployment:

```sh
bun run secret:openrouter
bun run secret:trigger
```

The first command stores the OpenRouter API key. The second stores the bearer token required to invoke the sandbox.

## Run an agent

Set `SANDBOX_API_TOKEN` to the trigger token, then run:

```nu
(http post
  --content-type "application/json"
  --headers {Authorization: $"Bearer ($env.SANDBOX_API_TOKEN)"}
  https://ai-sloth-sandbox.<subdomain>.workers.dev/run
  {
    repositoryUrl: "https://github.com/owner/repository"
    branch: "main"
    prompt: "Review this repository and report the most important issue."
  }
)
```

A successful response contains `output` and `truncated`. Execution failures contain `error` and may include `details`.

Each request:

1. Creates a fresh sandbox.
2. Shallow-clones the requested branch.
3. Runs Pi with OpenRouter's `openai/gpt-5.6-luna` model and low thinking.
4. Destroys the sandbox.

Clone and agent processes each have a five-minute timeout. Collected clone output is limited to 64 KiB and agent output to 1 MiB. A 30-second idle timeout provides fallback cleanup.

[deploy-token]: https://dash.cloudflare.com/?to=/:account/api-tokens&permissionGroupKeys=%5B%7B%22key%22%3A%22account_settings%22%2C%22type%22%3A%22read%22%7D%2C%7B%22key%22%3A%22workers_scripts%22%2C%22type%22%3A%22edit%22%7D%2C%7B%22key%22%3A%22containers%22%2C%22type%22%3A%22edit%22%7D%5D&name=AI%20Sloth%20Sandbox%20Deploy
