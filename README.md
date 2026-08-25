# AI Sloth

On-demand, disposable cloud development environments for AI coding agents.

## Development environment

With [Nix](https://nixos.org/download/) installed, enter the development environment after cloning:

```sh
./dev
```

## Cloudflare

Create an [AI Sloth Sandbox Deploy API token](https://dash.cloudflare.com/?to=/:account/api-tokens&permissionGroupKeys=%5B%7B%22key%22%3A%22account_settings%22%2C%22type%22%3A%22read%22%7D%2C%7B%22key%22%3A%22workers_scripts%22%2C%22type%22%3A%22edit%22%7D%2C%7B%22key%22%3A%22containers%22%2C%22type%22%3A%22edit%22%7D%5D&name=AI%20Sloth%20Sandbox%20Deploy). Select the target account, confirm the prefilled permissions, create the token, and copy it. The permissions allow reading the account identity, deploying the Worker, and deploying its sandbox container—nothing else.

Then provide the [account ID](https://developers.cloudflare.com/fundamentals/account/find-account-and-zone-ids/) and token when prompted:

```sh
bun install --cwd src/sandbox
bun run --cwd src/sandbox setup
bun run --cwd src/sandbox deploy
```

The setup command validates the credentials and saves them in the ignored `src/sandbox/.env` file. Deployment requires a running Docker-compatible daemon.

After the first deployment, store the OpenRouter key and a strong random trigger token as encrypted Worker secrets:

```sh
bun run --cwd src/sandbox secret:openrouter
bun run --cwd src/sandbox secret:trigger
```

Keep the trigger token: callers send it as a bearer token. Trigger an agent run with:

```nu
http post \
  --content-type application/json \
  --headers {Authorization: $"Bearer ($env.SANDBOX_API_TOKEN)"} \
  https://ai-sloth-sandbox.<subdomain>.workers.dev/run \
  {
    url: "https://github.com/owner/repository"
    branch: "main"
    prompt: "Review this repository and report the most important issue."
  }
```

Each request gets a fresh sandbox, shallow-clones the selected public GitHub branch, and runs Pi with OpenRouter's `openai/gpt-5.6-luna` model and low thinking. Runs are limited to ten minutes and 1 MiB of output. The container is destroyed afterward; a 30-second idle timeout is the fallback cleanup.

Each deployable service has its own Wrangler configuration and pinned Wrangler version, so these commands only affect the sandbox.
