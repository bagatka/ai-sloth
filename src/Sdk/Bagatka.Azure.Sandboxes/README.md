# Bagatka.Azure.Sandboxes

A .NET client for [Azure Container Apps Sandboxes](https://learn.microsoft.com/azure/container-apps/sandboxes-overview):
microVMs with their own kernel that stop with their memory and resume in about a second. Create,
stop, resume, and delete sandboxes, commit them to disk images, and turn container images into disk
images.

Microsoft has no .NET SDK for this service yet; this one follows the Azure SDK guidelines, on
Azure.Core.

```csharp
SandboxGroupClient client = new SandboxGroupClient(
    SandboxGroupClient.GetEndpoint("eastus2"),
    new SandboxGroupId(subscriptionId, "my-resource-group", "my-sandbox-group"),
    new DefaultAzureCredential());

Operation<DiskImage> image = await client.CreateDiskImageAsync(WaitUntil.Completed, new DiskImageCreateOptions("ghcr.io/owner/image:1.0"));
SandboxCreateOptions options = new SandboxCreateOptions(SandboxSource.FromDiskImage(image.Value.Id), new SandboxResources("2", "4Gi", "40Gi"));
options.Environment["TOKEN"] = token;
Operation<Sandbox> sandbox = await client.CreateSandboxAsync(WaitUntil.Completed, options);

await client.StopSandboxAsync(WaitUntil.Completed, sandbox.Value.Id);   // keeps its memory
await client.ResumeSandboxAsync(WaitUntil.Completed, sandbox.Value.Id);
```

- **Service:** the data plane at `https://management.<region>.azuredevcompute.io`, version
  `2026-09-01-preview`, as specified in
  [azure-rest-api-specs](https://github.com/Azure/azure-rest-api-specs/tree/main/specification/app/data-plane/ContainerApps).
  Sandbox groups themselves are Azure Resource Manager resources (`Microsoft.App/sandboxGroups`,
  `2026-07-01`), made with the Azure CLI, Bicep, or the ARM SDK.
- **Covered:** sandboxes (create, get, list by labels, stop, resume, delete, commit) and disk images
  (create from a container image, public or with registry credentials, get, list by labels, delete).
  Not yet: files, commands, ports, volumes, egress policies, secrets, memory snapshots as resources,
  and registry sign-in with the group's managed identity.
- **Sign-in:** any `TokenCredential`, for the scope `https://dynamicsessions.io/.default`. The
  identity needs the `Container Apps SandboxGroup Data Owner` role on the group or its resource group.
- **Waiting:** changes the service finishes later return an `Operation<T>` that polls the resource
  until it is running, stopped, or ready; `WaitUntil.Completed` waits for it.
- **Errors:** `RequestFailedException`, with the service's code, such as `SandboxNotRunning`.
  Throttling (429) and unavailability (5xx) are retried by Azure.Core's pipeline; a retried create can
  leave a second sandbox, so label what you create and look it up.
- **OpenTelemetry:** `AddSource("Bagatka.Azure.Sandboxes")` for an activity per call, and
  `AddMeter("Bagatka.Azure.Sandboxes")` for `bagatka.azure.sandboxes.client.operation.duration`
  (`SandboxesDiagnostics`). Azure.Core adds the HTTP requests under them.
- **Async only, trimmable, and Native AOT compatible.** The public API is tracked in
  `PublicAPI.*.txt` (Microsoft.CodeAnalysis.PublicApiAnalyzers).

## Quirks

- The service runs the image's entry point, command, and environment, as Docker would;
  `Entrypoint` and `Command` override them.
- A sandbox needs its disk image only to be created; deleting the disk image leaves it working.
- A disk is at most 20 GiB per core; more fails with `InvalidResourceTier`.
- A missing image fails with `ImageNotFound`, a missing or private repository with
  `RegistryAuthFailed`. `RegistryCredentials` sign in to a private one: an Azure Container Registry
  takes the refresh token its `/oauth2/exchange` gives for a Microsoft Entra token, with the username
  `00000000-0000-0000-0000-000000000000`.
- An ID that isn't a GUID fails with `InvalidRequest`, not `SandboxNotFound`.
- Stopping a sandbox that isn't running fails with `SandboxNotRunning`, resuming one that isn't
  stopped with `InvalidSandboxState`, and committing one that isn't running with
  `SandboxNotRunning`.
- Deleting what doesn't exist succeeds.
- A sandbox's environment is never returned. One restored from a snapshot keeps its labels and
  environment.
- The service decrypts sandboxes' HTTPS in its egress proxy, whose certificate authority sandboxes
  trust.
- Identities given to the sandbox group are available to code in its sandboxes, so a group that runs
  untrusted code should have none.

## Tests

`tests/Bagatka.Azure.Sandboxes.Tests`: the client against recorded responses, and a live test when
`BAGATKA_AZURE_SANDBOXES_GROUP` is `subscription/resource-group/group/region` (with `az login`).

Not published yet; publishing needs a license for the repository first.
