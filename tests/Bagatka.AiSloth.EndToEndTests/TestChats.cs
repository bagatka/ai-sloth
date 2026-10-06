using System;
using System.Globalization;
using System.Net;
using System.Net.Http;
using System.Threading.Tasks;
using Bagatka.AiSloth.AgentAccounts.Contracts;
using Bagatka.AiSloth.Chats.Contracts;
using Bagatka.AiSloth.Nooks.Contracts;
using Bagatka.AiSloth.Workspaces.Contracts;
using Xunit;

namespace Bagatka.AiSloth.EndToEndTests;

/// <summary>
/// A new person's chats on one of an app's providers, and what tests of nooks' lives ask of them:
/// starting them, writing in them, and waiting for their nook's status.
/// </summary>
internal sealed class TestChats(ControlPlane app, string provider) : IDisposable
{
    public HttpClient Person { get; } = app.ClientFor("alice-" + Guid.CreateVersion7());

    public FakeModel Model => app.Model;

    /// <summary>A chat in a new workspace whose Anthropic account carries the fake model's key.</summary>
    public async Task<ChatSummary> StartChatAsync()
    {
        WorkspaceSummary workspace = await Api.ReadAsync<WorkspaceSummary>(Person.SendPostAsync("/workspaces", new { name = "Acme" }), HttpStatusCode.Created);
        string path = string.Create(CultureInfo.InvariantCulture, $"/workspaces/{workspace.Id.Value}");
        AgentAccountSummary account = await Api.ReadAsync<AgentAccountSummary>(
            Person.SendPostAsync(path + "/agent-accounts", new { kind = "AnthropicApiKey", name = "Team key", secret = FakeModel.ApiKey, endpoint = app.Model.Url }), HttpStatusCode.Created);
        return await Api.ReadAsync<ChatSummary>(
            Person.SendPostAsync(path + "/chats", new { provider, harness = "claude-code", account = account.Id }), HttpStatusCode.Created);
    }

    public async Task SendAsync(ChatSummary chat, string text)
    {
        await Api.ReadAsync<ChatMessage>(Person.SendPostAsync(string.Create(CultureInfo.InvariantCulture, $"/chats/{chat.Id.Value}/messages"), new { text }), HttpStatusCode.OK);
    }

    /// <summary>The nook's status once it is one the test waits for, or as it is when patience ran out.</summary>
    public async Task<NookStatus> StatusAsync(ChatSummary chat, Func<NookStatus, bool> awaited, TimeSpan patience)
    {
        long started = TimeProvider.System.GetTimestamp();
        NookSummary nook = await NookAsync(chat);
        while (!awaited(nook.Status) && TimeProvider.System.GetElapsedTime(started) < patience)
        {
            await Task.Delay(TimeSpan.FromMilliseconds(500), TestContext.Current.CancellationToken);
            nook = await NookAsync(chat);
        }

        return nook.Status;
    }

    public async Task<NookSummary> NookAsync(ChatSummary chat)
    {
        return await Api.ReadAsync<NookSummary>(Person.SendGetAsync(string.Create(CultureInfo.InvariantCulture, $"/nooks/{chat.NookId.Value}")), HttpStatusCode.OK);
    }

    public void Dispose()
    {
        Person.Dispose();
    }
}
