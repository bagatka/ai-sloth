using System;
using Bagatka.AiSloth.Workspaces.Contracts;
using Bagatka.Foundation;

namespace Bagatka.AiSloth.Chats.Model;

// A workspace's instructions for every agent of its chats (AgentInstructions).
internal sealed class WorkspaceInstructions
{
    // Used by Write and by EF: parameter names match property names.
    private WorkspaceInstructions(WorkspaceId workspaceId, string text, DateTimeOffset updatedAt, UserId updatedBy)
    {
        WorkspaceId = workspaceId;
        Text = text;
        UpdatedAt = updatedAt;
        UpdatedBy = updatedBy;
    }

    public WorkspaceId WorkspaceId { get; private set; }

    public string Text { get; private set; }

    public DateTimeOffset UpdatedAt { get; private set; }

    public UserId UpdatedBy { get; private set; }

    // The text is checked by AgentInstructions.Check.
    public static WorkspaceInstructions Write(WorkspaceId workspaceId, string text, UserId by, TimeProvider time)
    {
        return new WorkspaceInstructions(workspaceId, text, time.GetUtcNow(), by);
    }

    public void Rewrite(string text, UserId by, TimeProvider time)
    {
        Text = text;
        UpdatedAt = time.GetUtcNow();
        UpdatedBy = by;
    }
}
