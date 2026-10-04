using System;
using Bagatka.AiSloth.Workspaces.Contracts;

namespace Bagatka.AiSloth.Workspaces.Model;

// A resource is in another, so access to the parent reaches the child: a nook in its workspace.
internal sealed class ResourceLink
{
    // Used by Link and by EF: parameter names match property names.
    private ResourceLink(ResourceKind childKind, Guid childId, ResourceKind parentKind, Guid parentId)
    {
        ChildKind = childKind;
        ChildId = childId;
        ParentKind = parentKind;
        ParentId = parentId;
    }

    public ResourceKind ChildKind { get; private set; }

    public Guid ChildId { get; private set; }

    public ResourceKind ParentKind { get; private set; }

    public Guid ParentId { get; private set; }

    public static ResourceLink Link(Resource child, Resource parent)
    {
        return new ResourceLink(child.Kind, child.Id, parent.Kind, parent.Id);
    }
}
