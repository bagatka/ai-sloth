# Machines

A machine is a computer a workspace adds so its nooks can run there, such as a Hetzner VPS or a
Mac mini. To nooks, the workspace's machines are one sandbox provider, `machine`, and each machine
is a place within it. The `sloth` CLI's machine mode runs on the computer, dials out to the control
plane, and runs the provider calls it receives on its local Docker Engine.

## Owns

- **Data:** the workspace's machines, their registration codes and tokens (stored as hashes), and
  which machine each sandbox and snapshot lives on.
- **Rules:** who may add and remove machines (workspace owners) and see them (members); how a
  machine proves itself; which machine a provider call goes to.
- **Runtime state:** each connected machine's connection, in the memory of the instance it dialed.
- **Integrations:** the `machine` sandbox provider, which this module registers for the host.

## Does not own

- Nooks: Nooks decides where each nook runs, including that a nook runs only on its own workspace's
  machines, and calls this module's provider like any other.
- The wire protocol and its gRPC endpoint: `src/Cli/Bagatka.AiSloth.MachineProtocol` and the WebApi.
- The provider call messages: `Bagatka.Sandboxing.Remote`, which turns `ISandboxProvider` calls
  into messages and runs them on the other side.
- What runs inside a nook: the daemon, as on any provider.

## Contract

`IMachinesApi` in `Bagatka.AiSloth.Machines.Contracts`: owners add machines, getting a one-time
registration code, and remove them; members list them and see whether each is online.
`IMachineConnectionsApi` is the machine endpoint's side, never a public route or a tool: a machine
trades its code for a token, then connects with it. `MachineProvider` names the provider and owns
the location format: a machine's ID.

```csharp
MachineRegistration added = (await machines.AddAsync(owner, new AddMachine(workspaceId, "hetzner-1"), ct)).Value;
// The owner runs on the computer: sloth machine connect https://… <added.Code>, then sloth machine run.
// Nooks then create on provider "machine:<added.Machine.Id>".
```

## Asks

Workspaces (`GetRoleAsync`), on every call made for a user.

## Publishes

Nothing yet.

## Reacts to

Nothing yet. Once workspaces can be deleted, `WorkspaceDeleted` removes their machines.

## Data

Schema `machines`. Table `machines` (ID, workspace ID, name, added at, registration code hash and
expiry, token hash; `xmin` as concurrency token, so two registrations with one code can't both win)
and `placements` (sandbox or snapshot key, machine ID).

## Background work

None. A connection lives as long as its gRPC call.

## Configuration

`MachinesSettings`, passed by the host (`PATTERNS.md`, entry 20): the connection string.

## Decisions and constraints

- **A machine is a place, not a kind of nook.** One provider, `machine`, serves every workspace's
  machines; `SandboxSpec.Location` says which machine. Nooks, the reconciler, and the conformance
  suite treat it like any other provider.
- **Registration.** An owner adds a machine and gets a 16-character code, valid for an hour and
  usable once, compared without regard to case or surrounding spaces. Registering trades it for a
  random token. Only hashes are stored; the token stays on the machine, in
  `~/.config/sloth/machine.json`, readable by its owner only. Removing a machine makes its token
  worthless at once and ends its connection; `sloth machine run` then stops.
- **Placements are recorded before the call.** Creating a sandbox records its machine first, so a
  call by key finds it even if the create was interrupted. A snapshot's sandboxes start on the
  snapshot's machine. A disconnected machine makes calls for its sandboxes throw, as an unreachable
  backend does, and the reconciler retries.
- **The same providers, elsewhere.** Machine mode runs the providers the cloud uses, and the
  conformance suite runs every provider through `Bagatka.Sandboxing.Remote` too. The Docker provider
  works with any Docker-compatible engine on a Unix socket: Docker, OrbStack, Colima, or Podman.
  Each machine works in its own Docker scope, `m-<machine ID>`.
- **Nooks never run directly on a machine's own operating system.** Linux nooks are containers; on a
  Mac, the container engine already runs them inside a Linux VM. macOS nooks, for iOS work, will be
  macOS virtual machines on Apple's Virtualization.framework (through Tart, after a spike).
- **Lifecycle only.** The protocol carries sandbox lifecycle calls. A machine never accepts a
  command to run on its own operating system.
- **One active instance for now**, as for daemons: connections live in the instance they dialed.

## Not built yet

- **Slow and hung machines delay everyone.** The reconciler works on one nook at a time and waits
  for each call, so a machine pulling the nook image for the first time delays every workspace's new
  nooks for that long, and a machine whose Docker Engine hangs blocks them until it disconnects.
  Calls need deadlines, and the reconciler needs to work on nooks independently, before many
  workspaces share a control plane.
- **Removing a machine leaves its nooks.** Their records stay, and their containers keep running
  until someone stops them on the computer; their daemons still connect, because a daemon proves
  itself with its nook's token. Removal should fail those nooks, through a `MachineRemoved` event
  once the outbox exists. Deleting such a nook removes only its record.
- **The nook image must be in a registry the machine can pull from.** The local development image,
  `aisloth-nook:dev`, exists only on the developer's Docker Engine.
- **Network reach.** Nooks on a machine dial the control plane's daemon URL, which must be reachable
  from there.
- **Lists skip offline machines.** `ListAsync` and `ListSnapshotsAsync` answer for connected
  machines only. Nothing uses them yet; the reconciler will before it deletes sandboxes without a
  record.
- **Capacity.** A machine doesn't report how many nooks it can run; macOS virtual machines will need
  it (Apple silicon only, about two at a time per Mac).
