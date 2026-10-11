# AgentAccounts

Agent accounts are accounts at agent vendors that pay for agents' work: an API key for OpenAI's or
Anthropic's API (at the vendor or at another endpoint that speaks it, such as a company's gateway),
an OpenRouter key for many vendors' models, or a plan its vendor's own agent signs in to: a ChatGPT
plan with a code entered at OpenAI, as Codex does; a Claude plan approved at Claude's page, as Claude
Code does; or a Copilot plan with a code entered at GitHub. A workspace's account serves everyone
with Write on it; a personal account serves its owner, wherever they work.
Secrets are encrypted at rest and never shown again, and the keys and plans of model APIs never enter
a nook: the model gateway adds them to each call.

Every feature is the same for every kind. What differs between kinds is each kind's own, one class
in `Kinds/` (`IAccountKind`; a plan is an `IPlanKind`): whether this host allows it, where its calls go
and what pays for them, its vendor's models and how they're priced, and for a plan, how it signs in,
renews, and reports its limits. Adding a kind is one class and one line in `AgentAccountsModule`.

## Owns

- **Data:** the accounts: kind, name, whose they are (a workspace or a person), an API key's
  endpoint, when a sign-in ended, and the sealed credential; and sign-ins in progress.
- **Rules:** who may add and remove them (Manage on the workspace, or the person), who may use them
  (Write on the workspace, or the person; the control plane's own processes, any), which kinds are
  personal only and added by signing in (plans), and which kinds this deployment allows. What each
  kind is at its vendor: where its calls go and which headers pay for them.
- **Integrations:** the plans' sign-ins, as their vendors' agents sign in: Codex's (its device flow
  and renewals) through `Bagatka.Sdk.OpenAI`, Claude Code's (its code shown at Claude's page, and
  renewals) through `Bagatka.Sdk.Anthropic`, and GitHub's device flow for the host's Copilot app
  through `Bagatka.Sdk.GitHub`. The vendors' lists of models: OpenAI's and ChatGPT's Codex backend's
  through `Bagatka.Sdk.OpenAI`, Anthropic's through `Bagatka.Sdk.Anthropic`, Copilot's through
  `Bagatka.Sdk.GitHub`, and OpenRouter's catalog through `Bagatka.Sdk.OpenRouter`. models.dev's open
  catalog of what each model takes, holds, and costs (`Catalog/`). What plans have used of their
  limits, from the vendors' undocumented endpoints that their own clients read: ChatGPT's windows
  through `Bagatka.Sdk.OpenAI`, Claude's through `Bagatka.Sdk.Anthropic`, and Copilot's quotas
  through `Bagatka.Sdk.GitHub`.

## Does not own

- Harnesses, and what each kind of account means to them: `Bagatka.Harnesses` knows harnesses, and
  Chats maps kinds of account to the credentials harnesses take.
- Whose messages in a chat reach the agent: Chats, asking `MayUseAsync`; everyone else proposes.
- Which networks the model gateway may reach: the WebApi's `ModelGateway` settings.

## Contract

`IAgentAccountsApi` in `Bagatka.AiSloth.AgentAccounts.Contracts`: people add accounts to a workspace
or as their own, with their secret or by signing in at the vendor, list the ones they can use in a
workspace, the models each one's vendor offers, and what a plan has used of its limits
(`GetPlanUsageAsync`), and remove them; `ListKindsAsync` says, for every kind, how it is added, whether it is
personal only, and whether this host allows it. `UseAsync` hands whoever runs an agent what it takes, renewing a plan's
token when it is due; it is for module callers only, never a route.

```csharp
Result<AgentAccountSummary> added = await accounts.AddAsync(owner, new AddAgentAccount(workspaceId, AgentAccountKind.OpenRouterApiKey, "OpenRouter", key, Endpoint: null), ct);
Result<IReadOnlyList<AgentModel>> models = await accounts.ListModelsAsync(owner, added.Output.Id, workspaceId, ct); // openai/gpt-6-luna, …

// A Claude plan: the person approves at started.Output.Url, Claude's page, and pastes the code it shows.
Result<SignInStarted> started = await accounts.StartSignInAsync(owner, new StartSignIn(AgentAccountKind.ClaudePlan, "My Claude"), ct);
Result<SignInProgress> plan = await accounts.CompleteSignInAsync(owner, new CompleteSignIn(started.Output.Id, shownCode), ct);

// A ChatGPT or Copilot plan: the person enters started.Output.UserCode at started.Output.Url, OpenAI's
// or GitHub's; the caller completes without a code every interval until progress.Output.Account is there.
Result<SignInStarted> code = await accounts.StartSignInAsync(owner, new StartSignIn(AgentAccountKind.ChatGptPlan, "My ChatGPT"), ct);
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
credential as JSON: a key or a plan's token, and for a plan this host renews, its refresh token,
when it expires, and the vendor's account it uses). Table `sign_ins` (who, the plan's kind and name,
the code the person enters at the vendor if any, when it expires, when the vendor may be asked next,
and the sealed secret the sign-in continues with: a device code or a PKCE verifier).

## Background work

None. A plan's credential is renewed when it is used and due, never in the background.
models.dev's catalog is read again once a day old, by the next caller that needs it, never in the
background.

## Configuration

`AgentAccountsSettings`, passed by the host (`PATTERNS.md`, entry 20), with the deployment's
`EncryptionSettings`:

- `AllowClaudePlans` and `AllowChatGptPlans`, off: plans signed in as their vendors' own agents,
  for people running agents on their own plans; a service offering them to other people should
  agree it with the vendor first. The AppHost turns both on;
- `CopilotClientId`, the GitHub OAuth app people sign in to for their Copilot plans: AiSloth's own
  unless the host names another;
- `ChatGptAuthority` (Codex's sign-in), `ChatGptBackend` (a ChatGPT plan's calls, at `/codex`, and
  its usage), `ClaudeAuthorizeUrl` and `ClaudeTokenUrl` (Claude Code's sign-in), `AnthropicApi`,
  `OpenRouterApi`, `CopilotApi`, and `ModelsDevUrl`, the vendors' own by default; tests point them
  at fakes. GitHub itself is the host's `GitHubClient`'s.

## Decisions and constraints

- **A plan's limits, as its vendor counts them** (`PlanUsage`), asked of the vendor when someone
  looks, where its own clients ask: a ChatGPT plan's five-hour and weekly windows (ChatGPT's
  `/wham/usage`, as Codex's `/status`), a Claude plan's (Anthropic's `/api/oauth/usage`, as Claude
  Code's `/usage`), and Copilot's quotas (requests used of those allowed, or the credits a plan
  without limits spent). No vendor documents these answers.
- **A plan signs in as its vendor's own agent,** with that agent's OAuth client, and runs only that
  agent: a ChatGPT plan signs in as Codex, and its calls go to ChatGPT's Codex backend
  (`chatgpt.com/backend-api/codex`) as Codex's do; a Claude plan signs in as Claude Code (Claude's
  page shows a code the person pastes back, `code#state`), and Claude Code, which runs on Anthropic's
  Agent SDK, calls Anthropic's API through the gateway with an OAuth token of its chat's, which the
  gateway replaces with the plan's. The host keeps the session and renews it; it never enters a nook.

