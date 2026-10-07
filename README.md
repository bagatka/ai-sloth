# AI Sloth

On-demand, disposable cloud development environments for AI coding agents.

## Installing sloth

Run `curl -fsSL https://aisloth.dev/install.sh | sh`, which installs sloth in `~/.local/bin`, or
updates the sloth on your PATH, from the
[latest release](https://github.com/bagatka/ai-sloth/releases/latest): Linux x64 or arm64, or
macOS on Apple silicon; on Windows, run it in WSL. It keeps the download only when its SHA-256 is the
one GitHub has for it. `sloth update` installs newer releases too, which sloth mentions once they're
out.

## Development

Install [Nix](https://nixos.org/download/), then run `./dev` to enter Nushell with
.NET 11 RC1, the Docker CLI, Git, clang for Native AOT, and the Azure CLI. Run `dotnet tool restore` once for the
pinned Aspire CLI, then `dotnet aspire run` starts the app locally: PostgreSQL, the migrations, the
nook image (the first build takes a few minutes), and the WebApi. While nobody has signed up, the
WebApi prints a setup code in its console output; sign in with it as the host's first person:
`dotnet run --project src/Cli/Bagatka.AiSloth.Cli -- host add <url> --code <code>`, then
`... -- help` for what `sloth` does. To sign people in with an OpenID Connect provider as well, such
as a WorkOS staging environment, give the AppHost `Parameters:sign-in-provider-issuer`,
`-client-id`, `-client-secret`, and `-name` with
`dotnet user-secrets set <name> <value> --project src/Aspire/Bagatka.AiSloth.AppHost`. For chats
to start with your GitHub repositories, make the host's GitHub App once with
`... -- github create-app`, turn on its device flow at GitHub as it says, and give the AppHost
`Parameters:github-app-client-id`, `-client-secret`, and `-slug` the same way. Anything that runs nooks,
including the tests, needs a Docker engine with [Sysbox](https://github.com/nestybox/sysbox), which
Nix can't provide: nooks run Docker of their own, and Sysbox lets them do it without privileges on
the host. On Linux, install Docker and Sysbox's package; on Windows, run a `dockerd` with Sysbox
inside WSL, beside Docker Desktop, whose engine can't have it; on a Mac, the engine has to run in a
Linux VM of your own (not tried yet). Point `DOCKER_HOST` at that engine when it isn't the default
one: the AppHost, the tests, and `sloth machine run` use it, as the `docker` command does. Sysbox
0.7.1's `sysbox-fs` now and then hangs as it starts, and systemd stops it after 10 seconds; have
systemd start it again, before installing the package or followed by `sudo systemctl daemon-reload`:
`sudo mkdir -p /etc/systemd/system/sysbox-fs.service.d && printf '[Service]\nRestart=on-failure\nRestartSec=1\n' | sudo tee /etc/systemd/system/sysbox-fs.service.d/restart.conf`.

Personal coding tools (Claude Code, Codex, etc.) are not managed by the flake.
Install and update them using their own installers. `./dev` includes
`$HOME/.local/bin` in PATH and retains the inherited PATH, so user-installed tools
remain available across shell sessions and WSL restarts without Nix updates.
For access outside `./dev`, include `$HOME/.local/bin` in your shell's startup PATH
as well.

## Deploying

`dotnet aspire deploy` deploys to Azure, into one resource group: the WebApi in Container Apps, nooks
in a Container Apps sandbox group, and checkpoints in Blob Storage. Sign in with `az login` first.
It asks for the subscription, resource group, and region, or takes them as `Azure__SubscriptionId`,
`Azure__ResourceGroup`, and `Azure__Location`. Give the AppHost the nook images' public repository
and tag, `Parameters:nook-image-repository` and `Parameters:nook-image-tag`: every push to main
publishes them on GHCR as `ghcr.io/<owner>/aisloth-nook*`, tagged with its commit, so a fork gets
its own. Give `Parameters:postgres-connection-string` to use a Postgres of your own; without it, the
deployment gets an Azure Database for PostgreSQL server.
A deployment also needs three keys that encrypt secrets at rest, `Parameters:agent-accounts-key`,
`-sources-key`, and `-secrets-key`: make each once with `openssl rand -hex 32` and keep them, since
losing one makes what it encrypted unreadable. Aspire keeps every parameter's value in its state,
`~/.aspire/deployments`, and a kept value wins over an environment variable: change one there, or
pass `-- --Parameters:<name>=<value>`. The WebApi prints the first sign-in's setup code in its
log (`az containerapp logs show --name webapi --resource-group <group>`). For a custom domain, add
its records and managed certificate with `az containerapp hostname add` and `bind`, then deploy with
`Parameters:custom-domain` and `Parameters:custom-domain-certificate`, the certificate's name.

From GitHub, the `Control plane / Deploy` workflow deploys every push to main once its journeys pass
and its nook images are published; a newer push replaces one still waiting, and a deploy that started
always finishes. Run it by hand in Actions or with `gh workflow run control-plane-deploy.yml` to deploy
main again. Its `production` environment, limited to main, holds the
deployment's settings: the secrets `AZURE_CLIENT_ID`, `AZURE_TENANT_ID`, and
`AZURE_SUBSCRIPTION_ID` of an Entra app that trusts the environment's GitHub token and may manage
the resource group and assign its roles, `AZURE_RESOURCE_GROUP`, `AZURE_LOCATION`, the three keys as
`AGENT_ACCOUNTS_KEY`, `SOURCES_KEY`, and `SECRETS_KEY`, and `POSTGRES_CONNECTION_STRING`; and the
variables `CUSTOM_DOMAIN`, `CUSTOM_DOMAIN_CERTIFICATE`, and `ALLOW_CHATGPT_PLANS`.

The `CLI / Release` workflow releases sloth from main, run with its version in Actions or with
`gh workflow run cli-release.yml -f version=<version>`.

The landing page at aisloth.dev is an Astro site in `site/`: run `npm ci` and `npm run dev` there to
work on it. The `Site / Publish` workflow publishes it to GitHub Pages whenever a push to main
changes it; the custom domain is a setting of the repository's Pages.

### Amp orbs

`.agents/setup` installs single-user Nix, materializes the existing locked flake,
and restores locked NuGet packages. Amp snapshots the installed tools and caches
for reuse by fresh orbs; stale snapshots rerun the idempotent setup. Setup changes
must reach the project's default branch before future orbs use them.

The setup adds a repository-scoped login-shell hook so agents and supervised
services receive the full Nix development environment without running `./dev`.
After setup, commands such as `dotnet build AiSloth.slnx --no-restore` work directly
in new login shells within the repository. Local development is unchanged.

No resume script is needed: there is no authentication or service state to repair.
The flake supplies the Docker client, not a Docker daemon; container workloads
would need a separately configured engine. Setup does not launch application
servers.
