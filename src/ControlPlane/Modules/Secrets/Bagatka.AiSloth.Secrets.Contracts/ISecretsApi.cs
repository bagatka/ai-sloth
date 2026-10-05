using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using Bagatka.AiSloth.Workspaces.Contracts;
using Bagatka.Foundation;

namespace Bagatka.AiSloth.Secrets.Contracts;

/// <summary>
/// Secrets: environment variables a workspace gives to every process in its nooks, agents included,
/// for the tools they run, such as <c>GH_TOKEN</c> for <c>gh</c>. Managers set and remove them;
/// everyone in the workspace sees their names, never their values, which are encrypted at rest.
/// </summary>
/// <remarks>
/// A secret is in its nooks for anyone who may write there to use and read, the workspace's people and
/// a nook's guests alike. A process gets the values as they were when it started.
/// </remarks>
public interface ISecretsApi
{
    /// <summary>Sets a secret, adding it or replacing its value. Managers of the workspace only.</summary>
    /// <returns>
    /// The secret; a validation error for a name that isn't an environment variable's, or is the
    /// system's own (<c>PATH</c>, <c>HOME</c>, <c>LD_*</c>, <c>SLOTHD_*</c>), or an empty or too long value;
    /// a conflict when the workspace already holds the most secrets it may; forbidden without Manage; or
    /// not found without access to the workspace.
    /// </returns>
    public Task<Result<SecretSummary>> SetAsync(Actor actor, SetSecret command, CancellationToken ct);

    /// <summary>The workspace's secrets, by name, without their values. Not found without access to the workspace.</summary>
    public Task<Result<IReadOnlyList<SecretSummary>>> ListAsync(Actor actor, WorkspaceId workspaceId, CancellationToken ct);

    /// <summary>
    /// Removes a secret. Processes already running keep it; new ones don't get it.
    /// </summary>
    /// <returns>Success; forbidden without Manage; or not found.</returns>
    public Task<Result> RemoveAsync(Actor actor, RemoveSecret command, CancellationToken ct);

    /// <summary>
    /// The workspace's secrets with their values, for starting a process in one of its nooks. For the
    /// control plane's own processes only, never a route: forbidden for anyone else. Never log the values.
    /// </summary>
    public Task<Result<IReadOnlyDictionary<string, string>>> ResolveAsync(Actor actor, WorkspaceId workspaceId, CancellationToken ct);
}
