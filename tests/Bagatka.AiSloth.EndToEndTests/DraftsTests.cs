using System;
using System.Globalization;
using System.Net;
using System.Net.Http;
using System.Threading.Tasks;
using Bagatka.AiSloth.AgentAccounts.Contracts;
using Bagatka.AiSloth.Chats.Contracts;
using Bagatka.AiSloth.Workspaces.Contracts;
using Bagatka.Foundation;
using Xunit;

namespace Bagatka.AiSloth.EndToEndTests;

/// <summary>
/// Drafts: apps start a chat as someone starts writing its first message, so its nook is ready when
/// they send it. A chat nobody wrote in yet isn't listed, and a person keeps at most two.
/// </summary>
public sealed class DraftsTests(ControlPlane controlPlane) : IDisposable
{
    private readonly HttpClient _alice = controlPlane.ClientFor("alice-" + Guid.CreateVersion7());

    [Fact]
    public async Task A_chat_nobody_wrote_in_is_listed_from_its_first_message()
    {
        (WorkspaceId workspace, AgentAccountId account) = await WorkspaceAsync();
        ChatSummary chat = await StartChatAsync(workspace, account);

        Page<ChatSummary> before = await ListAsync(workspace);
        await Api.ReadAsync<ChatMessage>(_alice.SendPostAsync(string.Create(CultureInfo.InvariantCulture, $"/chats/{chat.Id.Value}/messages"), new { text = "say hello" }), HttpStatusCode.OK);
        Page<ChatSummary> after = await ListAsync(workspace);

        Assert.DoesNotContain(before.Items, listed => listed.Id == chat.Id);
        Assert.Contains(after.Items, listed => listed.Id == chat.Id);
    }

    [Fact]
    public async Task Starting_a_third_draft_drops_the_oldest_with_its_nook()
    {
        (WorkspaceId workspace, AgentAccountId account) = await WorkspaceAsync();
        ChatSummary oldest = await StartChatAsync(workspace, account);
        ChatSummary second = await StartChatAsync(workspace, account);

        ChatSummary third = await StartChatAsync(workspace, account);

        bool nookGoes = await Api.GoneOrDeletingAsync(_alice, oldest.NookId);

        await Api.ExpectAsync(_alice.SendGetAsync(string.Create(CultureInfo.InvariantCulture, $"/chats/{oldest.Id.Value}")), HttpStatusCode.NotFound);
        Assert.True(nookGoes);
        await Api.ReadAsync<ChatSummary>(_alice.SendGetAsync(string.Create(CultureInfo.InvariantCulture, $"/chats/{second.Id.Value}")), HttpStatusCode.OK);
        await Api.ReadAsync<ChatSummary>(_alice.SendGetAsync(string.Create(CultureInfo.InvariantCulture, $"/chats/{third.Id.Value}")), HttpStatusCode.OK);
    }

    public void Dispose()
    {
        _alice.Dispose();
    }

    // A new workspace, and its Anthropic account with the fake model's key.
    private async Task<(WorkspaceId, AgentAccountId)> WorkspaceAsync()
    {
        WorkspaceSummary workspace = await Api.ReadAsync<WorkspaceSummary>(_alice.SendPostAsync("/workspaces", new { name = "Acme" }), HttpStatusCode.Created);
        AgentAccountSummary account = await Api.ReadAsync<AgentAccountSummary>(
            _alice.SendPostAsync(string.Create(CultureInfo.InvariantCulture, $"/workspaces/{workspace.Id.Value}/agent-accounts"), new { kind = "AnthropicApiKey", name = "Team key", secret = FakeModel.ApiKey, endpoint = controlPlane.Model.Url }),
            HttpStatusCode.Created);
        return (workspace.Id, account.Id);
    }

    private async Task<ChatSummary> StartChatAsync(WorkspaceId workspace, AgentAccountId account)
    {
        return await Api.ReadAsync<ChatSummary>(
            _alice.SendPostAsync(string.Create(CultureInfo.InvariantCulture, $"/workspaces/{workspace.Value}/chats"), new { provider = "docker", harness = "claude-code", account }), HttpStatusCode.Created);
    }

    private async Task<Page<ChatSummary>> ListAsync(WorkspaceId workspace)
    {
        return await Api.ReadAsync<Page<ChatSummary>>(_alice.SendGetAsync(string.Create(CultureInfo.InvariantCulture, $"/workspaces/{workspace.Value}/chats")), HttpStatusCode.OK);
    }
}
