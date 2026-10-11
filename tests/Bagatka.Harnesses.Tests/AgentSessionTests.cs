using System;
using System.Collections.Generic;
using System.Text.Json;
using Xunit;

namespace Bagatka.Harnesses.Tests;

// The lines are written as agents write them, request IDs included: hosts keep agent output and read it
// again after a restart, so these IDs must keep matching.
public sealed class AgentSessionTests
{
    private const string Work = "/work";

    private static readonly Guid Key = Guid.Parse("01a10812-bca7-7f56-8efa-be22ffa9681b");

    private static readonly AgentSession Ready = new AgentSession(SessionId: "session-1", CanSteer: true);

    [Fact]
    public void A_prompt_and_a_steer_carry_their_key_in_the_request_id()
    {
        Assert.Equal("prompt:01a10812-bca7-7f56-8efa-be22ffa9681b", IdOf(Ready.Prompt(Key, "Add a README")));
        Assert.Equal("steer:01a10812-bca7-7f56-8efa-be22ffa9681b", IdOf(Ready.Steer(Key, "Use British spelling")));
        Assert.Throws<InvalidOperationException>(() => new AgentSession().Prompt(Key, "Too early"));
    }

    [Fact]
    public void A_new_agent_starts_a_session_and_says_how_it_works()
    {
        AgentStep initialized = new AgentSession().Read("""{"jsonrpc":"2.0","id":"initialize","result":{"protocolVersion":1,"_meta":{"steering":{"supported":true}}}}""", Work);
        AgentStep created = initialized.Session.Read("""{"jsonrpc":"2.0","id":"session/new","result":{"sessionId":"session-2","configOptions":[{"id":"mode","category":"mode","type":"select","currentValue":"default"},{"id":"model","category":"model","type":"select","currentValue":"gpt-5-codex","options":[]}]}}""", Work);

        Assert.Equal("session/new", IdOf(Assert.Single(initialized.Lines)));
        Assert.Null(initialized.Event);
        Assert.Equal(new SessionReady("gpt-5-codex", Remembers: null), created.Event?.Value);
        Assert.Equal(new AgentSession("session-2", Earlier: null, CanSteer: true), created.Session);
    }

    // An agent that took over a stopped or lost one continues its session when it can load it, skipping
    // the history it replays, which its caller has; otherwise it starts a new one, without the memory.
    [Fact]
    public void A_new_agent_continues_the_earlier_session_when_it_can()
    {
        AgentSession taken = new AgentSession("session-1", CanSteer: true).Ended();
        AgentStep initialized = taken.Read("""{"jsonrpc":"2.0","id":"initialize","result":{"protocolVersion":1,"agentCapabilities":{"loadSession":true}}}""", Work);
        AgentStep history = initialized.Session.Read("""{"jsonrpc":"2.0","method":"session/update","params":{"sessionId":"session-1","update":{"sessionUpdate":"agent_message_chunk","content":{"type":"text","text":"Hi"}}}}""", Work);
        AgentStep loaded = history.Session.Read("""{"jsonrpc":"2.0","id":"session/load","result":{"configOptions":[{"id":"model","category":"model","type":"select","currentValue":"default","options":[]}]}}""", Work);
        AgentStep loadFailed = initialized.Session.Read("""{"jsonrpc":"2.0","id":"session/load","error":{"code":-32000,"message":"Session not found"}}""", Work);
        AgentStep created = loadFailed.Session.Read("""{"jsonrpc":"2.0","id":"session/new","result":{"sessionId":"session-3"}}""", Work);

        Assert.Equal(new AgentSession(SessionId: null, Earlier: "session-1"), taken);
        Assert.Equal("session/load", IdOf(Assert.Single(initialized.Lines)));
        Assert.Null(history.Event);
        Assert.Equal(new SessionReady("default", Remembers: true), loaded.Event?.Value);
        Assert.Equal("session-1", loaded.Session.SessionId);
        Assert.Equal("session/new", IdOf(Assert.Single(loadFailed.Lines)));
        Assert.Equal(new SessionReady(Model: null, Remembers: false), created.Event?.Value);
        Assert.Equal(new AgentSession("session-3"), created.Session);
    }

