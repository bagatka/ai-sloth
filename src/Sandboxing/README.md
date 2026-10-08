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
flight, as an unreachable backend would; its owner can ping the other side (`KeepAliveAsync`) to
notice one that fell silent without closing the connection.

## Rules for providers

- **Naming and dependencies.** `Bagatka.Sandboxing.<Backend>` references `Bagatka.Sandboxing`,
  `Bagatka.Foundation`, .NET, and the backend's official SDK or its client in `src/Sdk`. Never
  `Bagatka.AiSloth.*`. Its registration is `<Backend>SandboxProviderRegistration`.
- **Settings.** One immutable settings record (region, deployment scope), passed by the host:
  `services.Add<Backend>SandboxProvider(settings)` (`PATTERNS.md`, entry 20). Cloud credentials are
  objects that refresh themselves, never strings, passed beside it:
  `AddAzureSandboxProvider(settings, credential)`.
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
- **Sandboxes don't reach each other.** Put a scope's sandboxes where they can't reach one another:
  a network of their own, or a backend that isolates each sandbox (see Network below).
- **Containers inside, no privileges outside.** A sandbox can run containers of its own, as
  code's Docker does: give it its own kernel, as a microVM, or a runtime that makes that safe in
  a container, as Docker does with Sysbox. Never a privileged container.
- **Secrets.** `SandboxSpec.Environment` may hold secrets; never log it.

## Providers

| Provider | Status | Suspends to | Notes |
|---|---|---|---|
| Docker | Built | `Stopped` (`docker stop`) | Local development, CI, single-machine deployments, and machines. Stopping frees a sandbox's memory, which matters on people's machines; a container whose entry point exited cleanly, as it does when asked to stop, is `Stopped`, and any other end is `Failed`. Runs sandboxes under Sysbox (`sysbox-runc`), which the engine must have; creating fails with `sandboxing.sysbox_missing` otherwise. Snapshots are committed images, with the sandbox's environment values kept out. Each sandbox has a network and a router of its own ("Network"). |
| Azure Container Apps Sandboxes | Built | `Paused` | The official host: microVMs with Docker inside. Images must be public; an image's disk image is shared by the deployments of a sandbox group, made again daily, and deleted after two days unused. Disks get 20 GiB per core. The service suspends a sandbox idle for 30 minutes, in case the control plane is down. Snapshots are committed disk images. Through `Bagatka.Azure.Sandboxes` (`src/Sdk`) |
| macOS virtual machines | After a spike | To be measured | On people's Macs only, through machines (`src/ControlPlane/Modules/Machines`), for iOS and macOS work; Apple's Virtualization.framework through Tart |

## An Azure sandbox group

`dotnet aspire deploy` makes a deployment's own. For development and tests, make one once with the
Azure CLI. The provider's identity (a managed identity when hosted, `az login` in development) needs
the data plane role on it. Never give the group an identity: code in its sandboxes could use it.

```sh
az group create --name <resource-group> --location eastus2
az rest --method put --body '{"location":"eastus2"}' \
  --url "https://management.azure.com/subscriptions/<subscription>/resourceGroups/<resource-group>/providers/Microsoft.App/sandboxGroups/<group>?api-version=2026-07-01"
az role assignment create --role "Container Apps SandboxGroup Data Owner" \
  --assignee <object-id> --scope /subscriptions/<subscription>/resourceGroups/<resource-group>
```

The AppHost takes it as `azure-sandbox-group` (`subscription/resource-group/group/region`). Nooks
there need the images in a public repository (`nook-image-repository`) and public addresses for this
computer's daemon and model endpoints (`nook-daemon-url`, `nook-models-url`). We test with ngrok and
ttl.sh, and anyone can: the end-to-end suite's Azure tests run when `BAGATKA_AZURE_SANDBOXES_GROUP`
is set and `BAGATKA_NGROK_ENV_FILE` names an env file with `NGROK_AUTHTOKEN`.

## Network

What a sandbox can reach, as measured on each backend:

- **Azure Container Apps Sandboxes:** the internet only. Each sandbox is alone on a link-local
  network of its own, so it reaches no other sandbox, and neither the metadata service nor the
  WireServer answers. Azure offers every sandbox an identity endpoint (`IDENTITY_ENDPOINT`); it gives
  no token as long as the sandbox group has no identity, so a group never gets one.
- **Docker:** the internet, and on the host only the TCP ports in `DockerSandboxSettings.HostPorts`
  (at `host.docker.internal`), such as a control plane's in development. Each sandbox is a computer
  of its own: it has a network of its own, a /29 of 198.18.0.0/15, and a router of its own
  (`router.sh`), a small Alpine container that forwards its traffic to the internet and drops
  everything bound for private networks, link-local and metadata addresses, the host's own
  addresses, or other sandboxes. DNS goes through Docker as for any container. The host has no
  address on a sandbox's network, IPv6 link-local included (the bridge's MTU is below IPv6's
  minimum), so even a sandbox's root, which can change its own routes and switch IPv6 on, has no way
  around the router. Routers run Alpine with iptables, an image (`bagatka-router`) each engine
  builds once, which takes the internet that once. The engine must be Docker 28 or later.

## Tests

One conformance suite (`tests/Bagatka.Sandboxing.ConformanceTests`) runs against every provider
listed in `ProvidersUnderTest`: Docker in CI, directly and through remote calls, and Azure when
`BAGATKA_AZURE_SANDBOXES_GROUP` is set. It tests the contract only, so
swapping providers is safe. Each test works in a scope of its own and deletes everything in it
afterwards. Behavior the contract can't observe, such as what a provider's images contain, gets
tests of that provider next to the suite.
