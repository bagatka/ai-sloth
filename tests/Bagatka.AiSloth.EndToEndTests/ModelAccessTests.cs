using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Net;
using System.Net.Http;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using Bagatka.AiSloth.AgentAccounts.Contracts;
using Bagatka.AiSloth.Chats.Contracts;
using Bagatka.AiSloth.Workspaces.Contracts;
using Microsoft.AspNetCore.WebUtilities;
using Xunit;

namespace Bagatka.AiSloth.EndToEndTests;

/// <summary>
/// Agents reach their models through the model gateway on every kind of account that pays for one:
/// Codex and pi, the real harnesses in real nooks, on an OpenAI key, and on a ChatGPT plan whose
/// tokens the control plane renews. Only the model and ChatGPT's sign-in are fake.
/// </summary>
public sealed class ModelAccessTests(ControlPlane controlPlane) : IDisposable
{
    private readonly HttpClient _alice = controlPlane.ClientFor("alice-" + Guid.CreateVersion7());

    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    [Theory]
    [InlineData("codex")]
    [InlineData("pi")]
    public async Task An_agent_answers_through_the_gateway_on_an_openai_key(string harness)
    {
        WorkspaceSummary workspace = await CreateWorkspaceAsync();
        AgentAccountSummary key = await Api.ReadAsync<AgentAccountSummary>(
            _alice.SendPostAsync(AccountsPath(workspace), new { kind = "OpenAIApiKey", name = "Team key", secret = FakeModel.ApiKey, endpoint = controlPlane.Model.OpenAIUrl }),
            HttpStatusCode.Created);
        ChatSummary chat = await StartChatAsync(workspace, harness, key);
        await using ChatWatch watch = await ChatWatch.OpenAsync(_alice, chat);

        await SendAsync(chat, "say hello");
        JsonElement ended = await watch.NextAsync("turn-ended");

        Assert.Equal("end_turn", ended.GetProperty("stopReason").GetString());
        Assert.Contains(watch.Seen, seen => string.Equals(seen.Type, "agent-update", StringComparison.Ordinal));
        Assert.Contains(controlPlane.Model.OpenAIAuthorizations, authorization => string.Equals(authorization, "Bearer " + FakeModel.ApiKey, StringComparison.Ordinal));
    }

    // Every token set the fake ChatGPT issues asks to be renewed at once, so each model call renews:
    // two agents working at the same time on one plan must never use a refresh token twice.
    [Fact]
    public async Task A_chatgpt_plan_pays_for_agents_and_is_renewed_one_call_at_a_time()
    {
        WorkspaceSummary workspace = await CreateWorkspaceAsync();
        (AgentAccountSummary plan, _) = await SignInWithChatGptAsync();
        ChatSummary first = await StartChatAsync(workspace, "codex", plan);
        ChatSummary second = await StartChatAsync(workspace, "codex", plan);
        await using ChatWatch firstWatch = await ChatWatch.OpenAsync(_alice, first);
        await using ChatWatch secondWatch = await ChatWatch.OpenAsync(_alice, second);
        int renewalsBefore = controlPlane.ChatGpt.Renewals;

        await Task.WhenAll(SendAsync(first, "say hello"), SendAsync(second, "say hello"));
        JsonElement firstEnded = await firstWatch.NextAsync("turn-ended");
        JsonElement secondEnded = await secondWatch.NextAsync("turn-ended");
        string[] planCalls = [.. controlPlane.Model.OpenAIAuthorizations
            .Select(authorization => authorization?["Bearer ".Length..] ?? string.Empty)
            .Where(controlPlane.ChatGpt.Issued)];

        Assert.Equal("end_turn", firstEnded.GetProperty("stopReason").GetString());
        Assert.Equal("end_turn", secondEnded.GetProperty("stopReason").GetString());
        Assert.True(planCalls.Length >= 2);
        Assert.True(controlPlane.ChatGpt.Renewals > renewalsBefore);
        Assert.Equal(0, controlPlane.ChatGpt.ReusedRefreshTokens);
    }

    [Fact]
    public async Task A_plan_whose_sign_in_ended_runs_no_agent_and_says_why()
    {
        WorkspaceSummary workspace = await CreateWorkspaceAsync();
        (AgentAccountSummary plan, string clientId) = await SignInWithChatGptAsync();
        ChatSummary chat = await StartChatAsync(workspace, "codex", plan);
        await using ChatWatch watch = await ChatWatch.OpenAsync(_alice, chat);
        controlPlane.ChatGpt.Disconnect(clientId);

        await SendAsync(chat, "say hello");
        JsonElement ended = await watch.NextAsync("turn-ended");
        IReadOnlyList<AgentAccountSummary> accounts = await Api.ReadAsync<IReadOnlyList<AgentAccountSummary>>(_alice.SendGetAsync(AccountsPath(workspace)), HttpStatusCode.OK);

        Assert.Equal("failed", ended.GetProperty("stopReason").GetString());
        Assert.Equal("The agent couldn't start: " + AgentAccountsErrors.SignInEnded.Message, ended.GetProperty("failure").GetString());
        Assert.True(accounts.Single(found => found.Id == plan.Id).NeedsSignIn);
    }

    public void Dispose()
    {
        _alice.Dispose();
    }

    private static string AccountsPath(WorkspaceSummary workspace)
    {
        return string.Create(CultureInfo.InvariantCulture, $"/workspaces/{workspace.Id.Value}/agent-accounts");
    }

    private async Task<WorkspaceSummary> CreateWorkspaceAsync()
    {
        return await Api.ReadAsync<WorkspaceSummary>(_alice.SendPostAsync("/workspaces", new { name = "Acme" }), HttpStatusCode.Created);
    }

    private async Task<ChatSummary> StartChatAsync(WorkspaceSummary workspace, string harness, AgentAccountSummary account)
    {
        string chats = string.Create(CultureInfo.InvariantCulture, $"/workspaces/{workspace.Id.Value}/chats");
        return await Api.ReadAsync<ChatSummary>(_alice.SendPostAsync(chats, new { provider = "docker", harness, account = account.Id }), HttpStatusCode.Created);
    }

    private async Task SendAsync(ChatSummary chat, string text)
    {
        string messages = string.Create(CultureInfo.InvariantCulture, $"/chats/{chat.Id.Value}/messages");
        await Api.ExpectAsync(_alice.SendPostAsync(messages, new { text }), HttpStatusCode.OK);
    }

    // Alice's own ChatGPT plan, and the client ID its registration got.
    private async Task<(AgentAccountSummary Plan, string ClientId)> SignInWithChatGptAsync()
    {
        SignInStarted started = await AgentAccountsTests.StartSignInAsync(_alice);
        Uri returnedTo = await FakeChatGpt.FollowAsync(started.Url);
        AgentAccountSummary plan = await Api.ReadAsync<AgentAccountSummary>(
            _alice.SendPostAsync(AgentAccountsTests.CompletePath(started), new { returnedTo }), HttpStatusCode.Created);
        string clientId = QueryHelpers.ParseQuery(returnedTo.Query)["client_id"].ToString();
        return (plan, clientId);
    }
}
