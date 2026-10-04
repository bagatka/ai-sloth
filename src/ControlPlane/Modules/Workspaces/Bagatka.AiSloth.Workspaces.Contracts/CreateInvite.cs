namespace Bagatka.AiSloth.Workspaces.Contracts;

/// <summary>Input to <see cref="IWorkspacesApi.InviteAsync"/>.</summary>
/// <param name="Resource">What the invite gives access to.</param>
/// <param name="Access">How much.</param>
public sealed record CreateInvite(Resource Resource, AccessLevel Access);
