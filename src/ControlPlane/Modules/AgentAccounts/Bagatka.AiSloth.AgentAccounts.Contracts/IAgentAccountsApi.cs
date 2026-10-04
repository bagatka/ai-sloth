using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using Bagatka.AiSloth.Workspaces.Contracts;
using Bagatka.Foundation;

namespace Bagatka.AiSloth.AgentAccounts.Contracts;

/// <summary>
/// Agent accounts: accounts at agent vendors that pay for agents' work, such as an Anthropic API key
/// or a Copilot plan. A workspace's account serves the people with Write on it; a personal account
/// serves its owner, wherever they work. Secrets are encrypted at rest and never shown again.
/// </summary>
public interface IAgentAccountsApi
{
    /// <summary>
    /// Adds an account: to a workspace, by one of its managers, or as the actor's own. Copilot tokens
    /// and Claude subscriptions are personal only.
    /// </summary>
    /// <returns>
    /// The account; a validation error for an invalid name or secret, or a kind that can't be added
    /// there or that this deployment doesn't allow; forbidden without Manage on the workspace; or not
    /// found when the actor has no access to it.
    /// </returns>
    public Task<Result<AgentAccountSummary>> AddAsync(Actor actor, AddAgentAccount command, CancellationToken ct);

    /// <summary>
    /// The accounts the actor can use in the workspace: the workspace's, for people with Write on it,
    /// then the actor's own, oldest first. Anyone else, such as a viewer or a nook's guest, sees only
    /// their own.
    /// </summary>
    public Task<Result<IReadOnlyList<AgentAccountSummary>>> ListAsync(Actor actor, WorkspaceId workspaceId, CancellationToken ct);

    /// <summary>
    /// Removes an account: a personal one by its owner, a workspace's by its managers. Agents already
    /// running with a secret they hold keep it until they stop; calls through the model gateway stop at once.
    /// </summary>
    public Task<Result> RemoveAsync(Actor actor, AgentAccountId id, CancellationToken ct);

    /// <summary>
    /// The account's credential, for running an agent in the workspace (<see cref="MayUseAsync"/>).
    /// Not found for anyone who may not use it, or when it was removed.
    /// </summary>
    public Task<Result<AgentAccountCredential>> UseAsync(Actor actor, AgentAccountId id, WorkspaceId workspaceId, CancellationToken ct);

    /// <summary>
    /// Whether the actor may run agents on the account in the workspace: a workspace's accounts serve
    /// people with Write on it, a personal account its owner, and the control plane's own processes
    /// any. <see langword="null"/> when the account doesn't exist, such as after it was removed, or
    /// belongs to another workspace.
    /// </summary>
    public Task<bool?> MayUseAsync(Actor actor, AgentAccountId id, WorkspaceId workspaceId, CancellationToken ct);
}
