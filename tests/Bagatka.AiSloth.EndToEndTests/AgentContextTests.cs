using System;
using System.Globalization;
using System.Linq;
using System.Net;
using System.Net.Http;
using System.Threading.Tasks;
using Bagatka.AiSloth.AgentAccounts.Contracts;
using Bagatka.AiSloth.Chats.Contracts;
using Bagatka.AiSloth.Workspaces.Contracts;
using Xunit;

namespace Bagatka.AiSloth.EndToEndTests;

/// <summary>
/// What agents get from AiSloth besides people's messages: the instructions a workspace and a person
/// give every agent, whatever its harness, and the harness state a person's agents keep for later.
/// The fake model sees what the real harnesses send it.
/// </summary>
public sealed class AgentContextTests(ControlPlane controlPlane) : IDisposable
{
    private readonly HttpClient _alice = controlPlane.ClientFor("alice-" + Guid.CreateVersion7());

    [Theory]
    [InlineData("claude-code")]
    [InlineData("codex")]
    [InlineData("pi")]
    public async Task Every_harness_follows_the_workspaces_and_its_persons_instructions(string harness)
    {
        string workspaceRule = "Use tabs, team rule " + Guid.CreateVersion7().ToString("N", CultureInfo.InvariantCulture);
        string personalRule = "Answer briefly, my rule " + Guid.CreateVersion7().ToString("N", CultureInfo.InvariantCulture);
        (WorkspaceId workspace, AgentAccountId account) = await WorkspaceAsync(openAI: harness is not "claude-code");
        await Api.ExpectAsync(_alice.SendPutAsync(PathOf(workspace) + "/instructions", new { text = workspaceRule }), HttpStatusCode.NoContent);
        await Api.ExpectAsync(_alice.SendPutAsync("/instructions", new { text = personalRule }), HttpStatusCode.NoContent);

        Instructions instructions = await Api.ReadAsync<Instructions>(_alice.SendGetAsync(PathOf(workspace) + "/instructions"), HttpStatusCode.OK);
        ChatSummary chat = await StartChatAsync(workspace, account, harness);
        await TurnAsync(chat, "say hello");

        Assert.Equal(new Instructions(workspaceRule, personalRule), instructions);
        Assert.Contains(controlPlane.Model.Requests, body => body.Contains(workspaceRule, StringComparison.Ordinal) && body.Contains(personalRule, StringComparison.Ordinal));
    }

    [Fact]
    public async Task Instructions_are_at_most_ten_thousand_characters()
    {
        (WorkspaceId workspace, _) = await WorkspaceAsync(openAI: false);

        Problem tooLong = await Api.ProblemAsync(_alice.SendPutAsync(PathOf(workspace) + "/instructions", new { text = new string('x', 10_001) }), HttpStatusCode.BadRequest);

        Assert.Contains("text", tooLong.Errors?.Keys ?? [], StringComparer.Ordinal);
    }

    [Fact]
    public async Task Claude_codes_memory_follows_its_person_into_their_next_chats_in_the_workspace_and_no_further()
    {
        string remembered = "Prefers tabs, remembered " + Guid.CreateVersion7().ToString("N", CultureInfo.InvariantCulture);
        (WorkspaceId workspace, AgentAccountId account) = await WorkspaceAsync(openAI: false);
        ChatSummary first = await StartChatAsync(workspace, account, "claude-code");
        await NookProcesses.ExitCodeAsync(_alice, first.NookId, "sh", "-c", "mkdir -p /root/.claude/memory && printf -- '- %s\\n' '" + remembered + "' > /root/.claude/memory/MEMORY.md");
        await TurnAsync(first, "say hello");
        HarnessStateSummary[] saved = await Api.ReadAsync<HarnessStateSummary[]>(_alice.SendGetAsync(PathOf(workspace) + "/harness-state"), HttpStatusCode.OK);
        int toldBefore = controlPlane.Model.Requests.Count(body => body.Contains(remembered, StringComparison.Ordinal));

        ChatSummary second = await StartChatAsync(workspace, account, "claude-code");
        await TurnAsync(second, "say hello");
        int toldAfter = controlPlane.Model.Requests.Count(body => body.Contains(remembered, StringComparison.Ordinal));
        (WorkspaceId elsewhere, AgentAccountId otherAccount) = await WorkspaceAsync(openAI: false);
        ChatSummary third = await StartChatAsync(elsewhere, otherAccount, "claude-code");
        await TurnAsync(third, "say hello");
        int? keptHere = await NookProcesses.ExitCodeAsync(_alice, second.NookId, "test", "-e", "/root/.claude/memory/MEMORY.md");
        int? keptElsewhere = await NookProcesses.ExitCodeAsync(_alice, third.NookId, "test", "-e", "/root/.claude/memory/MEMORY.md");
        await Api.ExpectAsync(_alice.DeleteAsync(new Uri(PathOf(workspace) + "/harness-state/claude-code", UriKind.Relative), TestContext.Current.CancellationToken), HttpStatusCode.NoContent);
        HarnessStateSummary[] forgotten = await Api.ReadAsync<HarnessStateSummary[]>(_alice.SendGetAsync(PathOf(workspace) + "/harness-state"), HttpStatusCode.OK);

        HarnessStateSummary state = Assert.Single(saved);
        Assert.Equal("claude-code", state.Harness);
        Assert.Equal(first.Id, state.SavedFrom);
        Assert.True(toldAfter > toldBefore, "The second chat's agent never told its model what it remembered.");
        Assert.Equal(0, keptHere);
        Assert.Equal(1, keptElsewhere);
        Assert.Empty(forgotten);
    }

