# AgentAccounts

Agent accounts are accounts at agent vendors that pay for agents' work: an API key for OpenAI's or
Anthropic's API (at the vendor or at another endpoint that speaks it, such as a company's gateway),
an OpenRouter key for many vendors' models, a ChatGPT plan added by signing in with ChatGPT, a
Copilot plan added with a code entered at GitHub, or a Claude plan. A workspace's
account serves everyone with Write on it; a personal account serves its owner, wherever they work.
Secrets are encrypted at rest and never shown again, and the keys and plans of model APIs never enter
a nook: the model gateway adds them to each call.

## Owns

- **Data:** the accounts: kind, name, whose they are (a workspace or a person), an API key's
  endpoint, when a sign-in ended, and the sealed secret; and sign-ins in progress.
- **Rules:** who may add and remove them (Manage on the workspace, or the person), who may use them
  (Write on the workspace, or the person; the control plane's own processes, any), which kinds are
  personal only (plans), which are added by signing in (ChatGPT and Copilot plans), and which kinds this
  deployment allows. What each kind is at its vendor: where its calls go and which headers pay for
  them, or that its token goes to its harness.
- **Integrations:** Sign in with ChatGPT, through `Bagatka.Sdk.OpenAI`: registering, exchanging the
  code, renewing a plan's access token, and ending its session on removal. GitHub's device flow for
  the host's Copilot app, through `Bagatka.Sdk.GitHub`. The vendors' lists of
  models: OpenAI's through `Bagatka.Sdk.OpenAI`, Anthropic's through `Bagatka.Sdk.Anthropic`,
  Copilot's through `Bagatka.Sdk.GitHub`, and OpenRouter's catalog through `Bagatka.Sdk.OpenRouter`.
  models.dev's open catalog of what each model takes, holds, and costs (`Catalog/`). What plans have
  used of their limits: ChatGPT's windows through `Bagatka.Sdk.OpenAI`, Copilot's quotas through
  `Bagatka.Sdk.GitHub`, and Claude's windows through `Bagatka.Sdk.Anthropic`, from the vendors'
  undocumented endpoints that their own clients read.

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

// A ChatGPT plan: the caller listens on the callback and opens the URL in the person's browser.
Result<SignInStarted> started = await accounts.StartSignInAsync(owner, new StartSignIn(AgentAccountKind.ChatGptPlan, "My ChatGPT", new Uri("http://127.0.0.1:1455/auth/callback")), ct);
Result<SignInProgress> plan = await accounts.CompleteSignInAsync(owner, new CompleteSignIn(started.Output.Id, returnedTo), ct);

// A Copilot plan: the person enters started.Output.UserCode at started.Output.Url, GitHub's; the
// caller completes without an address every interval until plan.Output.Account is there.
Result<SignInStarted> code = await accounts.StartSignInAsync(owner, new StartSignIn(AgentAccountKind.CopilotPlan, "My Copilot", Callback: null), ct);

Result<AgentAccountCredential> used = await accounts.UseAsync(SystemActors.Harness, plan.Output.Id, workspaceId, ct);
if (used.Failed)
{
    return new Result(used.Error);   // e.g. AgentAccountsErrors.SignInEnded
}

switch (used.Output.Access)
{
    case ModelEndpoint endpoint: ...; break;   // the gateway forwards to endpoint.Url with endpoint.Headers
    case HarnessToken token: ...; break;       // a Claude plan's token goes to the harness
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
Table `code_sign_ins` (who, the account's kind and name, the sealed device code, when it expires,
and when GitHub may be asked next).

## Background work

None. A plan's access token is renewed when it is used and due, never in the background.
models.dev's catalog is read again once a day old, by the next caller that needs it, never in the
background.

## Configuration

`AgentAccountsSettings`, passed by the host (`PATTERNS.md`, entry 20), with the deployment's
`EncryptionSettings`:

- `AllowClaudePlans`, off: Anthropic's terms forbid storing Claude sign-in tokens without
  its written permission, so turn it on only with that permission;
- `AllowChatGptPlans`, off: OpenAI lets open-source and self-hosted deployments use Sign in with
  ChatGPT; a hosted service for other people needs OpenAI's approval first. The AppHost turns it on,
  since a local run is self-hosted;
- `CopilotClientId`, the GitHub OAuth app people sign in to for their Copilot plans: AiSloth's own
  unless the host names another;
- `ChatGptAuthority`, `ChatGptApi`, `ChatGptBackend`, `OpenRouterApi`, `CopilotApi`, `ClaudeApi`, and `ModelsDevUrl`,
  the vendors' own by default; tests point them at fakes. GitHub itself is the host's `GitHubClient`'s.

## Decisions and constraints

- **A plan's limits, as its vendor counts them** (`PlanUsage`), asked of the vendor when someone
  looks, where its own clients ask: a ChatGPT plan's five-hour and weekly windows from ChatGPT's
  backend (`/wham/usage`, as Codex's `/status`; the API's answers to a Sign in with ChatGPT token
  carry none), Copilot's quotas (requests used of those allowed, or the credits a plan without
  limits spent), and a Claude plan's windows. No vendor documents these answers.

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
- **Model APIs go through the gateway.** `UseAsync` gives an API key's or a plan's calls an endpoint
  and the headers that pay for them (`ModelEndpoint`); only tokens tied to one harness go to it
  (`HarnessToken`). An endpoint's address is checked for its shape here; which networks it may be on,
  the gateway decides.
- **Signing in with ChatGPT** registers the app for the person's ChatGPT account (named "AiSloth"),
  with PKCE and no client secret. The ID token comes straight from OpenAI's token endpoint over TLS,
  so its claims are checked (issuer, audience, nonce, expiry) without its signature, as OpenID Connect
  allows. The person must allow the app to use their plan; a sign-in without it adds nothing. OpenAI
  identifies each installation: this deployment's ID derives from its encryption key.
- **A Copilot plan is Copilot's API, behind the gateway.** Copilot's CLI and pi call the gateway
  as an OpenAI-style provider, with their chat's token; the gateway calls Copilot's API with the
  plan's token and the headers Copilot's CLI sends (`CopilotApi.Headers`). Copilot doesn't document
  this API; its CLI's provider mode is documented.
- **Copilot plans sign in to an app of their own,** not the host's GitHub App: a plan is what pays
  for models, perhaps a work account's, and has nothing to do with whose repositories a person
  reaches. The app, a GitHub OAuth app, asks for no scopes, so its token reaches the person's
  Copilot and public data only; it never expires, so this host never renews it, and its client ID is
  public, so every host uses AiSloth's own. A token that expires anyway is refused.
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
- **Removing an account** stops model-gateway calls at once; an agent holding a token directly, a
  Claude plan's, keeps it until its process stops.
