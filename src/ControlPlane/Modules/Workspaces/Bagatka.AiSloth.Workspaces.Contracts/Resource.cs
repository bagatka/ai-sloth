using System;

namespace Bagatka.AiSloth.Workspaces.Contracts;

/// <summary>
/// Something people are given access to: a workspace or a nook. Access to a resource reaches what is
/// in it, so a workspace's editor writes in all its nooks.
/// </summary>
/// <param name="Kind">What it is.</param>
/// <param name="Id">Its ID, in the module that owns it.</param>
public sealed record Resource(ResourceKind Kind, Guid Id)
{
    /// <summary>The workspace.</summary>
    public static Resource Workspace(WorkspaceId id)
    {
        return new Resource(ResourceKind.Workspace, id.Value);
    }

    /// <summary>The nook with this ID (Nooks' <c>NookId.Value</c>).</summary>
    public static Resource Nook(Guid nookId)
    {
        return new Resource(ResourceKind.Nook, nookId);
    }
}
