# CLI

`sloth`, a Native AOT command line over a host's public HTTP API: sign in to hosts, add agent
accounts and secrets, connect GitHub and add repositories, start, follow, and steer chats, see
and prepare the setup of their files, push their changes or download their files, now or at any
checkpoint, start chats from another's checkpoint, set the instructions every agent gets, and see
or forget harness state. Its machine mode runs a workspace's nooks on the computer it runs on, and `github create-app` makes a host's GitHub App for its operator. A released sloth
updates itself from its repository's releases.

## Parts

- **`Sloth`** (`Sloth.*.cs`): the commands, one area per file (hosts, workspaces, accounts, secrets,
  GitHub, repositories, git settings, chats, instructions, harness state, machines), dispatched in
  `Sloth.cs`. Each returns its exit code: 0 done, 1 failed, 2 called wrong.
- **`HostApi`**: calls to one host as one session, or anonymously to sign in. A refusal or an
  unreachable host is an `HttpRequestException` with the host's words, which `sloth` prints.
- **`LoopbackCallback`**: where a browser comes back after signing in, at `http://127.0.0.1:<port>/auth/callback`,
  or the address pasted when the browser is on another computer; it can first serve a page the
  browser starts from, such as the form GitHub's manifest flow begins with.
- **`Terminal`**: the person at the keyboard; secrets are read without echo at a console, or from
  standard input.
- **`PrivateFile`**, **`HostsFile`**: what lasts between runs, readable by its owner only.
- **`MachineLink`** and `Bagatka.AiSloth.MachineProtocol`: machine mode, dialing the control plane's
  daemon endpoint.
- **`SlothBuild`**, **`Releases`**: what this sloth is (a release with its version and repository,
  or built from source) and its releases through GitHub's REST API, tagged `sloth-v<version>`, each
  build checked against the SHA-256 GitHub keeps for it.
- **`Program.cs`**: the composition root, the only code that reads the environment.

Depends on: a host's public HTTP API (`/.well-known/aisloth` first), `Bagatka.Sandboxing.Docker` for
machine mode.

## Files

In the operating system's per-user, non-roaming place: `$XDG_CONFIG_HOME/sloth` or `~/.config/sloth`
on Linux, `~/Library/Application Support/sloth` on macOS, `%LOCALAPPDATA%\sloth` on Windows.
`hosts.json` holds each host's session token, the person, the workspace in use, and the last chat's
choices; `machine.json` holds a machine's token; `update.json`, when sloth last looked for a newer
release. All are written whole and, on Linux and macOS, with mode 600.

## Decisions and constraints

- **Ctrl+C only leaves a chat.** The agent keeps working; stopping it is explicit (`/stop`,
  `sloth chat stop`). Anything typed while following goes to the agent at once.
- **Choices are remembered per host:** a chat runs with what the command names, else what the last
  chat ran with, else the only choice.
- **Short chat IDs are the end of the ID,** its random part; the start is a timestamp shared by chats
  started together.
- **Browser sign-ins always show their link,** and accept the address pasted back, so they work over
  SSH. The browser opens only http and https links.
- **`sloth chat` without a message starts the chat, then takes the message:** the nook starts while
  it is typed at the prompt, or reads it as a line of input.
- **Without a person at the keyboard,** `sloth chat "<message>"` ends when that message's turn ends
  and its checkpoint is taken, with exit code 1 when the turn failed, for scripts.
- **Checkpoints are `<chat>@<number>`** wherever a chat's files are named: `--from abc123@3`.
- **A setup shows one line while it runs, saying when it sets up from a ready copy, and one when it
  ended;** a failed one adds the end of its
  output, and `sloth chat setup <id>` prints all of it, following it while it runs. A chat that
  started with files without a setup suggests `sloth chat prepare <id>` after its first turn.
- **Machine mode says what nooks reach:** the internet, but not the computer, its network, or each
  other (`src/Sandboxing/README.md`, "Network").
- **Harness state is yours or the workspace's:** `sloth harness state` lists both, and
  `sloth harness state forget <harness> --shared` forgets the workspace's.
- **Asleep is invisible but shown.** `sloth chat list` marks chats whose nook sleeps; `sloth chat
  open` wakes the nook, so it's ready by the time a message is typed; any message wakes it anyway.
- **`sloth chat prepare` is a message:** it asks the chat's agent to write, run, and commit the
  setup of its files, and follows that turn like any other.
- **Updates are the person's to make.** At a keyboard, a released sloth looks for a newer release
  at most once a day while a command runs, and mentions it after; a lookup that fails or takes more
  than a second past the command says nothing. `sloth update [<version>]` replaces the file sloth
  runs from, downgrades included, and never with a download whose SHA-256 isn't GitHub's.
- **A nearly full disk is told** after the turn that left it so.
