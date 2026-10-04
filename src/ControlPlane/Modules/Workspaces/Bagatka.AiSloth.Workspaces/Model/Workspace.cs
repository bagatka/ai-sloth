using System;
using System.Collections.Generic;
using Bagatka.AiSloth.Workspaces.Contracts;
using Bagatka.Foundation;

namespace Bagatka.AiSloth.Workspaces.Model;

// Where people work together; it owns its members.
internal sealed class Workspace
{
    private readonly List<Member> _members = [];

    // Used by Create and by EF: parameter names match property names.
    private Workspace(WorkspaceId id, WorkspaceName name, DateTimeOffset createdAt)
    {
        Id = id;
        Name = name;
        CreatedAt = createdAt;
    }

    public WorkspaceId Id { get; private set; }

    public WorkspaceName Name { get; private set; }

    public DateTimeOffset CreatedAt { get; private set; }

    public IReadOnlyList<Member> Members => _members;

    // The creator owns the new workspace.
    public static Workspace Create(WorkspaceName name, UserId owner, TimeProvider time)
    {
        Workspace workspace = new Workspace(WorkspaceId.New(), name, time.GetUtcNow());
        workspace._members.Add(new Member(workspace.Id, owner, WorkspaceRole.Owner));
        return workspace;
    }
}
