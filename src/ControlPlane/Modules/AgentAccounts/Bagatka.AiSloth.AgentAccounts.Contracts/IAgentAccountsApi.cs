using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using Bagatka.AiSloth.Workspaces.Contracts;
using Bagatka.Foundation;

namespace Bagatka.AiSloth.AgentAccounts.Contracts;

/// <summary>
/// Agent accounts: accounts at agent vendors that pay for agents' work, such as an Anthropic API key
/// or a Copilot plan. A workspace's account serves all its members; a personal account serves its
/// owner, in every workspace they belong to. Secrets are encrypted at rest and never shown again.
/// </summary>
public interface IAgentAccountsApi
{
    /// <summary>
    /// Adds an account: to a workspace, by one of its owners, or as the actor's own. Copilot tokens
    /// and Claude subscriptions are personal only.
    /// </summary>
    /// <returns>
    /// The account; a validation error for an invalid name or secret, or a kind that can't be added
    /// there or that this deployment doesn't allow; forbidden for a member who isn't an owner; or not
    /// found when the actor isn't a member of the workspace.
    /// </returns>
    public Task<Result<AgentAccountSummary>> AddAsync(Actor actor, AddAgentAccount command, CancellationToken ct);

    /// <summary>
    /// The accounts the actor can use in the workspace: the workspace's, then the actor's own, oldest
    /// first. Not found when the actor isn't a member.
    /// </summary>
    public Task<Result<IReadOnlyList<AgentAccountSummary>>> ListAsync(Actor actor, WorkspaceId workspaceId, CancellationToken ct);

    /// <summary>
    /// Removes an account: a personal one by its owner, a workspace's by its owners. Agents already
    /// running with a secret they hold keep it until they stop; calls through the model gateway stop at once.
    /// </summary>
    public Task<Result> RemoveAsync(Actor actor, AgentAccountId id, CancellationToken ct);

    /// <summary>
    /// The account's credential, for running an agent in the workspace. A member may use the
    /// workspace's accounts and a person their own; the control plane's own processes may use any.
    /// Not found otherwise, or when it was removed.
    /// </summary>
    public Task<Result<AgentAccountCredential>> UseAsync(Actor actor, AgentAccountId id, WorkspaceId workspaceId, CancellationToken ct);
}
