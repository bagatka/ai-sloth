namespace Bagatka.AiSloth.Workspaces.Contracts;

/// <summary>Input to <see cref="IWorkspacesApi.AcceptInviteAsync"/>.</summary>
/// <param name="Code">The invite's code, as the person typed or pasted it.</param>
public sealed record AcceptInvite(string Code);
