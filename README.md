# AI Sloth

On-demand, disposable cloud development environments for AI coding agents.

## Development

Install [Nix](https://nixos.org/download/), then run `./dev` to enter Nushell with
.NET 11 RC1, the Docker CLI, Git, and clang for Native AOT. Run `dotnet tool restore` once for the
pinned Aspire CLI, then `dotnet aspire run` starts the app locally. Anything that runs containers,
including the tests, needs a Docker engine, which Nix can't provide: use Docker Desktop (with WSL
integration on Windows), OrbStack, Colima, or a system `dockerd`.

Personal coding tools (Claude Code, Codex, etc.) are not managed by the flake.
Install and update them using their own installers. `./dev` includes
`$HOME/.local/bin` in PATH and retains the inherited PATH, so user-installed tools
remain available across shell sessions and WSL restarts without Nix updates.
For access outside `./dev`, include `$HOME/.local/bin` in your shell's startup PATH
as well.

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
