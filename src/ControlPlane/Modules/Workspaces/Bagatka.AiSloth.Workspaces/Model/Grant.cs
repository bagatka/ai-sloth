using System;
using Bagatka.AiSloth.Workspaces.Contracts;
using Bagatka.Foundation;

namespace Bagatka.AiSloth.Workspaces.Model;

// One person's access to one resource, given directly. Resources are matched by ID alone: IDs are
// UUIDv7s, unique across kinds.
internal sealed class Grant
{
    // Used by Give and by EF: parameter names match property names.
    private Grant(ResourceKind resourceKind, Guid resourceId, UserId userId, AccessLevel access)
    {
        ResourceKind = resourceKind;
        ResourceId = resourceId;
        UserId = userId;
        Access = access;
    }

    public ResourceKind ResourceKind { get; private set; }

    public Guid ResourceId { get; private set; }

    public UserId UserId { get; private set; }

    public AccessLevel Access { get; private set; }

    public static Grant Give(Resource resource, UserId userId, AccessLevel access)
    {
        return new Grant(resource.Kind, resource.Id, userId, access);
    }

    // Keeps the higher of the two levels, so an invite never lowers anyone's access.
    public void Raise(AccessLevel access)
    {
        if (access > Access)
        {
            Access = access;
        }
    }

    public GrantSummary ToSummary()
    {
        return new GrantSummary(UserId, Access);
    }
}
