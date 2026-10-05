using System;
using System.Collections.Generic;
using Xunit;

namespace Bagatka.Harnesses.Tests;

public sealed class HarnessProfilesTests
{
    private static readonly Uri Gateway = new Uri("http://host.docker.internal:5172/models/");

    // Every harness starts with the same variables; its start script translates them.
    [Fact]
    public void A_model_api_goes_through_a_gateway_and_a_harnesss_own_token_goes_alone()
    {
        IReadOnlyDictionary<string, string> openAI = HarnessProfile.EnvironmentFor(CredentialKind.OpenAIApi, "chat-token", Gateway);
        IReadOnlyDictionary<string, string> copilot = HarnessProfile.EnvironmentFor(CredentialKind.GitHubToken, "github_pat_x", gateway: null);

        Assert.Equal("openai", openAI["HARNESS_CREDENTIAL"]);
        Assert.Equal("http://host.docker.internal:5172/models", openAI["HARNESS_MODEL_URL"]);
        Assert.Equal("chat-token", openAI["HARNESS_TOKEN"]);
        Assert.Equal("github-token", copilot["HARNESS_CREDENTIAL"]);
        Assert.Equal("github_pat_x", copilot["HARNESS_TOKEN"]);
        Assert.False(copilot.ContainsKey("HARNESS_MODEL_URL"));
    }

    // A key or plan of a model API never reaches a harness: without a gateway it isn't handed over.
    [Fact]
    public void A_model_api_without_a_gateway_or_a_token_with_one_is_refused()
    {
        Assert.Throws<ArgumentException>(() => HarnessProfile.EnvironmentFor(CredentialKind.AnthropicApi, "sk-ant-key", gateway: null));
        Assert.Throws<ArgumentException>(() => HarnessProfile.EnvironmentFor(CredentialKind.GitHubToken, "github_pat_x", Gateway));
    }

    [Fact]
    public void Harnesses_accept_the_credentials_they_can_run_on()
    {
        Assert.True(HarnessProfiles.Pi.Accepts(CredentialKind.AnthropicApi));
        Assert.True(HarnessProfiles.Codex.Accepts(CredentialKind.OpenAIApi));
        Assert.False(HarnessProfiles.Codex.Accepts(CredentialKind.AnthropicApi));
        Assert.Equal(HarnessProfiles.Codex, HarnessProfiles.Find("codex"));
        Assert.Null(HarnessProfiles.Find("nowhere"));
    }
}
