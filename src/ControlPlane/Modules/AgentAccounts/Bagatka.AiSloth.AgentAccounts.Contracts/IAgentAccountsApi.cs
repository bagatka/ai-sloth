using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using Bagatka.AiSloth.Workspaces.Contracts;
using Bagatka.Foundation;
using Bagatka.ModelAccess;

namespace Bagatka.AiSloth.AgentAccounts.Contracts;

/// <summary>
/// Agent accounts: accounts at agent vendors that pay for agents' work, such as an OpenAI API key, or a
/// ChatGPT, Claude, or Copilot plan. A workspace's account serves the people with Write on it; a
/// personal account serves its owner, wherever they work. Secrets are encrypted at rest and never shown
/// again, and the keys and plans of model APIs never enter a nook: the model gateway adds them.
/// </summary>
public interface IAgentAccountsApi
{
    /// <summary>Every kind of account, how each is added, and whether this host allows it, for clients to offer.</summary>
    public Task<IReadOnlyList<AgentAccountKindSummary>> ListKindsAsync(Actor actor, CancellationToken ct);

    /// <summary>
    /// Adds an account with its secret: to a workspace, by one of its managers, or as the actor's own.
    /// API keys only: plans are added by signing in (<see cref="StartSignInAsync"/>). An API key may name
    /// another endpoint than its vendor's.
    /// </summary>
    /// <returns>
    /// The account; a validation error for an invalid name, secret, or endpoint, or a kind that can't be
    /// added there or this way, or that this deployment doesn't allow; forbidden without Manage on the
    /// workspace; or not found when the actor has no access to it.
    /// </returns>
    public Task<Result<AgentAccountSummary>> AddAsync(Actor actor, AddAgentAccount command, CancellationToken ct);

    /// <summary>
    /// Starts adding the actor's own plan by signing in at its vendor, as the vendor's own agent signs in.
    /// A ChatGPT plan (as Codex) or a Copilot plan: the person enters the answer's code at its URL, at
    /// OpenAI or GitHub, while <see cref="CompleteSignInAsync"/> asks whether they have, until the code
    /// expires; asks the vendor for the code. A Claude plan (as Claude Code): the person approves at the
    /// answer's URL, and <see cref="CompleteSignInAsync"/> finishes with the code Claude's page shows
    /// them, within ten minutes.
    /// </summary>
    /// <returns>
    /// The sign-in; a validation error for an invalid name, or a kind added with its secret or that this
    /// deployment doesn't allow; forbidden for anyone but a person.
    /// </returns>
    public Task<Result<SignInStarted>> StartSignInAsync(Actor actor, StartSignIn command, CancellationToken ct);

    /// <summary>
    /// Finishes a sign-in and adds the account: with the code Claude's page showed, or, for a sign-in with
    /// a user code, once the person approved it at the vendor; until then the progress says when to ask
    /// again, and asking sooner waits until then. A failed attempt may be retried until the sign-in
    /// expires, though a code the vendor refused stays refused: then start again.
    /// </summary>
    /// <returns>
    /// The progress; a validation error for a code of another sign-in or one the vendor refused; or
    /// <see cref="AgentAccountsErrors.SignInNotFound"/>, also once a code expired or was declined.
    /// </returns>
    public Task<Result<SignInProgress>> CompleteSignInAsync(Actor actor, CompleteSignIn command, CancellationToken ct);

    /// <summary>
    /// The accounts the actor can use in the workspace: the workspace's, for people with Write on it,
    /// then the actor's own, oldest first. Anyone else, such as a viewer or a nook's guest, sees only
    /// their own.
    /// </summary>
    public Task<Result<IReadOnlyList<AgentAccountSummary>>> ListAsync(Actor actor, WorkspaceId workspaceId, CancellationToken ct);

