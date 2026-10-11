# ModelAccess

A general-purpose library for paying for model vendors' models the way coding agents do: with an
API key, at the vendor or another endpoint that speaks its API, or with a person's plan, signed in as
the vendor's own agent signs in. It says where an agent's calls go and which headers pay for them,
which models each way offers and what's known of them, what a plan has used of its limits, and what
a model call used. It keeps nothing but models.dev's catalog: the caller stores every credential and
sign-in. Nothing here knows AiSloth; AgentAccounts is its caller, and the model gateway reads usage
with it.

## Contract

`Bagatka.ModelAccess`:

- `ModelVendors`, the one front door, the same for every kind (`ModelAccessKind`): `Endpoint`,
  `ListModelsAsync` and `FactsAsync` (`AgentModel`, `ModelFacts`: reasoning, limits, price, cache
  lifetime), and for a plan `StartSignInAsync`, `ContinueSignInAsync`, `RenewAsync`, and
  `LimitsAsync` (`UsageMeter`). `AddModelAccess` registers it with `ModelAccessSettings`, where the
  vendors are; the host registers a `GitHubClient`, which Copilot plans sign in with.
- `ModelCredential`: what pays, which the caller keeps sealed: a key or token, and for a plan, its
  refresh token, when it expires, and the vendor's account it uses.
- `ModelUsageReader`: the usage a model's answer reports (`ModelCallUsage`), read from its bytes as
  they pass, streamed or whole: Anthropic's Messages API, OpenAI's Responses and chat APIs.

```csharp
PendingSignIn pending = await vendors.StartSignInAsync(ModelAccessKind.ClaudePlan, state, ct);  // pending.Url: Claude's page
Result<SignInStep> step = await vendors.ContinueSignInAsync(ModelAccessKind.ClaudePlan, state, pending.UserCode, pending.Secret, pastedCode, ct);
ModelCredential credential = step.Output.Credential!;                   // the caller seals and keeps it

if (credential.RenewalDue(now))
{
    Result<ModelCredential> renewed = await vendors.RenewAsync(ModelAccessKind.ClaudePlan, credential, ct); // never two at once
}

ModelEndpoint endpoint = vendors.Endpoint(ModelAccessKind.ClaudePlan, credential, ownEndpoint: null);  // forward an agent's calls here
IReadOnlyList<AgentModel> models = await vendors.ListModelsAsync(ModelAccessKind.ClaudePlan, credential, ownEndpoint: null, ct);
IReadOnlyList<UsageMeter> limits = await vendors.LimitsAsync(ModelAccessKind.ClaudePlan, credential, ct); // 5 hours 37%, Week 8%
```

## Rules

- **Each kind is one class** in `Kinds/`, behind `IAccountKind` (a plan's, `IPlanKind`); adding one
  is a class, a member of `ModelAccessKind`, and a line in `AddModelAccess`.
- **A plan signs in as its vendor's own agent,** with that agent's OAuth client, and runs only that
  agent: a ChatGPT plan as Codex (a code entered at OpenAI; its calls go to ChatGPT's Codex backend,
  `chatgpt.com/backend-api/codex`, naming the ChatGPT account), a Claude plan as Claude Code
  (Claude's page shows `code#state` to bring back; Claude Code, on Anthropic's Agent SDK, calls
  Anthropic's API with the plan's token and the `oauth-2025-04-20` beta), and a Copilot plan with a
  code entered at GitHub for an OAuth app that asks for no scopes and whose tokens never expire,
  so they are never renewed; one that expires anyway is refused. These flows are read from the
  vendors' own agents; none is documented.
- **A plan's limits, as its vendor counts them,** asked where its own clients ask: ChatGPT's
  five-hour and weekly windows (`/wham/usage`, as Codex's `/status`), Claude's (`/api/oauth/usage`,
  as Claude Code's `/usage`), and Copilot's quotas (requests used of those allowed, or the credits a
  plan without limits spent).
- **One OpenRouter key, both APIs.** OpenRouter serves Anthropic's Messages API at its root
  (`/v1/messages`) and OpenAI's at `/v1`, so each harness calls the API it speaks. Only Anthropic and
  OpenAI API keys name an endpoint of their own.
- **Models come from each vendor's list.** OpenRouter's catalog is public, without its batch
  variants; Anthropic's and OpenAI's lists are asked at the account's endpoint with its credential,
  and of OpenAI's only its GPT, o-series, and Codex models are offered. A ChatGPT plan's are those
  ChatGPT's Codex backend offers the Codex the nooks run; a Copilot plan's, the chat models its policy
  enables; a Claude plan's, Anthropic's list for the plan.
- **What's known of a model** (`ModelFacts`): what the vendor states stands over the catalog's word
  (Copilot's reasoning efforts and limits, OpenRouter's prices and context). models.dev's catalog
  (MIT, `Catalog/models-dev.LICENSE`) says the rest: a snapshot built in
  (`Catalog/update-models-dev.sh`), read again daily by the next caller, keeping what it has when
  that fails. An API key pays its vendor's price; a plan's price is an estimate at the vendor's API
  prices; an endpoint of the account's own has no price that can be known. Cache lifetimes are the
  vendors' rules: Anthropic's five minutes, OpenAI's five or thirty for GPT-5.6 and later.
