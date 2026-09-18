# AI Sloth

On-demand, disposable cloud development environments for AI coding agents.

## Development

Install [Nix](https://nixos.org/download/), then run `./dev` to enter Nushell with
.NET 11 RC1, Docker CLI, and Git.

`nix/dotnet-11-rc1.nix` pins RC1 while nixpkgs still packages preview 7.

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
