using System;
using System.Security.Cryptography;
using System.Text;
using Bagatka.AiSloth.AgentAccounts.Contracts;
using Bagatka.AiSloth.Chats.Contracts;
using Bagatka.AiSloth.Nooks.Contracts;
using Bagatka.AiSloth.Workspaces.Contracts;
using Bagatka.Foundation;

namespace Bagatka.AiSloth.Chats.Model;

// A conversation with a coding agent in one nook, run by a harness on an agent account. After it
// starts, its runner is the only writer: the agent's process and session, the turn in progress, how
// far the agent's output is read, and the sequence number of the last event.
internal sealed class Chat
{
    public const int MaxHarnessLength = 32;

    // Used by Start and by EF: parameter names match property names.
    private Chat(ChatId id, NookId nookId, WorkspaceId workspaceId, UserId startedBy, DateTimeOffset startedAt, string harness, AgentAccountId agentAccountId, UserId? accountOwnerId)
    {
        Id = id;
        NookId = nookId;
        WorkspaceId = workspaceId;
        StartedBy = startedBy;
        StartedAt = startedAt;
        Harness = harness;
        AgentAccountId = agentAccountId;
        AccountOwnerId = accountOwnerId;
    }

    public ChatId Id { get; private set; }

    public NookId NookId { get; private set; }

    public WorkspaceId WorkspaceId { get; private set; }

    public UserId StartedBy { get; private set; }

    public DateTimeOffset StartedAt { get; private set; }

    public string Harness { get; private set; }

    public AgentAccountId AgentAccountId { get; private set; }

    // The personal account's owner; null for the workspace's account.
    public UserId? AccountOwnerId { get; private set; }

    // Its agent starts with the chat, before any message, until it tried.
    public bool StartsAgent { get; private set; }

    // The run of its nook's setup whose start the chat told, and that run's exit code once it told its
    // end. The agent starts after that.
    public ProcessId? SetupRunId { get; private set; }

    public int? SetupExitCode { get; private set; }

    // The message whose turn calls for testing the project's setup in a fresh nook afterwards, until
    // the test ended; the next turn waits for it.
    public MessageId? SetupTestAfter { get; private set; }

    // The agent's process, while it runs, and the SHA-256 of the token its model calls carry.
    public ProcessId? HarnessProcessId { get; private set; }

    public byte[]? HarnessTokenHash { get; private set; }

    // How far the agent's output is read: everything before this offset is handled.
    public long OutputOffset { get; private set; }

    // The agent's session, once it answered session/new or session/load, and whether it accepts
    // messages mid-turn.
    public string? SessionId { get; private set; }

    public bool SupportsSteering { get; private set; }

    // The session of an agent that ended, which the next one loads when its harness can; and
    // whether it is loading it, while the updates it replays are history already recorded.
    public string? ResumableSessionId { get; private set; }

    public bool LoadingSession { get; private set; }

    // The message whose turn ended, until the checkpoint after it is taken.
    public MessageId? CheckpointAfter { get; private set; }

    // The manifest of the harness state its nook held when it last synced with its starter's
    // (StateFiles); null before the first sync.
    public string? HarnessStateFiles { get; private set; }

    // The message whose turn is running.
    public MessageId? TurnMessageId { get; private set; }

    public long LastSequence { get; private set; }

    // PostgreSQL's xmin: a second writer conflicts instead of overwriting.
    public uint Version { get; private set; }

    public static Chat Start(NookId nookId, WorkspaceId workspaceId, UserId startedBy, string harness, AgentAccountCredential account, TimeProvider time)
    {
        return new Chat(ChatId.New(), nookId, workspaceId, startedBy, time.GetUtcNow(), harness, account.Id, account.OwnerId) { StartsAgent = true };
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

    // Its agent starts now, or couldn't; either way nothing waits for it to start any more.
    public void AgentStartTried()
    {
        StartsAgent = false;
    }

    // A run of its nook's setup; returns whether its start is news.
    public bool SetupRunSeen(ProcessId run)
    {
        if (SetupRunId == run)
        {
            return false;
        }

        SetupRunId = run;
        SetupExitCode = null;
        return true;
    }

    // Whether the agent still waits for the run it saw to end.
    public bool WaitsForSetup => SetupRunId is not null && SetupExitCode is null;

    public void SetupRunEnded(int exitCode)
    {
        SetupExitCode = exitCode;
    }

    public void HarnessStarted(ProcessId processId)
    {
        HarnessProcessId = processId;
        OutputOffset = 0;
        SessionId = null;
        SupportsSteering = false;
        LoadingSession = false;
    }

    // The agent's process is gone, with any turn it was working on; its session may come back.
    public void HarnessStopped()
    {
        HarnessLost();
        TurnMessageId = null;
    }

    // The agent's process was lost with its nook: the turn it was working on, if any, goes to the
    // next agent, and so may its session.
    public void HarnessLost()
    {
        HarnessProcessId = null;
        HarnessTokenHash = null;
        OutputOffset = 0;
        ResumableSessionId = SessionId ?? ResumableSessionId;
        SessionId = null;
        SupportsSteering = false;
        LoadingSession = false;
    }

    // Returns the earlier session the agent loads, or null when it starts a new one.
    public string? Initialized(bool supportsSteering, bool supportsLoading)
    {
        SupportsSteering = supportsSteering;
        LoadingSession = supportsLoading && ResumableSessionId is not null;
        return LoadingSession ? ResumableSessionId : null;
    }

    // The agent couldn't load the earlier session, so a new one starts.
    public void LoadFailed()
    {
        LoadingSession = false;
    }

    // A new session; returns whether it took over from an earlier agent's, without its conversation.
    public bool SessionReady(string sessionId)
    {
        bool tookOver = ResumableSessionId is not null;
        SessionId = sessionId;
        ResumableSessionId = null;
        return tookOver;
    }

    // The agent loaded the earlier session, and continues its conversation.
    public void SessionLoaded()
    {
        SessionId = ResumableSessionId;
        ResumableSessionId = null;
        LoadingSession = false;
    }

    public void Read(long offset)
    {
        OutputOffset = offset;
    }

    public void TurnStarted(MessageId messageId, bool testsSetup)
    {
        TurnMessageId = messageId;
        if (testsSetup)
        {
            SetupTestAfter = messageId;
        }
    }

    public void SetupTestEnded()
    {
        SetupTestAfter = null;
    }

    // The turn ended, so a checkpoint of the files it changed is due.
    public void TurnEnded()
    {
        CheckpointAfter = TurnMessageId;
        TurnMessageId = null;
    }

    public void CheckpointTaken()
    {
        CheckpointAfter = null;
    }

    public void HarnessStateSynced(string? manifest)
    {
        HarnessStateFiles = manifest;
    }

    public StoredEvent Record(ChatEventBody body, TimeProvider time)
    {
        LastSequence++;
        return StoredEvent.From(Id, LastSequence, time.GetUtcNow(), body);
    }

    public ChatSummary ToSummary(bool messagesWaiting)
    {
        return new ChatSummary(Id, NookId, WorkspaceId, StartedBy, StartedAt, TurnMessageId is not null || messagesWaiting, Harness, AgentAccountId, AccountOwnerId);
    }
}
