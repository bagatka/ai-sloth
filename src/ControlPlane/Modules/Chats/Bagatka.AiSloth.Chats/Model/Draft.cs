using System;
using Bagatka.AiSloth.Chats.Contracts;
using Bagatka.AiSloth.Nooks.Contracts;
using Bagatka.AiSloth.Workspaces.Contracts;
using Bagatka.Foundation;

namespace Bagatka.AiSloth.Chats.Model;

// A chat nobody wrote in yet (README, "Drafts"), from its start until its first message. Kept apart
// from chats, so finding drafts never reads every chat.
internal sealed class Draft
{
    // Used by Of and by EF: parameter names match property names.
    private Draft(ChatId chatId, NookId nookId, WorkspaceId workspaceId, UserId startedBy, DateTimeOffset startedAt)
    {
        ChatId = chatId;
        NookId = nookId;
        WorkspaceId = workspaceId;
        StartedBy = startedBy;
        StartedAt = startedAt;
    }

    public ChatId ChatId { get; private set; }

    public NookId NookId { get; private set; }

    public WorkspaceId WorkspaceId { get; private set; }

    public UserId StartedBy { get; private set; }

    public DateTimeOffset StartedAt { get; private set; }

    public static Draft Of(Chat chat)
    {
        return new Draft(chat.Id, chat.NookId, chat.WorkspaceId, chat.StartedBy, chat.StartedAt);
    }
}
