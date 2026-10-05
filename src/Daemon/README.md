# Daemon

`slothd` is the entry point of every nook's image. It dials the control plane, then starts,
feeds, watches, and stops processes on its instructions. It is a single .NET Native AOT binary.

## Projects

| Project | Status | Holds |
|---|---|---|
| `Bagatka.AiSloth.DaemonProtocol` | Built | The protocol, generated from `daemon.proto` for both sides |
| `Bagatka.AiSloth.Daemon` | Built | The daemon |

## Protocol

`daemon.proto` is the single source; never hand-edit generated code.

- **Control stream.** The daemon opens `ControlPlane.Connect`, a bidirectional gRPC stream, and
  sends `Hello` first, listing the processes still running. It reconnects with backoff when the
  stream drops. A newer stream for the same nook replaces an older one.
- **Bulk streams.** Output travels in a separate `UploadOutput` call per watch, on its own HTTP/2
  stream over the same connection, so a busy process never delays instructions. An upload ends with
  the process's exit, so a watch gets everything from one stream; one that ends without it broke off.
  Previews of web servers will travel the same way.
- **Authentication.** Every call carries `authorization: Bearer <token>`. The Nooks module issued
  the token and verifies it.
- **Bulk input.** A process started with `input_streamed` reads its standard input from a
  `ReadInput` call of its own, which the daemon makes as soon as it starts it; the stream's end, or
  its failure, closes the process's input. The control plane copies repositories and files into
  nooks this way.
- **Instructions:** `StartProcess`, `StopProcess`, `SendInput`, `WatchOutput`, and `Reconnect`.
  **Events:** `ProcessExited`, always the last thing reported about a process. A program that can't
  start exits with 127 and says why on standard error.
- **Evolution.** Changes are additive; a breaking change is a new package version (`v2`).

## Processes

- **Detached.** A process runs until it exits or is stopped, whatever happens to the connection.
- **Kept output.** Output is kept on the nook's disk, with standard output and standard error
  sharing one sequence of byte offsets, as each process's retention says:
  - `Recent` (commands, terminals, servers): at least the last 16 MiB; older output may be dropped,
    like a terminal's scrollback.
  - `Complete` (agent conversations): nothing is dropped. Output is kept until delivered, plus the
    last 16 MiB delivered. When 64 MiB waits undelivered, the daemon stops reading the process, so
    it blocks until something reads: pausing an agent beats corrupting its conversation.

  Files the process writes are never affected.
- **Bounded memory.** The daemon awaits every write and never buffers output without bound, so a
  slow watcher slows its upload, not the daemon.
- **Handover.** On `Reconnect`, the daemon reconnects at once and reaches another control-plane
  instance; running processes are unaffected.

## Full disk

A full disk must never cost output or leave a nook unrecoverable (`ARCHITECTURE.md`, "Nothing
delivered is lost").

- **Reported.** The daemon reports how full the working directory's disk is, so the control plane
  can ask for confirmation before it fills up.
- **A reserve.** The daemon keeps a 256 MiB reserve file in its state directory, preallocated so the
  space is really held, and takes it only while the disk has twice that free. When output can't be
  written because the disk is full, it deletes the reserve and reports at once, so watchers still
  get output and people can still start the commands that free space. It takes the reserve again
  once there is room.
- **Waiting, not dropping.** While output can't be written, the daemon stops reading the process's
  output, so the process waits instead of losing it.

## Environment

Set by the Nooks module through the provider: `SLOTHD_CONTROL_PLANE_URL`, `SLOTHD_NOOK_ID`, and
`SLOTHD_TOKEN`. The image may set `SLOTHD_WORKING_DIRECTORY` (default `/work`) and
`SLOTHD_STATE_DIRECTORY` (default `/var/lib/slothd`, where output is kept). The daemon reads them
once at startup; a missing or invalid value prints one line to standard error and exits with 2.

## Lifetime

The daemon lives as long as its nook. `SIGTERM` or `SIGINT`, as when the nook stops, kills its
processes and deletes their output; a daemon that starts again starts with none. The nook image runs
it under `tini`, which reaps the orphaned processes that agents leave behind. Its entry point,
`start-nook.sh`, has the Docker engine for the project's own containers start on first use: Docker's
socket listens from the start, and the first connection starts `dockerd` on it, about 0.4 seconds
before it answers. A nook that never uses Docker pays nothing for it; `/var/log/dockerd.log` says
what the engine did.

## Build and test

Native AOT needs clang and zlib, which `./dev` provides. Restore first, then publish without
restoring; restoring during a publish for a runtime would rewrite the protocol project's lock file:

    dotnet restore AiSloth.slnx --locked-mode
    dotnet publish src/Daemon/Bagatka.AiSloth.Daemon -c Release -r linux-x64 --no-restore

The nook images (`Dockerfile`) do the same inside the SDK image that `global.json` pins. The `nook`
target installs the binary as `/usr/local/bin/slothd` under tini, on plain Ubuntu 26.04 with git and
Ubuntu's Docker engine, Compose, and Buildx, for nooks without chats; the daemon needs only libc and
OpenSSL, and runs without ICU. Each harness target adds Node.js and one harness, at the version the profiles in
`src/Harnesses` are written for: `claude-code` (Claude Code's ACP adapter) and `copilot` (GitHub
Copilot CLI). A nook carries one, so a host pulls only what its nooks use; each harness's own files
make most of its image's size. Build one from the repository root with
`docker build -f src/Daemon/Dockerfile --target <target> -t aisloth-nook[-<harness>] .`; the AppHost
builds them all as `aisloth-nook:dev`, `aisloth-nook-claude-code:dev`, and `aisloth-nook-copilot:dev`.

The tests run the real daemon against a fake control plane: a real gRPC server on a loopback port.
The full-disk test mounts a small tmpfs, which needs root, and skips elsewhere.

## Not built yet

- **Compressed uploads.** Output travels uncompressed; gzip on `UploadOutput` is a few lines, worth
  it once nooks and the control plane run in different clouds and egress costs money.
- **Version checks.** The control plane accepts any daemon version.

## Open questions

- Previews: the instruction and the stream that carry a browser's traffic to a port in the nook.
- File operations and interactive terminals.
- Which daemon versions a control plane accepts.
