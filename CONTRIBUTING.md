# Contributing

AiSloth is open source under the [MIT license](LICENSE), and contributions are welcome. The
[system map](ARCHITECTURE.md) shows how the code fits together, and [AGENTS.md](AGENTS.md) is how
we build it, for people and agents alike.

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

## Releasing

The `CLI / Release` workflow releases sloth from main, run with its version in Actions or with
`gh workflow run cli-release.yml -f version=<version>`.

The landing page at aisloth.dev is an Astro site in `site/`: run `npm ci` and `npm run dev` there to
work on it. The `Site / Publish` workflow publishes it to GitHub Pages whenever a push to main
changes it; the custom domain is a setting of the repository's Pages.

## Agent environments

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
