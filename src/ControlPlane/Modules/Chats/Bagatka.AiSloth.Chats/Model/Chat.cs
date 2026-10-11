using System;
using System.Linq;
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
    /// <summary>The longest title, in characters; titles come from the start of the first message.</summary>
    public const int TitleLength = 80;

    public const int MaxHarnessLength = 32;

    // Thinking levels are a harness's own words, such as xhigh.
    public const int MaxThinkingLength = 16;

    // Used by Start and by EF: parameter names match property names.
    private Chat(ChatId id, NookId nookId, WorkspaceId workspaceId, UserId startedBy, DateTimeOffset startedAt, string harness, string? model, string? thinking, AgentAccountId agentAccountId, UserId? accountOwnerId)
    {
        Id = id;
        NookId = nookId;
        WorkspaceId = workspaceId;
        StartedBy = startedBy;
        StartedAt = startedAt;
        Harness = harness;
        Model = model;
        Thinking = thinking;
        AgentAccountId = agentAccountId;
        AccountOwnerId = accountOwnerId;
    }

    public ChatId Id { get; private set; }

    public NookId NookId { get; private set; }

    public WorkspaceId WorkspaceId { get; private set; }

    public UserId StartedBy { get; private set; }

    public DateTimeOffset StartedAt { get; private set; }

    public string Harness { get; private set; }

    // The model its agent runs on, as the account's vendor names it; null for the harness's default.
    public string? Model { get; private set; }

    // How hard its agent thinks, one of its harness's levels; null for the harness's default.
    public string? Thinking { get; private set; }

    // Another account, model, and thinking level someone chose while an agent ran, which the next agent
    // runs on: the running agent's model calls stay on its own account until it stops, between turns.
    public bool AgentChangeWaiting { get; private set; }

    public AgentAccountId? NextAgentAccountId { get; private set; }

    public string? NextModel { get; private set; }

    public string? NextThinking { get; private set; }

    public AgentAccountId AgentAccountId { get; private set; }

    // The personal account's owner; null for the workspace's account.
    public UserId? AccountOwnerId { get; private set; }

    // Its agent starts with the chat, before any message, until it tried.
    public bool StartsAgent { get; private set; }

    // The run of its nook's setup whose start the chat told, and that run's exit code once it told its
    // end. The agent starts after that.
    public ProcessId? SetupRunId { get; private set; }

    public int? SetupExitCode { get; private set; }


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



    // The message whose turn is running.
    public MessageId? TurnMessageId { get; private set; }

    public long LastSequence { get; private set; }

    // PostgreSQL's xmin: a second writer conflicts instead of overwriting.
    public uint Version { get; private set; }

    public static Chat Start(NookId nookId, WorkspaceId workspaceId, UserId startedBy, string harness, string? model, string? thinking, AgentAccountCredential account, TimeProvider time)
    {
        return new Chat(ChatId.New(), nookId, workspaceId, startedBy, time.GetUtcNow(), harness, model, thinking, account.Id, account.OwnerId) { StartsAgent = true };
    }

    public static byte[] HashToken(string token)
    {
        return SHA256.HashData(Encoding.UTF8.GetBytes(token));
    }

    // The account, model, and thinking level from the next turn on: at once without a running agent,
    // otherwise once it stops. Returns whether that changes anything.
    public bool ChangeAgent(AgentAccountId account, string? model, string? thinking)
    {
        bool sameAsUpcoming = AgentChangeWaiting
            ? Same(NextAgentAccountId!.Value, NextModel, NextThinking, account, model, thinking)
            : Same(AgentAccountId, Model, Thinking, account, model, thinking);
        if (sameAsUpcoming)
        {
            return false;
        }

        bool running = HarnessProcessId is not null;
        AgentChangeWaiting = running && !Same(AgentAccountId, Model, Thinking, account, model, thinking);
        NextAgentAccountId = AgentChangeWaiting ? account : null;
        NextModel = AgentChangeWaiting ? model : null;
        NextThinking = AgentChangeWaiting ? thinking : null;
        if (!running)
        {
            AgentAccountId = account;
            Model = model;
            Thinking = thinking;
        }

        return true;
    }

    // The agent stopped: the next one runs on the account and model chosen meanwhile, and starts now.
    public void ApplyAgentChange()
    {
        if (!AgentChangeWaiting)
        {
            return;
        }

        AgentAccountId = NextAgentAccountId!.Value;
        Model = NextModel;
        Thinking = NextThinking;
        AgentChangeWaiting = false;
        NextAgentAccountId = null;
        NextModel = null;
        NextThinking = null;
        StartsAgent = true;
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

    /// <param name="messagesWaiting">Whether messages wait for the agent.</param>
    /// <param name="firstMessage">The start of its first message, if anyone wrote in it.</param>
    public ChatSummary ToSummary(bool messagesWaiting, string? firstMessage)
    {
        return new ChatSummary(Id, NookId, WorkspaceId, StartedBy, StartedAt, TurnMessageId is not null || messagesWaiting, Harness, AgentChangeWaiting ? NextModel : Model, AgentChangeWaiting ? NextThinking : Thinking, AgentChangeWaiting ? NextAgentAccountId!.Value : AgentAccountId, AccountOwnerId, TitleFrom(firstMessage));
    }

    private static bool Same(AgentAccountId account, string? model, string? thinking, AgentAccountId otherAccount, string? otherModel, string? otherThinking)
    {
        return account == otherAccount && string.Equals(model, otherModel, StringComparison.Ordinal) && string.Equals(thinking, otherThinking, StringComparison.Ordinal);
    }

    // A title is the first line with words in it, its spaces collapsed, cut at a word near 80 characters.
    private static string? TitleFrom(string? firstMessage)
    {
        string? line = firstMessage?.Split('\n').Select(text => string.Join(' ', text.Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries))).FirstOrDefault(text => text.Length > 0);
        if (line is null || line.Length <= TitleLength)
        {
            return line;
        }

        int cut = line.AsSpan(0, TitleLength).LastIndexOf(' ');
        return line[..(cut > TitleLength / 2 ? cut : TitleLength - 1)].TrimEnd() + "…";
    }
}
