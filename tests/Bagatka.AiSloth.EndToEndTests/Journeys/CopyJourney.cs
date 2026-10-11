using System;
using System.Net;
using System.Net.Http;
using System.Threading.Tasks;
using Bagatka.AiSloth.Chats.Contracts;
using Xunit;

namespace Bagatka.AiSloth.EndToEndTests;

/// <summary>
/// Journey: a chat starts from another chat's files. With <c>sloth</c>, Erin lists a chat's checkpoints
/// and starts a new chat from one, whose nook has the files as they were then; a chat copied through
/// the API starts with the files as they are now, and one that continues the other's conversation gets
/// it as a file its agent's instructions name.
/// </summary>
public sealed class CopyJourney(ControlPlane app) : IDisposable
{
    private readonly string _person = "erin-" + Guid.CreateVersion7();
    private HttpClient? _erin;

    private HttpClient Erin => _erin!;

    [Fact]
    public async Task A_chat_starts_from_another_chats_files_as_they_are_or_were_at_a_checkpoint_and_can_continue_its_conversation()
    {
        await using SlothCli sloth = new SlothCli(_person);
        await sloth.RunAsync("host", "add", app.WebApiUrl.AbsoluteUri);
        await sloth.RunWithInputAsync(FakeModel.ApiKey + "\n", "account", "add", "anthropic-api-key", "--name", "Fake", "--endpoint", app.Model.Url.AbsoluteUri);
        _erin = app.ClientFor(_person);
        ChatSummary first = await SlothStartsAChatFromACheckpointOfAnotherAsync(sloth);
        await AChatCopiedNowStartsWithTheFilesAsTheyAreAsync(first);
        await AChatThatContinuesTheConversationHasItAsAFileAsync(first);
    }

    public void Dispose()
    {
        _erin?.Dispose();
    }

    private async Task<ChatSummary> SlothStartsAChatFromACheckpointOfAnotherAsync(SlothCli sloth)
    {
        await sloth.RunAsync("chat", "Please write hello.txt for me", "--harness", "claude-code");
        string shortId = sloth.Output[5..11];
        ChatSummary first = await SlothCli.NewestChatAsync(Erin);
        int? changed = await NookProcesses.ExitCodeAsync(Erin, first.NookId, "sh", "-c", "echo changed > /work/hello.txt");

        int listed = await sloth.RunAsync("chat", "checkpoints", shortId);
        string checkpoints = sloth.Output;
        int started = await sloth.RunAsync("chat", "say hello", "--from", shortId + "@1");
        string second = sloth.Output;
        ChatSummary copy = await SlothCli.NewestChatAsync(Erin);
        int? restored = await NookProcesses.ExitCodeAsync(Erin, copy.NookId, "grep", "-q", "hi from the fake model", "/work/hello.txt");

        Assert.Equal((0, 0, 0), (changed, listed, started));
        Assert.StartsWith("   1  just now    after: Please write hello.txt for me\n", checkpoints, StringComparison.Ordinal);
        Assert.Contains(" · a copy of " + shortId + " at checkpoint 1\n", second, StringComparison.Ordinal);
        Assert.Contains("\nTip: this chat's files have no setup, so every new nook installs what they need from scratch. Have the agent write one: sloth chat prepare ", second, StringComparison.Ordinal);
        Assert.Equal(0, restored);
        return first;
    }

    private async Task AChatCopiedNowStartsWithTheFilesAsTheyAreAsync(ChatSummary first)
    {
        ChatSummary copy = await Api.ReadAsync<ChatSummary>(
            Erin.SendPostAsync(Paths.Workspace(first.WorkspaceId) + "/chats", new { provider = "docker", harness = "claude-code", account = first.Account, model = TestWorkspace.AnyModel, thinking = TestWorkspace.AnyThinking, copyOf = first.Id }), HttpStatusCode.Created);

        int? copied = await NookProcesses.ExitCodeAsync(Erin, copy.NookId, "grep", "-q", "changed", "/work/hello.txt");

        Assert.Equal(0, copied);
    }

    // The new chat starts on a fresh session; its agent reads the earlier conversation from a file when
    // it needs it. The person writes the first message.
    private async Task AChatThatContinuesTheConversationHasItAsAFileAsync(ChatSummary first)
    {
        string chats = Paths.Workspace(first.WorkspaceId) + "/chats";
        ChatSummary continued = await Api.ReadAsync<ChatSummary>(
            Erin.SendPostAsync(chats, new { provider = "docker", harness = "claude-code", account = first.Account, model = TestWorkspace.AnyModel, thinking = TestWorkspace.AnyThinking, copyOf = first.Id, withConversation = true }), HttpStatusCode.Created);
        Problem withoutCopy = await Api.ProblemAsync(
            Erin.SendPostAsync(chats, new { provider = "docker", harness = "claude-code", account = first.Account, model = TestWorkspace.AnyModel, thinking = TestWorkspace.AnyThinking, withConversation = true }), HttpStatusCode.BadRequest);
        await using ChatWatch watch = await ChatWatch.OpenAsync(Erin, continued);
        await Api.ReadAsync<ChatMessage>(Erin.SendPostAsync(Paths.Chat(continued) + "/messages", new { text = "say hello" }), HttpStatusCode.OK);
        await watch.NextAsync("turn-ended");
        string path = "/work/.aisloth/chats/" + first.Id.Value + ".md";
        ProcessRun conversation = await NookProcesses.RunAsync(Erin, continued.NookId, "cat", path);

        Assert.Equal(first.Id, continued.Continues);
        Assert.True(withoutCopy.Errors?.ContainsKey("withConversation"));
        Assert.StartsWith("# Please write hello.txt for me\n", conversation.StandardOutput, StringComparison.Ordinal);
        Assert.Contains("\n## Person\n\nPlease write hello.txt for me\n\n## Agent\n\n", conversation.StandardOutput, StringComparison.Ordinal);
        Assert.Contains(app.Model.Requests, body => body.Contains(path, StringComparison.Ordinal));
    }
}
