using System;
using Bagatka.AiSloth.AgentAccounts.Contracts;
using Bagatka.AiSloth.Chats.Contracts;
using Bagatka.AiSloth.Nooks.Contracts;
using Bagatka.AiSloth.Workspaces.Contracts;

namespace Bagatka.AiSloth.Chats.Model;

// One call a chat's agent made to its model through the gateway, as its answer reported it: its tokens,
// what it cost at the model's price then (or the vendor's own figure), and until when the prompt's
// cache it used stays. Kept for the chat's usage, and for costs by nook, workspace, and day.
internal sealed class ModelCall
{
    // An hour: Anthropic's longer cache.
    private static readonly TimeSpan LongCache = TimeSpan.FromHours(1);

    // Used by Record and by EF: parameter names match property names.
    private ModelCall(Guid id, ChatId chatId, WorkspaceId workspaceId, NookId nookId, AgentAccountId agentAccountId, MessageId? turn, DateTimeOffset at, string model,
        int input, int cacheRead, int cacheWrite, int cacheWriteLong, int output, int reasoning, decimal? cost, bool estimate, DateTimeOffset? cacheWarmUntil)
    {
        Id = id;
        ChatId = chatId;
        WorkspaceId = workspaceId;
        NookId = nookId;
        AgentAccountId = agentAccountId;
        Turn = turn;
        At = at;
        Model = model;
        Input = input;
        CacheRead = cacheRead;
        CacheWrite = cacheWrite;
        CacheWriteLong = cacheWriteLong;
        Output = output;
        Reasoning = reasoning;
        Cost = cost;
        Estimate = estimate;
        CacheWarmUntil = cacheWarmUntil;
    }

    public const int MaxModelLength = 200;

    public Guid Id { get; private set; }

    public ChatId ChatId { get; private set; }

    public WorkspaceId WorkspaceId { get; private set; }

    public NookId NookId { get; private set; }

    public AgentAccountId AgentAccountId { get; private set; }

    // The message whose turn the call was in; none between turns.
    public MessageId? Turn { get; private set; }

    public DateTimeOffset At { get; private set; }

    public string Model { get; private set; }

    public int Input { get; private set; }

    public int CacheRead { get; private set; }

    public int CacheWrite { get; private set; }

    public int CacheWriteLong { get; private set; }

    public int Output { get; private set; }

    public int Reasoning { get; private set; }

    public decimal? Cost { get; private set; }

    public bool Estimate { get; private set; }

    public DateTimeOffset? CacheWarmUntil { get; private set; }

    // Every input token, cached or not: what the model read.
    public int WholeInput => Input + CacheRead + CacheWrite + CacheWriteLong;

    // The call, from when it started, priced as its account pays for the model: the vendor's own
    // figure first, then the model's price. Its cache stays for the vendor's lifetime after the answer
    // of a call that read or wrote it, an hour after one that wrote the longer one.
    public static ModelCall Record(Chat chat, MessageId? turn, DateTimeOffset startedAt, ModelCallUsage usage, ModelFacts facts, TimeProvider time)
    {
        DateTimeOffset answered = time.GetUtcNow();
        ModelPrice? price = facts.Price;
        decimal? cost = usage.Cost ?? price?.CostOf(usage.Input, usage.CacheRead, usage.CacheWrite, usage.CacheWriteLong, usage.Output);
        bool estimate = usage.Cost is null && price?.Estimate == true;
        bool cached = usage.CacheRead + usage.CacheWrite + usage.CacheWriteLong > 0;
        TimeSpan? lifetime = usage.CacheWriteLong > 0 ? LongCache : facts.CacheLifetime;
        DateTimeOffset? warmUntil = cached && lifetime is TimeSpan kept ? answered + kept : null;
        string model = usage.Model.Length <= MaxModelLength ? usage.Model : usage.Model[..MaxModelLength];
        return new ModelCall(Guid.CreateVersion7(startedAt), chat.Id, chat.WorkspaceId, chat.NookId, chat.AgentAccountId, turn, startedAt, model,
            usage.Input, usage.CacheRead, usage.CacheWrite, usage.CacheWriteLong, usage.Output, usage.Reasoning, cost, estimate, warmUntil);
    }
}
