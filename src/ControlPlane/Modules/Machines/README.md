# Machines

Planned. A machine is a computer someone registers to a workspace so nooks can run on it, such as a
Hetzner VPS or a Mac mini, instead of on AiSloth's cloud. The `sloth` CLI's machine mode runs there,
dials out to the control plane, and runs nooks with the same sandbox providers the cloud uses.

## Owns

- **Data:** the workspace's machines, their credentials (stored as hashes), and what each can run
  and how many at once.
- **Rules:** who may register and remove machines (workspace owners) and who may run nooks on them
  (members).
- **Runtime state:** each online machine's connection, in the memory of the instance it dialed.

## Does not own

- Nooks: Nooks decides where each nook runs and asks this module only for nooks placed on a
  machine.
- What runs inside a nook: the daemon, as on any provider.

## Contract

`IMachinesApi` (planned). Owners register and remove machines; members list them and see whether
they are online; Nooks runs sandbox lifecycle operations on a given machine. A machine's own side
is a dial-out connection, translated by the WebApi like the daemon's.

## Asks

Workspaces.

## Publishes

Nothing yet.

## Reacts to

Nothing yet.

## Data

Schema `machines`: machines, credential hashes, and capabilities.

## Background work

None yet.

## Configuration

None yet.

## Decisions and constraints

- **The same providers, elsewhere.** The machine mode runs the providers the cloud uses, so the
  sandbox provider conformance suite also runs through a machine's connection. The Docker provider
  works with any Docker-compatible engine: Docker, OrbStack, Colima, or Podman.
- **Nooks never run directly on a machine's own operating system.** Linux nooks are containers; on a
  Mac, the container engine already runs them inside a Linux VM, and on a Linux host gVisor can add
  a stronger boundary. macOS nooks, for iOS work, are macOS virtual machines on Apple's
  Virtualization.framework (through Tart, after a spike), never a shared user account.
- **Lifecycle only.** The machine protocol carries sandbox lifecycle operations. A machine never
  accepts a command to run on its own operating system, and its credential can be revoked at any
  time.
- **Limits.** macOS virtual machines need Apple silicon, Apple's license allows about two at a time
  per Mac, and macOS images with Xcode take tens of gigabytes. A machine reports its capacity.