    [Fact]
    public async Task Parallel_chats_keep_what_each_learns_and_get_the_others_at_their_next_turn()
    {
        (WorkspaceId workspace, AgentAccountId account) = await WorkspaceAsync(openAI: false);
        ChatSummary first = await StartChatAsync(workspace, account, "claude-code");
        ChatSummary second = await StartChatAsync(workspace, account, "claude-code");
        await using ChatWatch firstWatch = await ChatWatch.OpenAsync(_alice, first);
        await using ChatWatch secondWatch = await ChatWatch.OpenAsync(_alice, second);
        await TurnAsync(firstWatch, first, "say hello");
        await TurnAsync(secondWatch, second, "say hello");

        // Each agent learns something while the other works.
        await LearnAsync(first, "tabs", "Prefers tabs");
        await LearnAsync(second, "tests", "Runs tests first");
        await TurnAsync(firstWatch, first, "say hello");
        await TurnAsync(secondWatch, second, "say hello");
        await TurnAsync(firstWatch, first, "say hello");
        int? firstHasBoth = await MemoryHasAsync(first, "Prefers tabs", "Runs tests first");
        int? secondHasBoth = await MemoryHasAsync(second, "Prefers tabs", "Runs tests first");

        // One forgets something, and the other loses it at its next turn.
        await NookProcesses.ExitCodeAsync(_alice, first.NookId, "sh", "-c", "rm /root/.claude/memory/tabs.md && grep -v tabs /root/.claude/memory/MEMORY.md > /tmp/m && mv /tmp/m /root/.claude/memory/MEMORY.md");
        await TurnAsync(firstWatch, first, "say hello");
        await TurnAsync(secondWatch, second, "say hello");
        int? secondHasTabs = await NookProcesses.ExitCodeAsync(_alice, second.NookId, "test", "-e", "/root/.claude/memory/tabs.md");
        int? secondStillTests = await MemoryHasAsync(second, "Runs tests first");

        Assert.Equal(0, firstHasBoth);
        Assert.Equal(0, secondHasBoth);
        Assert.Equal(1, secondHasTabs);
        Assert.Equal(0, secondStillTests);
    }

    public void Dispose()
    {
        _alice.Dispose();
    }

    private static string PathOf(WorkspaceId workspace)
    {
        return string.Create(CultureInfo.InvariantCulture, $"/workspaces/{workspace.Value}");
    }

    // A new workspace with an account on the fake model: OpenAI's API for Codex and pi, Anthropic's for Claude Code.
    private async Task<(WorkspaceId Workspace, AgentAccountId Account)> WorkspaceAsync(bool openAI)
    {
        WorkspaceSummary workspace = await Api.ReadAsync<WorkspaceSummary>(_alice.SendPostAsync("/workspaces", new { name = "Acme" }), HttpStatusCode.Created);
        object account = openAI
            ? new { kind = "OpenAIApiKey", name = "Team key", secret = FakeModel.ApiKey, endpoint = controlPlane.Model.OpenAIUrl }
            : new { kind = "AnthropicApiKey", name = "Team key", secret = FakeModel.ApiKey, endpoint = controlPlane.Model.Url };
        AgentAccountSummary added = await Api.ReadAsync<AgentAccountSummary>(_alice.SendPostAsync(PathOf(workspace.Id) + "/agent-accounts", account), HttpStatusCode.Created);
        return (workspace.Id, added.Id);
    }

    private async Task<ChatSummary> StartChatAsync(WorkspaceId workspace, AgentAccountId account, string harness)
    {
        return await Api.ReadAsync<ChatSummary>(_alice.SendPostAsync(PathOf(workspace) + "/chats", new { provider = "docker", harness, account }), HttpStatusCode.Created);
    }

    // The chat's first turn: sends the message and waits for the checkpoint after it, when the
    // harness state is synced too.
    private async Task TurnAsync(ChatSummary chat, string text)
    {
        await using ChatWatch watch = await ChatWatch.OpenAsync(_alice, chat);
        await TurnAsync(watch, chat, text);
    }

    // A turn of a chat whose events the watch follows from the start, so each wait is for this turn's checkpoint.
    private async Task TurnAsync(ChatWatch watch, ChatSummary chat, string text)
    {
        await Api.ReadAsync<ChatMessage>(_alice.SendPostAsync(string.Create(CultureInfo.InvariantCulture, $"/chats/{chat.Id.Value}/messages"), new { text }), HttpStatusCode.OK);
        await watch.NextAsync("checkpoint-saved");
    }

    // Writes a memory as Claude Code does: a file of its own, and a line for it in the index.
    private async Task LearnAsync(ChatSummary chat, string name, string fact)
    {
        string script = string.Create(CultureInfo.InvariantCulture, $"mkdir -p /root/.claude/memory && echo '{fact}' > /root/.claude/memory/{name}.md && echo '- [{fact}]({name}.md)' >> /root/.claude/memory/MEMORY.md");
        int? learned = await NookProcesses.ExitCodeAsync(_alice, chat.NookId, "sh", "-c", script);
        Assert.Equal(0, learned);
    }

    // 0 when the memory index holds every fact.
    private async Task<int?> MemoryHasAsync(ChatSummary chat, params string[] facts)
    {
        string script = string.Join(" && ", facts.Select(fact => "grep -q '" + fact + "' /root/.claude/memory/MEMORY.md"));
        return await NookProcesses.ExitCodeAsync(_alice, chat.NookId, "sh", "-c", script);
    }
}
