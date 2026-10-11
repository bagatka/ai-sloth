# AgentAccounts

Agent accounts pay for agents' work: an API key, a workspace's or someone's own, or a person's plan,
signed in at its vendor. A workspace's account serves everyone with Write on it; a personal account
serves its owner, wherever they work. Credentials are encrypted at rest and never shown again, and
never enter a nook: the model gateway adds them to each call.

What a kind of account is at its vendor (`ModelAccessKind`): where its calls go and what pays for
them, its models and what's known of them, and for a plan how it signs in, renews, and reports its
limits, is `Bagatka.ModelAccess`'s (`src/ModelAccess`). This module is AiSloth's side: the accounts
and whose they are, who may use them, which kinds this host allows, sealing credentials, sign-ins in
progress, and renewing one credential at a time.

## Owns

- **Data:** the accounts: kind, name, whose they are (a workspace or a person), an API key's
  endpoint, when a sign-in ended, and the sealed credential; and sign-ins in progress.
- **Rules:** who may add and remove them (Manage on the workspace, or the person), who may use them
  (Write on the workspace, or the person; the control plane's own processes, any), that plans are
  personal and added by signing in, and which kinds this host allows.

## Does not own

- What each kind is at its vendor: `Bagatka.ModelAccess`.
- Harnesses, and what each kind of account means to them: `Bagatka.Harnesses` knows harnesses, and
  Chats maps kinds of account to the credentials harnesses take.
- Whose messages in a chat reach the agent: Chats, asking `MayUseAsync`; everyone else proposes.
- Which networks the model gateway may reach: the WebApi's `ModelGateway` settings.

## Contract

`IAgentAccountsApi` in `Bagatka.AiSloth.AgentAccounts.Contracts`: people add accounts to a workspace
or as their own, with their secret or by signing in at the vendor, list the ones they can use in a
workspace, the models each one's vendor offers, and what a plan has used of its limits
(`GetPlanUsageAsync`), and remove them; `ListKindsAsync` says, for every kind, whether it is a plan
and whether this host allows it. `UseAsync` hands whoever runs an agent what it takes, renewing a
plan's credential when it is due; it is for module callers only, never a route.

```csharp
Result<AgentAccountSummary> added = await accounts.AddAsync(owner, new AddAgentAccount(workspaceId, ModelAccessKind.OpenRouterApiKey, "OpenRouter", key, Endpoint: null), ct);
Result<IReadOnlyList<AgentModel>> models = await accounts.ListModelsAsync(owner, added.Output.Id, workspaceId, ct); // openai/gpt-6-luna, …

// A Claude plan: the person approves at started.Output.Url, Claude's page, and pastes the code it shows.
Result<SignInStarted> started = await accounts.StartSignInAsync(owner, new StartSignIn(ModelAccessKind.ClaudePlan, "My Claude"), ct);
Result<SignInProgress> plan = await accounts.CompleteSignInAsync(owner, new CompleteSignIn(started.Output.Id, shownCode), ct);

// A ChatGPT or Copilot plan: the person enters started.Output.UserCode at started.Output.Url, OpenAI's
// or GitHub's; the caller completes without a code every interval until progress.Output.Account is there.
Result<SignInStarted> code = await accounts.StartSignInAsync(owner, new StartSignIn(ModelAccessKind.ChatGptPlan, "My ChatGPT"), ct);
Result<SignInProgress> progress = await accounts.CompleteSignInAsync(owner, new CompleteSignIn(code.Output.Id, Code: null), ct);

Result<AgentAccountCredential> used = await accounts.UseAsync(SystemActors.Harness, plan.Output.Account!.Id, workspaceId, ct);
if (used.Failed)
{
    return new Result(used.Error);   // e.g. AgentAccountsErrors.SignInEnded
}

// The gateway forwards to used.Output.Endpoint.Url with used.Output.Endpoint.Headers.
```

## Asks

Workspaces (`GetAccessAsync`), for the caller's access to the workspace.

## Publishes

Nothing yet.

## Reacts to

Nothing yet. Once workspaces can be deleted, `WorkspaceDeleted` removes their accounts.

## Data

Schema `agent_accounts`. Table `accounts` (kind, name, workspace or owner — a check constraint keeps
exactly one — when it was added, an API key's endpoint, when a sign-in ended, and the sealed
`ModelCredential` as JSON). Table `sign_ins` (who, the plan's kind and name, the code the person
enters at the vendor if any, when it expires, when the vendor may be asked next, and the sealed
secret the sign-in continues with).

## Background work

None. A plan's credential is renewed when it is used and due, never in the background.

## Configuration

`AgentAccountsSettings`, passed by the host (`PATTERNS.md`, entry 20), with the deployment's
`EncryptionSettings`: `AllowClaudePlans` and `AllowChatGptPlans`, off, for plans signed in as their
vendors' own agents, for people running agents on their own plans; a service offering them to other
people should agree it with the vendor first. The AppHost turns both on. Where the vendors are is
`ModelAccessSettings`, which the host registers.

## Decisions and constraints

- **Plans are personal.** Copilot, Claude, and ChatGPT plans can only be someone's own; a team
  shares a workspace's API key.
- **Secrets at rest** are sealed with AES-256-GCM under the deployment's encryption key, bound to
  their row's ID (`PATTERNS.md`, entry 13). They leave the module only through `UseAsync`, in records
  whose text form leaves them out.
- **Model APIs go through the gateway.** `UseAsync` gives every account's calls an endpoint and the
  headers that pay for them (`ModelEndpoint`). An endpoint's address is checked for its shape here;
  which networks it may be on, the gateway decides.
- **One renewal at a time per account.** A plan's access token lasts hours and is renewed shortly
  before it expires, on the next use. Every renewal replaces the refresh token and the vendor may
  refuse one used twice, so the renewal locks the account's row (`SELECT … FOR UPDATE`) while the
  vendor answers, within the client's 30 seconds, and ignores the caller's cancellation: a renewal the
  vendor completed but this side dropped would end the sign-in.
- **An ended sign-in** (the vendor refuses a renewal, such as after the person signed out
  everywhere) clears the account's credential and marks it; it runs no agents, and says so, until it
  is removed and added again.
- **A sign-in's state is its ID,** which ties the code a vendor's page shows to it; a failed attempt
  may be retried until the sign-in expires.
- **Removing an account** stops model-gateway calls at once. A plan's tokens go with it; the person
  can also disconnect it at the vendor.
