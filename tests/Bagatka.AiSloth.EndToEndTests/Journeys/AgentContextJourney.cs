using System;
using System.Globalization;
using System.Linq;
using System.Net;
using System.Net.Http;
using System.Threading.Tasks;
using Bagatka.AiSloth.Chats.Contracts;
using Xunit;

namespace Bagatka.AiSloth.EndToEndTests;

/// <summary>
/// Journey: what agents get from AiSloth besides people's messages. Every harness follows the
/// instructions its workspace and person give; Claude Code's memory follows its person into their next
/// chats in the workspace and no further; and chats working side by side share what each learns at
/// their next turn. The fake model sees what the real harnesses send it. The parts are independent,
/// so they run at the same time, each as someone else.
/// </summary>
public sealed class AgentContextJourney(ControlPlane app)
{
    [Fact]
    public async Task Agents_follow_their_instructions_and_remember_what_their_person_taught_them()
    {
        await Task.WhenAll(
            EveryHarnessFollowsTheWorkspacesAndItsPersonsInstructionsAsync("claude-code"),
            EveryHarnessFollowsTheWorkspacesAndItsPersonsInstructionsAsync("codex"),
            EveryHarnessFollowsTheWorkspacesAndItsPersonsInstructionsAsync("pi"),
            ClaudeCodesMemoryFollowsItsPersonIntoTheirNextChatsInTheWorkspaceAndNoFurtherAsync(),
            ChatsSideBySideGetWhatEachLearnsAtTheirNextTurnAsync());
    }

    private async Task EveryHarnessFollowsTheWorkspacesAndItsPersonsInstructionsAsync(string harness)
    {
        string workspaceRule = "Use tabs, team rule " + Guid.CreateVersion7().ToString("N", CultureInfo.InvariantCulture);
        string personalRule = "Answer briefly, my rule " + Guid.CreateVersion7().ToString("N", CultureInfo.InvariantCulture);
        using HttpClient alice = app.ClientFor("alice-" + Guid.CreateVersion7());
        TestWorkspace acme = await TestWorkspace.CreateAsync(app, alice, openAI: harness is not "claude-code");
        await Api.ExpectAsync(alice.SendPutAsync(acme.Path + "/instructions", new { text = workspaceRule }), HttpStatusCode.NoContent);
        await Api.ExpectAsync(alice.SendPutAsync("/instructions", new { text = personalRule }), HttpStatusCode.NoContent);

        Instructions instructions = await Api.ReadAsync<Instructions>(alice.SendGetAsync(acme.Path + "/instructions"), HttpStatusCode.OK);
        ChatSummary chat = await acme.StartChatAsync(harness);
        await TurnAsync(acme, chat);

        Assert.Equal(new Instructions(workspaceRule, personalRule), instructions);
        Assert.Contains(app.Model.Requests, body => body.Contains(workspaceRule, StringComparison.Ordinal) && body.Contains(personalRule, StringComparison.Ordinal));
    }

    private async Task ClaudeCodesMemoryFollowsItsPersonIntoTheirNextChatsInTheWorkspaceAndNoFurtherAsync()
    {
        string remembered = "Prefers tabs, remembered " + Guid.CreateVersion7().ToString("N", CultureInfo.InvariantCulture);
        using HttpClient bob = app.ClientFor("bob-" + Guid.CreateVersion7());
        TestWorkspace acme = await TestWorkspace.CreateAsync(app, bob);
        ChatSummary first = await acme.StartChatAsync();
        await acme.ChangeAsync(first.NookId, "mkdir -p /root/.claude/memory && printf -- '- %s\\n' '" + remembered + "' > /root/.claude/memory/MEMORY.md");
        await TurnAsync(acme, first);
        HarnessStateSummary[] saved = await Api.ReadAsync<HarnessStateSummary[]>(bob.SendGetAsync(acme.Path + "/harness-state"), HttpStatusCode.OK);
        int toldBefore = app.Model.Requests.Count(body => body.Contains(remembered, StringComparison.Ordinal));

        ChatSummary second = await acme.StartChatAsync();
        await TurnAsync(acme, second);
        int toldAfter = app.Model.Requests.Count(body => body.Contains(remembered, StringComparison.Ordinal));
        TestWorkspace elsewhere = await TestWorkspace.CreateAsync(app, bob);
        ChatSummary third = await elsewhere.StartChatAsync();
        await TurnAsync(elsewhere, third);
        int? keptHere = await acme.RunAsync(second.NookId, "test -e /root/.claude/memory/MEMORY.md");
        int? keptElsewhere = await elsewhere.RunAsync(third.NookId, "test -e /root/.claude/memory/MEMORY.md");
        await Api.ExpectAsync(bob.DeleteAsync(new Uri(acme.Path + "/harness-state/claude-code", UriKind.Relative), TestContext.Current.CancellationToken), HttpStatusCode.NoContent);
        HarnessStateSummary[] forgotten = await Api.ReadAsync<HarnessStateSummary[]>(bob.SendGetAsync(acme.Path + "/harness-state"), HttpStatusCode.OK);

        HarnessStateSummary state = Assert.Single(saved);
        Assert.Equal(("claude-code", first.Id), (state.Harness, state.SavedFrom));
        Assert.True(toldAfter > toldBefore, "The second chat's agent never told its model what it remembered.");
        Assert.Equal((0, 1), (keptHere, keptElsewhere));
        Assert.Empty(forgotten);
    }

