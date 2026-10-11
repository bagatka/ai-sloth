using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using Bagatka.AiSloth.Workspaces.Contracts;
using Bagatka.Foundation;

namespace Bagatka.AiSloth.AgentAccounts.Contracts;

/// <summary>
/// Agent accounts: accounts at agent vendors that pay for agents' work, such as an OpenAI API key, a
/// ChatGPT plan, or a Copilot plan. A workspace's account serves the people with Write on it; a
/// personal account serves its owner, wherever they work. Secrets are encrypted at rest and never shown
/// again, and the keys and plans of model APIs never enter a nook: the model gateway adds them.
/// </summary>
public interface IAgentAccountsApi
{
    /// <summary>Every kind of account, how each is added, and whether this host allows it, for clients to offer.</summary>
    public Task<IReadOnlyList<AgentAccountKindSummary>> ListKindsAsync(Actor actor, CancellationToken ct);

    /// <summary>
    /// Adds an account with its secret: to a workspace, by one of its managers, or as the actor's own.
    /// Plans are personal only. An API key may name another endpoint than its vendor's. ChatGPT and
    /// Copilot plans are added by signing in instead (<see cref="StartSignInAsync"/>).
    /// </summary>
    /// <returns>
    /// The account; a validation error for an invalid name, secret, or endpoint, or a kind that can't be
    /// added there or this way, or that this deployment doesn't allow; forbidden without Manage on the
    /// workspace; or not found when the actor has no access to it.
    /// </returns>
    public Task<Result<AgentAccountSummary>> AddAsync(Actor actor, AddAgentAccount command, CancellationToken ct);

    /// <summary>
    /// Starts adding the actor's own account by signing in at its vendor, for kinds added this way. A
    /// ChatGPT plan: open the answer's URL in the person's browser; ChatGPT sends it back to the
    /// callback, and <see cref="CompleteSignInAsync"/> finishes, within ten minutes. A Copilot plan: the
    /// person enters the answer's code at GitHub's URL, while <see cref="CompleteSignInAsync"/> asks
    /// whether they have, until the code expires. Asks GitHub for the code.
    /// </summary>
    /// <returns>
    /// The sign-in; a validation error for an invalid name, a callback the vendor can't return to (or
    /// any, for a sign-in with a code), or a kind added with its secret or that this deployment doesn't
    /// allow; forbidden for anyone but a person.
    /// </returns>
    public Task<Result<SignInStarted>> StartSignInAsync(Actor actor, StartSignIn command, CancellationToken ct);

    /// <summary>
    /// Finishes a sign-in and adds the account: with the address the person's browser returned to, or,
    /// with a code, once they approved it at GitHub; until then the progress says when to ask again,
    /// and asking sooner waits until then. The vendor's answer is used once: a failed sign-in starts
    /// again.
    /// </summary>
    /// <returns>
    /// The progress; a validation error when the person declined, didn't allow the app to use their
    /// plan, or the vendor refused the answer; or <see cref="AgentAccountsErrors.SignInNotFound"/>,
    /// also once a code expired or was declined.
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
    /// asked with its credentials, newest first; a ChatGPT plan's catalog in its own order; a Copilot
    /// plan's chat models from Copilot's API; and a Claude plan's from Anthropic's. Asks the vendor each
    /// time, which throws when it can't be reached; try again later.
    /// </summary>
    /// <returns>
    /// The models; <see cref="AgentAccountsErrors.SignInEnded"/>; or not found for anyone who may not
    /// use the account in the workspace.
    /// </returns>
    public Task<Result<IReadOnlyList<AgentModel>>> ListModelsAsync(Actor actor, AgentAccountId id, WorkspaceId workspaceId, CancellationToken ct);

    /// <summary>
    /// Removes an account: a personal one by its owner, a workspace's by its managers. Agents already
    /// running with a token they hold keep it until they stop; calls through the model gateway stop at
    /// once. A sign-in's session at the vendor ends too, when the vendor can be reached.
    /// </summary>
    public Task<Result> RemoveAsync(Actor actor, AgentAccountId id, CancellationToken ct);

    /// <summary>
    /// What running an agent on the account in the workspace takes (<see cref="MayUseAsync"/>), called
    /// when an agent starts and for every call through the model gateway. A ChatGPT plan's access token
    /// is renewed here when it is due, one renewal at a time per account, which may take a moment and
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
