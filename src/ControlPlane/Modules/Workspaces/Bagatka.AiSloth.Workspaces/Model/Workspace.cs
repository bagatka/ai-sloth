using System;
using Bagatka.AiSloth.Workspaces.Contracts;

namespace Bagatka.AiSloth.Workspaces.Model;

// Where people work together. Who may do what in it are grants on it (Grant).
internal sealed class Workspace
{
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

    public static Workspace Create(WorkspaceName name, TimeProvider time)
    {
        return new Workspace(WorkspaceId.New(), name, time.GetUtcNow());
    }
}
