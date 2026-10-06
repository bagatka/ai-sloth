using System;
using System.Net;
using System.Net.Http;
using System.Threading.Tasks;
using Bagatka.AiSloth.AgentAccounts.Contracts;
using Bagatka.AiSloth.Chats.Contracts;
using Bagatka.AiSloth.Nooks.Contracts;
using Bagatka.AiSloth.Sources.Contracts;
using Bagatka.AiSloth.Workspaces.Contracts;
using Xunit;

namespace Bagatka.AiSloth.EndToEndTests;

/// <summary>
/// A person's new workspace with an agent account on the fake model, and what journeys do in it:
/// start chats, send messages, run commands in nooks, and wait for a nook to change.
/// </summary>
internal sealed class TestWorkspace
{
    private TestWorkspace(ControlPlane app, HttpClient person, WorkspaceId id, AgentAccountId account)
    {
        App = app;
        Person = person;
        Id = id;
        Account = account;
    }

    public ControlPlane App { get; }

    public HttpClient Person { get; }

    public WorkspaceId Id { get; }

    public AgentAccountId Account { get; }

    public string Path => Paths.Workspace(Id);

    /// <summary>A new workspace and its account: Anthropic's API for Claude Code, or OpenAI's for Codex and pi.</summary>
    public static async Task<TestWorkspace> CreateAsync(ControlPlane app, HttpClient person, bool openAI = false)
    {
        WorkspaceSummary workspace = await Api.ReadAsync<WorkspaceSummary>(person.SendPostAsync("/workspaces", new { name = "Acme" }), HttpStatusCode.Created);
        object account = openAI
            ? new { kind = "OpenAIApiKey", name = "Team key", secret = FakeModel.ApiKey, endpoint = app.Model.OpenAIUrl }
            : new { kind = "AnthropicApiKey", name = "Team key", secret = FakeModel.ApiKey, endpoint = app.Model.Url };
        AgentAccountSummary added = await Api.ReadAsync<AgentAccountSummary>(person.SendPostAsync(Paths.Workspace(workspace.Id) + "/agent-accounts", account), HttpStatusCode.Created);
        return new TestWorkspace(app, person, workspace.Id, added.Id);
    }

    /// <summary>A chat in a nook of its own: with the repository, or a copy of another chat's files, when given.</summary>
    public async Task<ChatSummary> StartChatAsync(
        string harness = "claude-code", AgentAccountId? account = null, ChatId? copyOf = null, RepositoryId? repository = null, string provider = "docker")
    {
        AgentAccountId paying = account ?? Account;
        object body = repository is RepositoryId chosen
            ? new { provider, harness, account = paying, copyOf, repositories = new[] { new { repository = chosen } } }
            : new { provider, harness, account = paying, copyOf };
        return await Api.ReadAsync<ChatSummary>(Person.SendPostAsync(Path + "/chats", body), HttpStatusCode.Created);
    }

    public async Task<ChatMessage> SendAsync(ChatSummary chat, string text)
    {
        return await Api.ReadAsync<ChatMessage>(Person.SendPostAsync(Paths.Chat(chat) + "/messages", new { text }), HttpStatusCode.OK);
    }

    /// <summary>Runs a shell script in the nook, as a person or agent would, and returns its exit code.</summary>
    public async Task<int?> RunAsync(NookId nook, string script)
    {
        return await NookProcesses.ExitCodeAsync(Person, nook, "sh", "-c", script);
    }

    /// <summary>Like <see cref="RunAsync"/>, for a script that must succeed.</summary>
    public async Task ChangeAsync(NookId nook, string script)
    {
        int? exitCode = await RunAsync(nook, script);
        Assert.Equal(0, exitCode);
    }

    public async Task<NookSummary> NookAsync(NookId nook)
    {
        return await Api.ReadAsync<NookSummary>(Person.SendGetAsync(Paths.Nook(nook)), HttpStatusCode.OK);
    }

    /// <summary>The nook's status once it is one the journey waits for, or as it is when patience ran out.</summary>
    public async Task<NookStatus> StatusAsync(NookId nook, Func<NookStatus, bool> awaited, TimeSpan patience)
    {
        long started = TimeProvider.System.GetTimestamp();
        NookSummary current = await NookAsync(nook);
        while (!awaited(current.Status) && TimeProvider.System.GetElapsedTime(started) < patience)
        {
            await Task.Delay(TimeSpan.FromMilliseconds(500), TestContext.Current.CancellationToken);
            current = await NookAsync(nook);
        }

        return current.Status;
    }
}