    [Fact]
    public void Answers_are_matched_to_the_prompts_and_steers_they_answer()
    {
        object? ended = EventOf("""{"jsonrpc":"2.0","id":"prompt:01a10812-bca7-7f56-8efa-be22ffa9681b","result":{"stopReason":"end_turn"}}""");
        object? failed = EventOf("""{"jsonrpc":"2.0","id":"prompt:01a10812-bca7-7f56-8efa-be22ffa9681b","error":{"code":-32603,"message":"Overloaded"}}""");
        object? joined = EventOf("""{"jsonrpc":"2.0","id":"steer:01a10812-bca7-7f56-8efa-be22ffa9681b","result":{"outcome":"injected"}}""");
        object? promptRequired = EventOf("""{"jsonrpc":"2.0","id":"steer:01a10812-bca7-7f56-8efa-be22ffa9681b","result":{"outcome":"promptRequired"}}""");
        object? startFailed = EventOf("""{"jsonrpc":"2.0","id":"session/new","error":{"code":-32000,"message":"Authentication required"}}""");

        Assert.Equal(new PromptAnswered(Key, "end_turn", Error: null), ended);
        Assert.Equal(new PromptAnswered(Key, "failed", "Overloaded"), failed);
        Assert.Equal(new SteerAnswered(Key, Joined: true), joined);
        Assert.Equal(new SteerAnswered(Key, Joined: false), promptRequired);
        Assert.Equal(new StartFailed("Authentication required"), startFailed);
    }

    [Fact]
    public void Progress_says_which_model_answered_when_the_agent_tells()
    {
        object? claude = EventOf("""{"jsonrpc":"2.0","method":"session/update","params":{"sessionId":"session-1","update":{"sessionUpdate":"usage_update","used":1200,"size":200000,"_meta":{"_claude/model":"claude-sonnet-5"}}}}""");
        object? other = EventOf("""{"jsonrpc":"2.0","method":"session/update","params":{"sessionId":"session-1","update":{"sessionUpdate":"usage_update","used":1200,"size":200000}}}""");

        Assert.Equal("claude-sonnet-5", Assert.IsType<AgentReported>(claude).Model);
        Assert.Null(Assert.IsType<AgentReported>(other).Model);
    }

    // Permission to run a tool is granted as broadly as offered; nothing else this client offers.
    [Fact]
    public void Requests_are_answered_without_the_caller()
    {
        AgentStep permission = Ready.Read("""{"jsonrpc":"2.0","id":7,"method":"session/request_permission","params":{"options":[{"optionId":"once","kind":"allow_once"},{"optionId":"always","kind":"allow_always"}]}}""", Work);
        AgentStep other = Ready.Read("""{"jsonrpc":"2.0","id":8,"method":"fs/read_text_file","params":{}}""", Work);

        Assert.Null(permission.Event);
        Assert.Contains("\"optionId\":\"always\"", Assert.Single(permission.Lines), StringComparison.Ordinal);
        Assert.Contains("-32601", Assert.Single(other.Lines), StringComparison.Ordinal);
    }

    [Fact]
    public void Noise_and_answers_to_unknown_requests_mean_nothing()
    {
        IReadOnlyList<AgentStep> read = [Ready.Read("Starting Claude Code...", Work), Ready.Read("""{"jsonrpc":"2.0","id":"something-else","result":{}}""", Work)];

        Assert.All(read, step => Assert.Equal(((AgentEvent?)null, 0, Ready), (step.Event, step.Lines.Count, step.Session)));
    }

    private static object? EventOf(string line)
    {
        return Ready.Read(line, Work).Event?.Value;
    }

    private static string? IdOf(string message)
    {
        using JsonDocument document = JsonDocument.Parse(message);
        return document.RootElement.GetProperty("id").GetString();
    }
}
