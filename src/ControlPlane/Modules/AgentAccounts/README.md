# AgentAccounts

Agent accounts are accounts at agent vendors that pay for agents' work: an API key for OpenAI's or
Anthropic's API (at the vendor or at another endpoint that speaks it, such as OpenRouter), a ChatGPT
plan added by signing in with ChatGPT, a Copilot plan, or a Claude plan. A workspace's
account serves everyone with Write on it; a personal account serves its owner, wherever they work.
Secrets are encrypted at rest and never shown again, and the keys and plans of model APIs never enter
a nook: the model gateway adds them to each call.

## Owns

- **Data:** the accounts: kind, name, whose they are (a workspace or a person), an API key's
  endpoint, when a sign-in ended, and the sealed secret; and sign-ins in progress.
- **Rules:** who may add and remove them (Manage on the workspace, or the person), who may use them
  (Write on the workspace, or the person; the control plane's own processes, any), which kinds are
  personal only (plans), which are added by signing in (ChatGPT plans), and which kinds this
  deployment allows. What each kind is at its vendor: where its calls go and which headers pay for
  them, or that its token goes to its harness.
- **Integrations:** Sign in with ChatGPT, through `Bagatka.Sdk.OpenAI`: registering, exchanging the
  code, renewing a plan's access token, and ending its session on removal.

## Does not own

- Harnesses, and what each kind of account means to them: `Bagatka.Harnesses` knows harnesses, and
  Chats maps kinds of account to the credentials harnesses take.
- Whose messages in a chat reach the agent: Chats, asking `MayUseAsync`; everyone else proposes.
- Which networks the model gateway may reach: the WebApi's `ModelGateway` settings.

## Contract

`IAgentAccountsApi` in `Bagatka.AiSloth.AgentAccounts.Contracts`: people add accounts to a workspace
or as their own, with their secret or by signing in at the vendor, list the ones they can use in a
workspace, and remove them; `ListKindsAsync` says, for every kind, how it is added, whether it is
personal only, and whether this host allows it. `UseAsync` hands whoever runs an agent what it takes, renewing a plan's
token when it is due; it is for module callers only, never a route.

```csharp
Result<AgentAccountSummary> added = await accounts.AddAsync(owner, new AddAgentAccount(workspaceId, AgentAccountKind.OpenAIApiKey, "OpenRouter", key, new Uri("https://openrouter.ai/api/v1")), ct);

// A ChatGPT plan: the caller listens on the callback and opens the URL in the person's browser.
Result<SignInStarted> started = await accounts.StartSignInAsync(owner, new StartSignIn(AgentAccountKind.ChatGptPlan, "My ChatGPT", new Uri("http://127.0.0.1:1455/auth/callback")), ct);
Result<AgentAccountSummary> plan = await accounts.CompleteSignInAsync(owner, new CompleteSignIn(started.Output.Id, returnedTo), ct);

Result<AgentAccountCredential> used = await accounts.UseAsync(SystemActors.Harness, plan.Output.Id, workspaceId, ct);
if (used.Failed)
{
    return new Result(used.Error);   // e.g. AgentAccountsErrors.SignInEnded
}

switch (used.Output.Access)
{
    case ModelEndpoint endpoint: ...; break;   // the gateway forwards to endpoint.Url with endpoint.Headers
    case HarnessToken token: ...; break;       // a Copilot or Claude plan's token goes to the harness
}
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
secret: a key or token as given, or a plan's session as JSON). Table `sign_ins` (who, the account's
kind and name, the callback, the state and nonce, when it expires, and the sealed PKCE verifier).

## Background work

None. A plan's access token is renewed when it is used and due, never in the background.

## Configuration

`AgentAccountsSettings`, passed by the host (`PATTERNS.md`, entry 20), with the deployment's
`EncryptionSettings`:

- `AllowClaudePlans`, off: Anthropic's terms forbid storing Claude sign-in tokens without
  its written permission, so turn it on only with that permission;
- `AllowChatGptPlans`, off: OpenAI lets open-source and self-hosted deployments use Sign in with
  ChatGPT; a hosted service for other people needs OpenAI's approval first. The AppHost turns it on,
  since a local run is self-hosted;
- `ChatGptAuthority` and `ChatGptApi`, OpenAI's by default; tests point them at fakes.

## Decisions and constraints

- **Secrets at rest** are sealed with AES-256-GCM under the deployment's encryption key, bound to
  their row's ID (`PATTERNS.md`, entry 13). They leave the module only through `UseAsync`, in records
  whose text form leaves them out.
- **Plans are personal.** Copilot, Claude, and ChatGPT plans can only be
  someone's own; a team shares a workspace's API key.
- **Model APIs go through the gateway.** `UseAsync` gives an API key's or a plan's calls an endpoint
  and the headers that pay for them (`ModelEndpoint`); only tokens tied to one harness go to it
  (`HarnessToken`). An endpoint's address is checked for its shape here; which networks it may be on,
  the gateway decides.
- **Signing in with ChatGPT** registers the app for the person's ChatGPT account (named "AiSloth"),
  with PKCE and no client secret. The ID token comes straight from OpenAI's token endpoint over TLS,
  so its claims are checked (issuer, audience, nonce, expiry) without its signature, as OpenID Connect
  allows. The person must allow the app to use their plan; a sign-in without it adds nothing. OpenAI
  identifies each installation: this deployment's ID derives from its encryption key.
- **One renewal at a time per account.** A plan's access token lasts an hour; it is renewed when
  OpenAI asks (`earliest_refresh_at`), or shortly before it expires, on the next use. Every renewal
  replaces the refresh token and OpenAI refuses one used twice, so the renewal locks the account's
  row (`SELECT … FOR UPDATE`) while OpenAI answers, within the client's 30 seconds, and ignores the
  caller's cancellation: a renewal OpenAI completed but this side dropped would end the sign-in.
- **An ended sign-in** (OpenAI refuses a renewal, such as after the person disconnected the app)
  clears the account's tokens and marks it; it runs no agents, and says so, until it is removed and
  added again. A refresh token unused for 30 days ends it too.
- **Removing a plan ends its session** at OpenAI. When OpenAI can't be reached the account goes
  anyway, with a warning in the log.
- **Removing an account** stops model-gateway calls at once; an agent holding a token directly, such
  as a Copilot token, keeps it until its process stops.
