# Contribute to AiSloth

AiSloth is open source under the [MIT license](LICENSE), and we welcome your changes.
[ARCHITECTURE.md](ARCHITECTURE.md) shows how the parts fit together. [AGENTS.md](AGENTS.md) tells
how we build, for people and agents alike.

## What you need

- A computer with Linux, or Windows with WSL.
- [Nix](https://nixos.org/download/), which gives you every other tool.
- Docker Engine 28 or later, with [Sysbox](https://github.com/nestybox/sysbox). Nooks run their own Docker, and
  Sysbox lets them do that without special rights on your computer. Nix cannot install Sysbox, so
  step 2 tells you how.

## 1. Get the code and the tools

1. Get the code:

   ```sh
   git clone https://github.com/bagatka/ai-sloth.git
   cd ai-sloth
   ```

2. Open the AiSloth shell:

   ```sh
   ./dev
   ```

   This opens Nushell with .NET 11 RC1, the Docker CLI, Git, clang for Native AOT, and the Azure
   CLI. The first time, it downloads them, which can take some minutes. Do all the next steps in
   this shell.

3. Install Aspire, which runs AiSloth on your computer:

   ```sh
   dotnet tool restore
   ```

## 2. Get Docker with Sysbox

- **Linux:** install Docker Engine and the Sysbox package.
- **Windows:** the engine of Docker Desktop cannot use Sysbox. In WSL, run a second Docker Engine
  (`dockerd`) with Sysbox, beside Docker Desktop.
- **Mac:** the engine must run in a Linux virtual machine. Nobody has tried this yet.

Sysbox 0.7.1 sometimes stops as it starts, because `sysbox-fs` hangs and systemd stops it after
10 seconds. Tell systemd to start it again. Run this before you install the Sysbox package:

```sh
sudo mkdir -p /etc/systemd/system/sysbox-fs.service.d && printf '[Service]\nRestart=on-failure\nRestartSec=1\n' | sudo tee /etc/systemd/system/sysbox-fs.service.d/restart.conf
```

If you installed the package already, run `sudo systemctl daemon-reload` after it.

If your engine with Sysbox is not the default one, point `DOCKER_HOST` at it. The AppHost, the tests,
and `sloth machine run` use it, as the `docker` command does. In Nushell:

```sh
$env.DOCKER_HOST = "unix:///run/<your engine>.sock"
```

## 3. Run AiSloth on your computer

Here you are two people: the host's operator, who starts and sets up the host, and its first user,
who then uses it like anyone else. Only the operator's steps touch the host's settings; everything a
user does is in the web app and `sloth`.

### Set up the host, as its operator

1. Start everything: PostgreSQL, the database migrations, the nook image, and the host.

   ```sh
   dotnet aspire run
   ```

   The first build of the nook image takes some minutes. The database keeps its data between runs,
   in a Docker volume whose name ends in `postgres-data`. To start over, stop AiSloth and remove
   that volume.

2. Optional: to work in GitHub repositories, make the host's GitHub App, give its three values to
   the AppHost, and start AiSloth again:

   ```sh
   ./host github-app http://localhost:5170
   dotnet user-secrets set Parameters:github-app-client-id <client ID> --project src/Aspire/Bagatka.AiSloth.AppHost
   dotnet user-secrets set Parameters:github-app-client-secret <client secret> --project src/Aspire/Bagatka.AiSloth.AppHost
   dotnet user-secrets set Parameters:github-app-slug <slug> --project src/Aspire/Bagatka.AiSloth.AppHost
   ```

3. Optional: people always sign in with codes. To sign them in with an OpenID Connect provider too,
   such as a WorkOS staging environment, give the AppHost these settings with
   `dotnet user-secrets set`, as above: `Parameters:sign-in-provider-issuer`, `-client-id`,
   `-client-secret`, and `-name`.
4. Open the dashboard link that `dotnet aspire run` shows. Go to the console logs of `webapi`, and
   find the line that starts with `First sign-in:`. Until someone signs up, it has the setup code.

### Use it, as anyone would

Sign in with the setup code: in the web app at `http://localhost:5170/app/`, or with the command
line from the source code, in place of `sloth`:

```sh
dotnet run --project src/Cli/Bagatka.AiSloth.Cli -- host add http://localhost:5170 --code <setup code>
dotnet run --project src/Cli/Bagatka.AiSloth.Cli -- help
```

Agent accounts, GitHub, repositories, secrets, and instructions are in the web app's settings and in
`sloth help`.

## 4. Check your change

| What | Command |
|---|---|
| Build | `dotnet build AiSloth.slnx` |
| The journey closest to your change | `dotnet test --project tests/Bagatka.AiSloth.EndToEndTests --filter-class "*.ChatJourney"` |
| All tests | `dotnet test --solution AiSloth.slnx` |
| Format | `dotnet format AiSloth.slnx --verify-no-changes` |
| The web app, in `src/Apps/Web` | `npm run lint`, `npm run format:check`, and `npm run build` |

The journeys serve the web app as last built, so build it before `WebJourney` and `WebSetupJourney`.

On each push to `main`, CI runs all the journeys. Pushes that change only the landing page or the
docs skip them.

## Your own coding tools

The flake does not manage personal coding tools, such as Claude Code or Codex. Install and update
them with their own installers. `./dev` adds `$HOME/.local/bin` to your PATH and keeps the PATH you
had, so these tools stay available. To use them outside `./dev` too, add `$HOME/.local/bin` to the
PATH in the startup file of your shell.

## The web app

The web app is in `src/Apps/Web` ([its README](src/Apps/Web/README.md)). With AiSloth running, work
on it with hot reload:

```sh
cd src/Apps/Web
npm ci
npm run dev
```

Open `http://localhost:5173/app/`, and sign in with a link code:

```sh
dotnet run --project src/Cli/Bagatka.AiSloth.Cli -- host link
```

The host serves the app at `/app/` too, as it was built when the host started.

## The landing page

The landing page at [aisloth.dev](https://aisloth.dev) is an Astro site in `site/`. To work on it:

```sh
cd site
npm ci
npm run dev
```

When a push to `main` changes it, the `Site / Publish` workflow publishes it to GitHub Pages. The
custom domain is a setting of the repository's Pages.

## Release

- **sloth:** run the `CLI / Release` workflow in Actions with the new version, or run
  `gh workflow run cli-release.yml -f version=<version>`.
- **The host:** `main` deploys itself after each push. See
  [docs/self-hosting.md](docs/self-hosting.md#optional-deploy-from-github-on-each-push).

## Agent environments

### Amp orbs

- `.agents/setup` installs Nix for one user, prepares the locked flake, and restores the locked NuGet
  packages.
- Amp saves the installed tools and caches, and new orbs use them again. When the saved copy is old,
  the setup runs again. It is safe to run more than one time.
- A change to the setup has an effect on new orbs only after it is on the default branch.
- The setup adds a login-shell hook for this repository. Agents and their services get the full Nix
  environment without `./dev`. For example, `dotnet build AiSloth.slnx --no-restore` works in a new
  login shell. Local development does not change.
- No resume script is necessary, because there is no sign-in or service state to repair.
- The flake gives the Docker client, not a Docker daemon. Work with containers needs an engine that
  you configure separately. The setup does not start application servers.
