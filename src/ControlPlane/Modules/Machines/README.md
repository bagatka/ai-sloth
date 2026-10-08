# Machines

A machine is a computer a workspace adds so its nooks can run there, such as a Hetzner VPS or a
Mac mini. To nooks, the workspace's machines are one sandbox provider, `machine`, and each machine
is a place within it. The `sloth` CLI's machine mode runs on the computer, dials out to the control
plane, and runs the provider calls it receives on its local Docker Engine.

## Owns

- **Data:** the workspace's machines, their registration codes and tokens (stored as hashes), and
  which machine each sandbox and snapshot lives on.
- **Rules:** who may add and remove machines (Manage on the workspace) and see them (Read); how a
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

`IMachinesApi` in `Bagatka.AiSloth.Machines.Contracts`: managers add machines, getting a one-time
registration code, and remove them; anyone with access lists them and sees whether each is online.
`IMachineConnectionsApi` is the machine endpoint's side, never a public route or a tool: a machine
trades its code for a token, then connects with it. `MachineProvider` names the provider and owns
the location format: a machine's ID.

```csharp
Result<MachineRegistration> added = await machines.AddAsync(owner, new AddMachine(workspaceId, "hetzner-1"), ct);
if (added.Failed)
{
    return new Result(added.Error);
}

MachineRegistration registration = added.Output;
// The owner runs on the computer: sloth machine connect https://… <registration.Code>, then sloth machine run.
// Nooks then create on provider "machine:<registration.Machine.Id>".
```

## Asks

Workspaces (`GetAccessAsync`), on every call made for a user.

## Publishes

`MachineRemoved` when a machine is removed.

## Reacts to

Nothing yet. Once workspaces can be deleted, `WorkspaceDeleted` removes their machines.

## Data

Schema `machines`. Table `machines` (ID, workspace ID, name, added at, registration code hash and
expiry, token hash; `xmin` as concurrency token, so two registrations with one code can't both win)
and `placements` (sandbox or snapshot key, machine ID); `outbox_messages` holds its events until
they are delivered.

## Background work

None outside connections. A connection lives as long as its gRPC call, which asks the machine every
15 seconds whether it is there and ends when it doesn't answer within 15: a computer gone to sleep or
off the network says nothing, and its machine goes offline within half a minute. The pings also keep
proxies that end idle streams, such as Azure Container Apps' ingress after four minutes, from cutting
a connection with nothing to do, or a call that takes longer, such as a first image pull. The machine
answers pings however many calls it runs.

## Configuration

None; the host passes the shared database.

## Decisions and constraints

- **A machine is a place, not a kind of nook.** One provider, `machine`, serves every workspace's
  machines; `SandboxSpec.Location` says which machine. Nooks, its lifecycle job, and the
  conformance suite treat it like any other provider.
- **What a machine needs.** It pulls the nook image from a registry, so the local development image,
  `aisloth-nook:dev`, works only on the developer's own Docker Engine; and its nooks dial the
  control plane's daemon URL, which must be reachable from there.
- **Registration.** An owner adds a machine and gets a 16-character code, valid for an hour and
  usable once, compared without regard to case or surrounding spaces. Registering trades it for a
  random token. Only hashes are stored; the token stays on the machine, in
  `~/.config/sloth/machine.json`, readable by its owner only. Removing a machine makes its token
  worthless at once and ends its connection, and Nooks fails its nooks for good (`MachineRemoved`).
  `sloth machine run`, then or whenever it runs next, is refused, deletes the machine's sandboxes and
  snapshots from the computer, and stops.
- **Placements are recorded before the call.** Creating a sandbox records its machine first, so a
  call by key finds it even if the create was interrupted. A snapshot's sandboxes start on the
  snapshot's machine. A disconnected machine makes calls for its sandboxes throw, as an unreachable
  backend does, and the lifecycle job retries.
- **The same providers, elsewhere.** Machine mode runs the providers the cloud uses, and the
  conformance suite runs every provider through `Bagatka.Sandboxing.Remote` too. The Docker provider
  works with a Docker Engine on a Unix socket that has Sysbox, so nooks run Docker of their own
  without privileges on the machine; `sloth machine run` checks for it first. Each machine works in
  its own Docker scope, `m-<machine ID>`.
- **Nooks never run directly on a machine's own operating system.** Linux nooks are containers under
  Sysbox, so machines run Linux, or Windows through WSL. Macs come later, with a VM per nook, Linux
  or macOS (`ROADMAP.md`, "Macs").
- **Lifecycle only.** The protocol carries sandbox lifecycle calls. A machine never accepts a
  command to run on its own operating system.
- **One active instance at a time**, as for daemons: connections live in the instance they dialed,
  and one that hands over ends them, so machines dial the active one.