- **One OpenRouter key, both APIs.** OpenRouter serves Anthropic's Messages API at its root
  (`/v1/messages`) and OpenAI's at `/v1`, so an OpenRouter key's endpoint is OpenRouter's root, with
  its key as a bearer token, and each harness calls the API it speaks. Only Anthropic and OpenAI API
  keys name an endpoint of their own.
- **What's known of a model** (`ModelFacts`): how it reasons, its context and output limits, its
  price per million tokens with any long-context tiers, and how long its vendor keeps a prompt's
  cache. Each vendor's list says what an account can use, and what the vendor states stands over the
  catalog's word: Copilot's reasoning efforts and limits, OpenRouter's prices, context, and whether a
  model reasons. models.dev's catalog (MIT, `Catalog/models-dev.LICENSE`) says the rest: a snapshot
  built in (`Catalog/update-models-dev.sh`), read again from models.dev daily, keeping what it has
  when that fails. An API key pays its vendor's price; a plan pays none per token, so its price is
  an estimate at the vendor's API prices; an endpoint of an account's own has no price we can know.
  What isn't known stays unknown. Cache lifetimes are the vendors' rules: Anthropic's five minutes,
  OpenAI's five (of five to ten) or thirty for GPT-5.6 and later; Copilot's isn't known.
- **Models come from each vendor's list.** OpenRouter's catalog is public, read on each call (about
  60 ms for 750 KB), without its batch variants. Anthropic's and OpenAI's lists are asked at the
  account's endpoint with its credentials; OpenAI's holds every model its API serves, so only its GPT,
  o-series, and Codex models are offered, and a ChatGPT plan's token gets the plan's own catalog,
  named and in order. A Copilot plan's token gets the chat models the plan's policy enables, from
  Copilot's undocumented `/models`; a Claude plan's is asked of Anthropic's list with
  the plan's sign-in token. Every chat names its model; the apps start a new one on the first
  listed.
- **Secrets at rest** are sealed with AES-256-GCM under the deployment's encryption key, bound to
  their row's ID (`PATTERNS.md`, entry 13). They leave the module only through `UseAsync`, in records
  whose text form leaves them out.
- **Plans are personal.** Copilot, Claude, and ChatGPT plans can only be
  someone's own; a team shares a workspace's API key.
- **Model APIs go through the gateway.** `UseAsync` gives every account's calls an endpoint and the
  headers that pay for them (`ModelEndpoint`). An endpoint's address is checked for its shape here;
  which networks it may be on, the gateway decides.
- **A Copilot plan is Copilot's API, behind the gateway.** Copilot's CLI and pi call the gateway
  as an OpenAI-style provider, with their chat's token; the gateway calls Copilot's API with the
  plan's token and the headers Copilot's CLI sends (`CopilotApi.Headers`). Copilot doesn't document
  this API; its CLI's provider mode is documented.
- **Copilot plans sign in to an app of their own,** not the host's GitHub App: a plan is what pays
  for models, perhaps a work account's, and has nothing to do with whose repositories a person
  reaches. The app, a GitHub OAuth app, asks for no scopes, so its token reaches the person's
  Copilot and public data only; it never expires, so this host never renews it, and its client ID is
  public, so every host uses AiSloth's own. A token that expires anyway is refused.
- **One renewal at a time per account.** A ChatGPT or Claude plan's access token lasts hours; it is
  renewed shortly before it expires, on the next use. Every renewal replaces the refresh token and
  the vendor may refuse one used twice, so the renewal locks the account's row (`SELECT … FOR
  UPDATE`) while the vendor answers, within the client's 30 seconds, and ignores the caller's
  cancellation: a renewal the vendor completed but this side dropped would end the sign-in.
- **An ended sign-in** (the vendor refuses a renewal, such as after the person signed out
  everywhere) clears the account's tokens and marks it; it runs no agents, and says so, until it is
  removed and added again.
- **Removing an account** stops model-gateway calls at once. A plan's tokens go with it; the person
  can also disconnect it at the vendor.
