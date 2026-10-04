# AgentAccounts

Agent accounts are accounts at agent vendors that pay for agents' work: an Anthropic API key, a
GitHub Copilot plan, later a ChatGPT plan. A workspace's account serves all its members; a personal
account serves its owner, in every workspace they belong to. Secrets are encrypted at rest and never
shown again.

## Owns

- **Data:** the accounts: kind, name, whose they are (a workspace or a person), and the sealed secret.
- **Rules:** who may add and remove them (a workspace's owners, or the person), who may use them
  (the workspace's members, or the person; the control plane's own processes, any), which kinds are
  personal only (plans), which kinds this deployment allows, and whether personal accounts may be
  shared.

## Does not own

- Harnesses, and what each kind of account means to them: `Bagatka.Harnesses` knows harnesses, and
  Chats maps kinds of account to the credentials harnesses take.
- Who may send messages to a chat: Chats, using the account's owner and whether it is shareable.

## Contract

`IAgentAccountsApi` in `Bagatka.AiSloth.AgentAccounts.Contracts`: people add accounts to a workspace
or as their own, list the ones they can use in a workspace, and remove them. `UseAsync` hands an
account's secret to whoever runs an agent with it; it is for module callers only, never a route.

```csharp
Result<AgentAccountSummary> added = await accounts.AddAsync(owner, new AddAgentAccount(workspaceId, AgentAccountKind.AnthropicApiKey, "Team key", key), ct);
if (added.Failed)
{
    return new Result(added.Error);
}

Result<AgentAccountCredential> used = await accounts.UseAsync(SystemActors.Harness, added.Output.Id, workspaceId, ct);
if (used.Failed)
{
    return new Result(used.Error);
}

AgentAccountCredential credential = used.Output; // credential.Secret
```

## Asks

Workspaces (`GetRoleAsync`), for owners and members.

## Publishes

Nothing yet.

## Reacts to

Nothing yet. Once workspaces can be deleted, `WorkspaceDeleted` removes their accounts.

## Data

Schema `agent_accounts`. Table `accounts` (kind, name, workspace or owner — a check constraint keeps
exactly one — when it was added, and the sealed secret).

## Background work

None.

## Configuration

`AgentAccountsSettings`, passed by the host (`PATTERNS.md`, entry 20):

- the connection string;
- the encryption key, at least 32 characters (the AppHost generates one and keeps it in its user
  secrets);
- `AllowClaudeSubscriptions`, off: Anthropic's terms forbid storing Claude sign-in tokens without
  its written permission, so turn it on only with that permission;
- `SharePersonalAccounts`, off: vendors' plans are for one person, so turn it on only where a
  vendor's terms allow sharing.

## Decisions and constraints

- **Secrets at rest** are sealed with AES-256-GCM under the deployment's encryption key, bound to
  the account's ID (`PATTERNS.md`, entry 13). They leave the module only through `UseAsync`, in a
  record whose text form leaves them out.
- **Plans are personal.** Copilot tokens and Claude subscriptions can only be someone's own; a team
  shares a workspace's API key.
- **Removing an account** stops model-gateway calls at once; an agent holding a secret directly, such
  as a Copilot token, keeps it until its process stops.

## Not built yet

- **Key rotation.** A new encryption key makes every stored secret unreadable.
- **ChatGPT plans** (Sign in with ChatGPT), once OpenAI grants hosted apps plan access, and OpenAI
  API keys for Codex.
- **Per-vendor sharing rules.** Sharing is one switch for every kind.
- **Members who aren't owners** can't be tested yet: workspaces have no invitations.