    /// <summary>
    /// The models the account's vendor offers, for choosing one per chat: OpenRouter's catalog without
    /// its batch variants, and the lists of Anthropic's and OpenAI's APIs at the account's endpoint,
    /// asked with its credentials, newest first; a ChatGPT plan's from ChatGPT's Codex backend, in its
    /// own order; a Copilot plan's chat models from Copilot's API; and a Claude plan's from Anthropic's.
    /// Asks the vendor each time, which throws when it can't be reached; try again later. Each comes
    /// with what's known of it (<see cref="ModelFacts"/>).
    /// </summary>
    /// <returns>
    /// The models; <see cref="AgentAccountsErrors.SignInEnded"/>; or not found for anyone who may not
    /// use the account in the workspace.
    /// </returns>
    public Task<Result<IReadOnlyList<AgentModel>>> ListModelsAsync(Actor actor, AgentAccountId id, WorkspaceId workspaceId, CancellationToken ct);

    /// <summary>
    /// What's known of a model at the account, from models.dev's catalog alone, without asking the
    /// vendor: for pricing an agent's calls as they happen. The catalog is read again daily.
    /// </summary>
    /// <returns>The facts, <see cref="ModelFacts.Unknown"/> for a model the catalog lacks; or not found for anyone who may not use the account.</returns>
    public Task<Result<ModelFacts>> GetModelFactsAsync(Actor actor, AgentAccountId id, WorkspaceId workspaceId, string model, CancellationToken ct);

    /// <summary>
    /// What a plan has used of its limits (<see cref="PlanUsage"/>), asked of its vendor now, as the
    /// vendor's own agent asks: a ChatGPT plan's five-hour and weekly windows (as Codex's
    /// <c>/status</c>), a Claude plan's (as Claude Code's <c>/usage</c>), and a Copilot plan's quotas or
    /// credits spent; nothing for an API key. A plan's access token is renewed first when it is due. No
    /// vendor documents these answers; asking throws when the vendor can't be reached or refuses.
    /// </summary>
    /// <returns>
    /// The usage; a conflict when the vendor asks to be asked later, as Anthropic does when asked about
    /// every minute; <see cref="AgentAccountsErrors.SignInEnded"/>; or not found for anyone who may not
    /// use the account.
    /// </returns>
    public Task<Result<PlanUsage>> GetPlanUsageAsync(Actor actor, AgentAccountId id, WorkspaceId workspaceId, CancellationToken ct);

    /// <summary>
    /// Removes an account: a personal one by its owner, a workspace's by its managers. Calls through the
    /// model gateway stop at once. A plan's tokens go with it; the person can also disconnect it at the
    /// vendor.
    /// </summary>
    public Task<Result> RemoveAsync(Actor actor, AgentAccountId id, CancellationToken ct);

    /// <summary>
    /// What running an agent on the account in the workspace takes (<see cref="MayUseAsync"/>), called
    /// when an agent starts and for every call through the model gateway. A ChatGPT or Claude plan's
    /// access token is renewed here when it is due, one renewal at a time per account, which may take a moment and
    /// throws when the vendor can't be reached; try again later.
    /// </summary>
    /// <returns>
    /// The credential; <see cref="AgentAccountsErrors.SignInEnded"/> once the vendor ended the account's
    /// sign-in; or not found for anyone who may not use it, or when it was removed.
    /// </returns>
    public Task<Result<AgentAccountCredential>> UseAsync(Actor actor, AgentAccountId id, WorkspaceId workspaceId, CancellationToken ct);

    /// <summary>
    /// Whether the actor may run agents on the account in the workspace: a workspace's accounts serve
    /// people with Write on it, a personal account its owner, and the control plane's own processes
    /// any. <see langword="null"/> when the account doesn't exist, such as after it was removed, or
    /// belongs to another workspace.
    /// </summary>
    public Task<bool?> MayUseAsync(Actor actor, AgentAccountId id, WorkspaceId workspaceId, CancellationToken ct);
}
