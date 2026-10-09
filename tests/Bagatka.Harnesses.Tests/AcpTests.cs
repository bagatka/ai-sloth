using System;
using System.Text.Json;
using Xunit;

namespace Bagatka.Harnesses.Tests;

public sealed class AcpTests
{
    private static readonly Guid Key = Guid.Parse("01a10812-bca7-7f56-8efa-be22ffa9681b");

    [Fact]
    public void A_prompt_and_a_steer_carry_their_key_in_the_request_id()
    {
        string prompt = Acp.Prompt(Key, "session-1", "Add a README");
        string steer = Acp.Steer(Key, "session-1", "Use British spelling");

        Assert.Equal("prompt:01a10812-bca7-7f56-8efa-be22ffa9681b", IdOf(prompt));
        Assert.Equal("steer:01a10812-bca7-7f56-8efa-be22ffa9681b", IdOf(steer));
    }

    // The lines are written as an agent wrote them, request IDs included: hosts keep agent output and
    // read it again after a restart, so these IDs must keep matching.
    [Fact]
    public void Answers_are_matched_to_the_requests_they_answer()
    {
        object? initialized = CaseOf("""{"jsonrpc":"2.0","id":"initialize","result":{"protocolVersion":1,"_meta":{"steering":{"supported":true}}}}""");
        object? created = CaseOf("""{"jsonrpc":"2.0","id":"session/new","result":{"sessionId":"session-1"}}""");
        object? createdWithModel = CaseOf("""{"jsonrpc":"2.0","id":"session/new","result":{"sessionId":"session-2","configOptions":[{"id":"mode","category":"mode","type":"select","currentValue":"default"},{"id":"model","category":"model","type":"select","currentValue":"gpt-5-codex","options":[]}]}}""");
        object? loaded = CaseOf("""{"jsonrpc":"2.0","id":"session/load","result":{"configOptions":[{"id":"model","category":"model","type":"select","currentValue":"default","options":[]}]}}""");
        object? ended = CaseOf("""{"jsonrpc":"2.0","id":"prompt:01a10812-bca7-7f56-8efa-be22ffa9681b","result":{"stopReason":"end_turn"}}""");
        object? injected = CaseOf("""{"jsonrpc":"2.0","id":"steer:01a10812-bca7-7f56-8efa-be22ffa9681b","result":{"outcome":"injected"}}""");
        object? promptRequired = CaseOf("""{"jsonrpc":"2.0","id":"steer:01a10812-bca7-7f56-8efa-be22ffa9681b","result":{"outcome":"promptRequired"}}""");

        Assert.Equal(new AcpInitialized(SupportsSteering: true, SupportsLoading: false), initialized);
        Assert.Equal(new AcpSessionCreated("session-1", Model: null), created);
        Assert.Equal(new AcpSessionCreated("session-2", "gpt-5-codex"), createdWithModel);
        Assert.Equal(new AcpSessionLoaded("default"), loaded);
        Assert.Equal(new AcpPromptEnded(Key, "end_turn"), ended);
        Assert.Equal(new AcpSteerAnswered(Key, Injected: true), injected);
        Assert.Equal(new AcpSteerAnswered(Key, Injected: false), promptRequired);
    }

    [Fact]
    public void Usage_says_which_model_answered_when_the_agent_tells()
    {
        object? claude = CaseOf("""{"jsonrpc":"2.0","method":"session/update","params":{"sessionId":"session-1","update":{"sessionUpdate":"usage_update","used":1200,"size":200000,"_meta":{"_claude/model":"claude-sonnet-5"}}}}""");
        object? other = CaseOf("""{"jsonrpc":"2.0","method":"session/update","params":{"sessionId":"session-1","update":{"sessionUpdate":"usage_update","used":1200,"size":200000}}}""");

        Assert.Equal("claude-sonnet-5", Assert.IsType<AcpUpdate>(claude).Model);
        Assert.Null(Assert.IsType<AcpUpdate>(other).Model);
    }

    [Fact]
    public void Errors_answer_the_request_that_failed()
    {
        object? startFailed = CaseOf("""{"jsonrpc":"2.0","id":"session/new","error":{"code":-32000,"message":"Authentication required"}}""");
        object? promptFailed = CaseOf("""{"jsonrpc":"2.0","id":"prompt:01a10812-bca7-7f56-8efa-be22ffa9681b","error":{"code":-32603,"message":"Overloaded"}}""");

        Assert.Equal(new AcpStartFailed("Authentication required"), startFailed);
        Assert.Equal(new AcpPromptFailed(Key, "Overloaded"), promptFailed);
    }

    [Fact]
    public void Noise_and_answers_to_unknown_requests_mean_nothing()
    {
        AcpEvent? noise = Acp.Read("Starting Claude Code...");
        AcpEvent? unknown = Acp.Read("""{"jsonrpc":"2.0","id":"something-else","result":{}}""");

        Assert.Null(noise);
        Assert.Null(unknown);
    }

    private static object? CaseOf(string line)
    {
        AcpEvent? read = Acp.Read(line);
        return read?.Value;
    }

    private static string? IdOf(string message)
    {
        using JsonDocument document = JsonDocument.Parse(message);
        return document.RootElement.GetProperty("id").GetString();
    }
}
