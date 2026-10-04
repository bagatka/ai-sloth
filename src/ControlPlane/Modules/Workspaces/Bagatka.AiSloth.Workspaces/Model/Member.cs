using Bagatka.AiSloth.Workspaces.Contracts;
using Bagatka.Foundation;

namespace Bagatka.AiSloth.Workspaces.Model;

// A user's membership in a workspace. Created and changed only by its workspace.
internal sealed class Member
{
    public Member(WorkspaceId workspaceId, UserId userId, WorkspaceRole role)
    {
        WorkspaceId = workspaceId;
        UserId = userId;
        Role = role;
    }

    public WorkspaceId WorkspaceId { get; private set; }

    public UserId UserId { get; private set; }

    public WorkspaceRole Role { get; private set; }
}
