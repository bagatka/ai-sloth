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
  stream over the same connection, so a busy process never delays instructions. Previews of web
  servers will travel the same way.
- **Authentication.** Every call carries `authorization: Bearer <token>`. The Nooks module issued
  the token and verifies it.
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

## Environment

Set by the Nooks module through the provider: `SLOTHD_CONTROL_PLANE_URL`, `SLOTHD_NOOK_ID`, and
`SLOTHD_TOKEN`. The image may set `SLOTHD_WORKING_DIRECTORY` (default `/work`) and
`SLOTHD_STATE_DIRECTORY` (default `/var/lib/slothd`, where output is kept). The daemon reads them
once at startup; a missing or invalid value prints one line to standard error and exits with 2.

## Lifetime

The daemon lives as long as its nook. `SIGTERM` or `SIGINT`, as when the nook stops, kills its
processes and deletes their output; a daemon that starts again starts with none. The image must run
it under an init such as `tini`, which reaps the orphaned processes that agents leave behind.

## Build and test

Native AOT needs clang and zlib, which `./dev` provides. Restore first, then publish without
restoring; restoring during a publish for a runtime would rewrite the protocol project's lock file:

    dotnet restore AiSloth.slnx --locked-mode
    dotnet publish src/Daemon/Bagatka.AiSloth.Daemon -c Release -r linux-x64 --no-restore

The image installs the published `Bagatka.AiSloth.Daemon` binary as `slothd`. The tests run the
real daemon against a fake control plane: a real gRPC server on a loopback port.

## Open questions

- Previews: the instruction and the stream that carry a browser's traffic to a port in the nook.
- File operations and interactive terminals.
- Which daemon versions a control plane accepts.
