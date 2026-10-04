using Bagatka.AiSloth.Workspaces.Contracts;

namespace Bagatka.AiSloth.Nooks.Contracts;

/// <summary>
/// Input to <see cref="INooksApi.CreateAsync"/>.
/// </summary>
/// <param name="WorkspaceId">The workspace that will own the nook.</param>
/// <param name="Provider">The name of the sandbox provider to run it on, such as <c>docker</c>.</param>
public sealed record CreateNook(WorkspaceId WorkspaceId, string Provider);