    private async Task ChatsSideBySideGetWhatEachLearnsAtTheirNextTurnAsync()
    {
        using HttpClient carol = app.ClientFor("carol-" + Guid.CreateVersion7());
        TestWorkspace acme = await TestWorkspace.CreateAsync(app, carol);
        ChatSummary first = await acme.StartChatAsync();
        ChatSummary second = await acme.StartChatAsync();
        await using ChatWatch firstWatch = await ChatWatch.OpenAsync(carol, first);
        await using ChatWatch secondWatch = await ChatWatch.OpenAsync(carol, second);
        await TurnAsync(acme, firstWatch, first);
        await TurnAsync(acme, secondWatch, second);

        // Each agent learns something while the other works.
        await LearnAsync(acme, first, "tabs", "Prefers tabs");
        await LearnAsync(acme, second, "tests", "Runs tests first");
        await TurnAsync(acme, firstWatch, first);
        await TurnAsync(acme, secondWatch, second);
        await TurnAsync(acme, firstWatch, first);
        int? firstHasBoth = await acme.RunAsync(first.NookId, "grep -q 'Prefers tabs' /root/.claude/memory/MEMORY.md && grep -q 'Runs tests first' /root/.claude/memory/MEMORY.md");
        int? secondHasBoth = await acme.RunAsync(second.NookId, "grep -q 'Prefers tabs' /root/.claude/memory/MEMORY.md && grep -q 'Runs tests first' /root/.claude/memory/MEMORY.md");

        // One forgets something, and the other loses it at its next turn.
        await acme.ChangeAsync(first.NookId, "rm /root/.claude/memory/tabs.md && grep -v tabs /root/.claude/memory/MEMORY.md > /tmp/m && mv /tmp/m /root/.claude/memory/MEMORY.md");
        await TurnAsync(acme, firstWatch, first);
        await TurnAsync(acme, secondWatch, second);
        int? secondForgot = await acme.RunAsync(second.NookId, "test ! -e /root/.claude/memory/tabs.md && grep -q 'Runs tests first' /root/.claude/memory/MEMORY.md");

        Assert.Equal((0, 0, 0), (firstHasBoth, secondHasBoth, secondForgot));
    }

    // A turn of a new chat: sends a message and waits for the checkpoint after it, when the harness
    // state is saved too.
    private static async Task TurnAsync(TestWorkspace workspace, ChatSummary chat)
    {
        await using ChatWatch watch = await ChatWatch.OpenAsync(workspace.Person, chat);
        await TurnAsync(workspace, watch, chat);
    }

    // A turn of a chat whose events the watch follows from the start, so each wait is for this turn's checkpoint.
    private static async Task TurnAsync(TestWorkspace workspace, ChatWatch watch, ChatSummary chat)
    {
        await workspace.SendAsync(chat, "say hello");
        await watch.NextAsync("checkpoint-saved");
    }

    // Writes a memory as Claude Code does: a file of its own, and a line for it in the index.
    private static async Task LearnAsync(TestWorkspace workspace, ChatSummary chat, string name, string fact)
    {
        await workspace.ChangeAsync(chat.NookId, string.Create(CultureInfo.InvariantCulture, $"mkdir -p /root/.claude/memory && echo '{fact}' > /root/.claude/memory/{name}.md && echo '- [{fact}]({name}.md)' >> /root/.claude/memory/MEMORY.md"));
    }
}
