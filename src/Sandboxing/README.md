# Sandboxing

A general-purpose contract for running sandboxes on any compute backend, plus one project per
backend. Nothing here knows AiSloth; the Nooks module is its caller.

## Contract

`ISandboxProvider` in `Bagatka.Sandboxing`. It covers lifecycle only: create (from an image or a
snapshot), suspend, resume, snapshot, observe, list, and delete. Everything that happens inside a
sandbox goes through the image's entry point, which keeps a new provider small and lets a feature
be written once.

A spec's `Location` says where within the backend a sandbox runs, such as a region or a machine,
for a backend with several places; it is null for a backend with one. A provider rejects a location
it doesn't have. Operations by key find the sandbox wherever it was created.

## Remote calls

`Bagatka.Sandboxing.Remote` runs one provider's calls on another provider somewhere else.
`RemoteSandboxProvider` turns each call into a message from `sandbox_calls.proto`, and
`SandboxCalls.ExecuteAsync` runs it on a local provider and answers. Moving the messages is the
caller's job: AiSloth's machines carry them over a gRPC stream (`src/Cli`), and the conformance
suite over an in-memory loop. A remote provider that loses its connection fails the calls in
flight, as an unreachable backend would.

## Rules for providers

- **Naming and dependencies.** `Bagatka.Sandboxing.<Backend>` references `Bagatka.Sandboxing`,
  `Bagatka.Foundation`, .NET, and the backend's official SDK or its client in `src/Sdk`. Never
  `Bagatka.AiSloth.*`. Its registration is `<Backend>SandboxProviderRegistration`.
- **Settings.** One immutable settings record (credentials, region, deployment scope), passed by
  the host: `services.Add<Backend>SandboxProvider(settings)` (`PATTERNS.md`, entry 20). Cloud
  credentials are credential objects that refresh themselves, never strings.
- **Files survive until deletion.** A backend that can't keep a suspended sandbox's disk must keep
  the files some other way, such as object storage, or it can't be a provider.
- **Memory is best effort.** Suspend with memory where the backend can, report `Paused`; otherwise
  report `Stopped`. Every provider implements every member; there are no capability flags.
- **Snapshots hold files.** A sandbox created from a snapshot boots its files with a fresh entry
  point and the new environment, never its memory. Deleting a snapshot never affects sandboxes
  created from it.
- **Safe to repeat.** Derive resource names from the `SandboxKey` and `SnapshotKey`, and tag every
  resource with the deployment scope and the key. Creating with the same spec returns the existing
  sandbox, a different spec is a conflict, and deleting something missing succeeds.
- **Stay in scope.** List and touch only resources tagged with your own scope.
- **Exact resources.** Run the requested resources or reject the spec; never round silently.
- **No inbound networking.** The sandbox dials out; never open ports into it.
- **Containers inside, no privileges outside.** A sandbox can run containers of its own, as
  code's Docker does: give it its own kernel, as a microVM, or a runtime that makes that safe in
  a container, as Docker does with Sysbox. Never a privileged container.
- **Secrets.** `SandboxSpec.Environment` may hold secrets; never log it.

## Providers

| Provider | Status | Suspends to | Notes |
|---|---|---|---|
| Docker | Built | `Paused` (`docker pause`) | Local development, CI, single-machine deployments, and machines. Runs sandboxes under Sysbox (`sysbox-runc`), which the engine must have; creating fails with `sandboxing.sysbox_missing` otherwise. Snapshots are committed images, with the sandbox's environment values kept out. Sandboxes can reach the host as `host.docker.internal`. |
| Azure Container Apps Sandboxes | Planned | `Paused` | The official host. MicroVMs with Docker inside; memory snapshots restore in under a second once warm |
| macOS virtual machines | After a spike | To be measured | On people's Macs only, through machines (`src/ControlPlane/Modules/Machines`), for iOS and macOS work; Apple's Virtualization.framework through Tart |

## Tests

One conformance suite (`tests/Bagatka.Sandboxing.ConformanceTests`) runs against every provider
listed in `ProvidersUnderTest`: Docker in CI, directly and through remote calls, and clouds on
demand. It tests the contract only, so
swapping providers is safe. Each test works in a scope of its own and deletes everything in it
afterwards. Behavior the contract can't observe, such as what a provider's images contain, gets
tests of that provider next to the suite.
