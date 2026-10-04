using System;
using System.Security.Cryptography;
using System.Text;
using Bagatka.AiSloth.Chats.Contracts;
using Bagatka.AiSloth.Nooks.Contracts;
using Bagatka.AiSloth.Workspaces.Contracts;
using Bagatka.Foundation;

namespace Bagatka.AiSloth.Chats.Model;

// A conversation with a coding agent in one nook. After it starts, its runner is the only writer:
// the agent's process and session, the turn in progress, how far the agent's output is read, and
// the sequence number of the last event.
internal sealed class Chat
{
    // Used by Start and by EF: parameter names match property names.
    private Chat(ChatId id, NookId nookId, WorkspaceId workspaceId, UserId startedBy, DateTimeOffset startedAt)
    {
        Id = id;
        NookId = nookId;
        WorkspaceId = workspaceId;
        StartedBy = startedBy;
        StartedAt = startedAt;
    }

    public ChatId Id { get; private set; }

    public NookId NookId { get; private set; }

    public WorkspaceId WorkspaceId { get; private set; }

    public UserId StartedBy { get; private set; }

    public DateTimeOffset StartedAt { get; private set; }

    // The agent's process, while it runs, and the SHA-256 of the token its model calls carry.
    public ProcessId? HarnessProcessId { get; private set; }

    public byte[]? HarnessTokenHash { get; private set; }

    // How far the agent's output is read: everything before this offset is handled.
    public long OutputOffset { get; private set; }

    // The agent's session, once it answered session/new, and whether it accepts messages mid-turn.
    public string? SessionId { get; private set; }

    public bool SupportsSteering { get; private set; }

    // The message whose turn is running.
    public MessageId? TurnMessageId { get; private set; }

    public long LastSequence { get; private set; }

    // PostgreSQL's xmin: a second writer conflicts instead of overwriting.
    public uint Version { get; private set; }

    public static Chat Start(NookId nookId, WorkspaceId workspaceId, UserId startedBy, TimeProvider time)
    {
        return new Chat(ChatId.New(), nookId, workspaceId, startedBy, time.GetUtcNow());
    }

    public static byte[] HashToken(string token)
    {
        return SHA256.HashData(Encoding.UTF8.GetBytes(token));
    }

    // A new token for an agent about to start; it replaces the last agent's.
    public string IssueHarnessToken()
    {
        string token = Convert.ToBase64String(RandomNumberGenerator.GetBytes(32));
        HarnessTokenHash = HashToken(token);
        return token;
    }

    public void HarnessStarted(ProcessId processId)
    {
        HarnessProcessId = processId;
        OutputOffset = 0;
        SessionId = null;
        SupportsSteering = false;
    }

    // The agent's process is gone, with its session and any turn it was working on.
    public void HarnessStopped()
    {
        HarnessProcessId = null;
        HarnessTokenHash = null;
        OutputOffset = 0;
        SessionId = null;
        SupportsSteering = false;
        TurnMessageId = null;
    }

    public void Initialized(bool supportsSteering)
    {
        SupportsSteering = supportsSteering;
    }

    public void SessionReady(string sessionId)
    {
        SessionId = sessionId;
    }

    public void Read(long offset)
    {
        OutputOffset = offset;
    }

    public void TurnStarted(MessageId messageId)
    {
        TurnMessageId = messageId;
    }

    public void TurnEnded()
    {
        TurnMessageId = null;
    }

    public StoredEvent Record(ChatEventBody body, TimeProvider time)
    {
        LastSequence++;
        return StoredEvent.From(Id, LastSequence, time.GetUtcNow(), body);
    }

    public ChatSummary ToSummary(bool messagesWaiting)
    {
        return new ChatSummary(Id, NookId, WorkspaceId, StartedBy, StartedAt, TurnMessageId is not null || messagesWaiting);
    }
}
