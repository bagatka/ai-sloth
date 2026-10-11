using System;
using System.Collections.Generic;
using Xunit;

namespace Bagatka.Harnesses.Tests;

public sealed class HarnessProfilesTests
{
    private static readonly Uri Gateway = new Uri("http://host.docker.internal:5172/models/");

    // Every harness starts with the same variables, its model and thinking level among them; its start
    // script translates them.
    [Fact]
    public void A_model_api_goes_through_a_gateway_and_a_harnesss_own_token_goes_alone()
    {
        IReadOnlyDictionary<string, string> openAI = HarnessProfiles.Codex.EnvironmentFor(CredentialKind.OpenAIApi, "chat-token", Gateway, "gpt-6.1-sol", "medium");
        IReadOnlyDictionary<string, string> openRouter = HarnessProfiles.Pi.EnvironmentFor(CredentialKind.OpenRouterApi, "chat-token", Gateway, "openai/gpt-6-luna", "xhigh");
        IReadOnlyDictionary<string, string> copilot = HarnessProfiles.Copilot.EnvironmentFor(CredentialKind.CopilotApi, "chat-token", Gateway, "gpt-6-luna", "high");
        IReadOnlyDictionary<string, string> claudePlan = HarnessProfiles.ClaudeCode.EnvironmentFor(CredentialKind.ClaudeOAuthToken, "sk-ant-oat-x", gateway: null, "claude-opus-5.5", "high");

        Assert.Equal("openai", openAI["HARNESS_CREDENTIAL"]);
        Assert.Equal("http://host.docker.internal:5172/models", openAI["HARNESS_MODEL_URL"]);
        Assert.Equal("chat-token", openAI["HARNESS_TOKEN"]);
        Assert.Equal(("gpt-6.1-sol", "medium"), (openAI["HARNESS_MODEL"], openAI["HARNESS_THINKING"]));
        Assert.Equal(("openrouter", "openai/gpt-6-luna", "xhigh"), (openRouter["HARNESS_CREDENTIAL"], openRouter["HARNESS_MODEL"], openRouter["HARNESS_THINKING"]));
        Assert.Equal(("copilot", "chat-token", "http://host.docker.internal:5172/models"), (copilot["HARNESS_CREDENTIAL"], copilot["HARNESS_TOKEN"], copilot["HARNESS_MODEL_URL"]));
        Assert.Equal(("claude-oauth-token", "sk-ant-oat-x", "claude-opus-5.5", "high"), (claudePlan["HARNESS_CREDENTIAL"], claudePlan["HARNESS_TOKEN"], claudePlan["HARNESS_MODEL"], claudePlan["HARNESS_THINKING"]));
        Assert.False(claudePlan.ContainsKey("HARNESS_MODEL_URL"));
    }

    // A key or plan of a model API never reaches a harness: without a gateway it isn't handed over.
    [Fact]
    public void A_model_api_without_a_gateway_or_a_token_with_one_is_refused()
    {
        Assert.Throws<ArgumentException>(() => HarnessProfiles.ClaudeCode.EnvironmentFor(CredentialKind.AnthropicApi, "sk-ant-key", gateway: null, "claude-opus-5.5", "high"));
        Assert.Throws<ArgumentException>(() => HarnessProfiles.Copilot.EnvironmentFor(CredentialKind.CopilotApi, "gho_x", gateway: null, "gpt-6-luna", "high"));
        Assert.Throws<ArgumentException>(() => HarnessProfiles.ClaudeCode.EnvironmentFor(CredentialKind.ClaudeOAuthToken, "sk-ant-oat-x", Gateway, "claude-opus-5.5", "high"));
    }

    // Each harness thinks at the levels its own settings name, its default among them; others are
    // refused before it starts.
    [Fact]
    public void A_harness_thinks_only_at_its_own_levels()
    {
        Assert.Throws<ArgumentException>(() => HarnessProfiles.Codex.EnvironmentFor(CredentialKind.OpenAIApi, "chat-token", Gateway, "gpt-6.1-sol", "max"));
        Assert.Throws<ArgumentException>(() => HarnessProfiles.Copilot.EnvironmentFor(CredentialKind.CopilotApi, "chat-token", Gateway, "gpt-6-luna", "off"));
        Assert.Equal("max", HarnessProfiles.ClaudeCode.EnvironmentFor(CredentialKind.AnthropicApi, "chat-token", Gateway, "claude-opus-5.5", "max")["HARNESS_THINKING"]);
        Assert.All(HarnessProfiles.All, harness => Assert.Contains(harness.DefaultThinking, harness.ThinkingLevels, StringComparer.Ordinal));
    }

    // A model takes the harness's levels its vendor names (none for pi's off), every level when it
    // takes a budget or isn't known, and none when it doesn't reason.
    [Fact]
    public void A_model_takes_the_thinking_levels_its_vendor_names()
    {
        Assert.Equal(["low", "medium", "high"], HarnessProfiles.Copilot.LevelsFor(reasons: true, ["low", "medium", "high"]));
        Assert.Equal(["off", "low", "high"], HarnessProfiles.Pi.LevelsFor(reasons: true, ["none", "low", "high"]));
        Assert.Equal(HarnessProfiles.ClaudeCode.ThinkingLevels, HarnessProfiles.ClaudeCode.LevelsFor(reasons: true, []));
        Assert.Equal(HarnessProfiles.Codex.ThinkingLevels, HarnessProfiles.Codex.LevelsFor(reasons: null, []));
        Assert.Empty(HarnessProfiles.Copilot.LevelsFor(reasons: false, []));
    }

    // A model's ID goes into start scripts' files as it is, so it can't carry anything to quote.
    [Theory]
    [InlineData("openai/gpt-6-luna", true)]
    [InlineData("~anthropic/claude-sonnet-latest", true)]
    [InlineData("claude-opus-5-5", true)]
    [InlineData("", false)]
    [InlineData("gpt\"},\"x\":\"", false)]
    [InlineData("a model", false)]
    public void A_models_id_is_only_what_vendors_write(string model, bool valid)
    {
        Assert.Equal(valid, HarnessProfile.IsModelId(model));
    }

    [Fact]
    public void Harnesses_accept_the_credentials_they_can_run_on()
    {
        Assert.True(HarnessProfiles.Pi.Accepts(CredentialKind.AnthropicApi));
        Assert.True(HarnessProfiles.Pi.Accepts(CredentialKind.CopilotApi));
        Assert.True(HarnessProfiles.ClaudeCode.Accepts(CredentialKind.OpenRouterApi));
        Assert.True(HarnessProfiles.Codex.Accepts(CredentialKind.OpenAIApi));
        Assert.False(HarnessProfiles.Codex.Accepts(CredentialKind.AnthropicApi));
        Assert.Equal(HarnessProfiles.Codex, HarnessProfiles.Find("codex"));
        Assert.Null(HarnessProfiles.Find("nowhere"));
    }
}
