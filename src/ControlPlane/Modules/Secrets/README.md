# Secrets

Secrets are environment variables a workspace gives to every process in its nooks, agents included,
for the tools they run: `GH_TOKEN` for `gh`, `NPM_TOKEN`, cloud keys. A person's own workspace holds
their own secrets.

## Owns

- **Data:** each workspace's secrets: name, sealed value, who set it, and when.
- **Rules:** who may set and remove them (Manage on the workspace), who sees their names (anyone with
  access to the workspace), which names a secret may take, and how many a workspace holds.

## Does not own

- Putting them into processes: Nooks asks for a workspace's values whenever it starts a process.
- Agent accounts, which pay for agents' models and never enter a nook: AgentAccounts.

## Contract

`ISecretsApi` in `Bagatka.AiSloth.Secrets.Contracts`: managers set and remove a workspace's secrets by
name, and its people list their names. `ResolveAsync` hands the values to the control plane's own
processes only, never to a route.

```csharp
Result<SecretSummary> set = await secrets.SetAsync(alice, new SetSecret(workspaceId, "GH_TOKEN", token), ct);
Result<IReadOnlyDictionary<string, string>> values = await secrets.ResolveAsync(SystemActors.Processes, workspaceId, ct); // Nooks, starting a process
```

## Asks

Workspaces (`GetAccessAsync`), for the caller's access to the workspace.

## Publishes

Nothing yet.

## Reacts to

Nothing yet. Once workspaces can be deleted, `WorkspaceDeleted` removes their secrets.

## Data

Schema `secrets`. Table `secrets` (workspace, name — unique in a workspace — the sealed value, who set
it, and when).

## Background work

None.

## Configuration

The deployment's `EncryptionSettings`, passed by the host (`PATTERNS.md`, entry 20).

## Decisions and constraints

- **A workspace's, never a person's in a shared space.** Anything in a nook is readable by everyone
  who may write there, so a secret belongs to the workspace whose people will see it. A person's own
  secrets are their own workspace's. Doing work as a person in a team's workspace, such as opening a
  pull request as them, is the control plane's job, with credentials that never enter a nook.
- **Readable by everyone who may write in a nook,** a nook's guests included: they can run processes
  there. Inviting someone with Write to a nook shares its secrets with them.
- **Every process gets them,** as they are when it starts: the agent and anything people run. A
  process's own variables win over a secret of the same name. Names the system relies on (`PATH`,
  `HOME`, `LD_*`, `SLOTHD_*`) are refused.
- **Values at rest** are sealed with AES-256-GCM under this module's key, derived from the deployment's, bound to their
  row's ID (`PATTERNS.md`, entry 13). They leave the module only through `ResolveAsync`.
- **At most 100 per workspace,** of at most 16,384 characters each, which bounds what every process
  start carries.
