using Bagatka.Foundation;

namespace Bagatka.AiSloth.Workspaces.Contracts;

/// <summary>Input to <see cref="IWorkspacesApi.RevokeAsync"/>.</summary>
/// <param name="Resource">The resource.</param>
/// <param name="UserId">Whose access to it ends.</param>
public sealed record RevokeAccess(Resource Resource, UserId UserId);
