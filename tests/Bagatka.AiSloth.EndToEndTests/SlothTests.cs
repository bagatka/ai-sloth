using System;
using System.Globalization;
using System.IO;
using System.Net;
using System.Net.Http;
using System.Text.RegularExpressions;
using System.Threading.Tasks;
using Bagatka.AiSloth.Chats.Contracts;
using Bagatka.AiSloth.Workspaces.Contracts;
using Bagatka.Foundation;
using Xunit;

namespace Bagatka.AiSloth.EndToEndTests;

/// <summary>
/// The <c>sloth</c> command line against the real host: signing in, accounts, secrets, and a chat with
/// the real agent, Claude Code, in a real nook. Only the model and the identity provider are fake.
/// </summary>
public sealed partial class SlothTests(ControlPlane controlPlane)
{
    private string Host => controlPlane.WebApiUrl.Authority;

    [Fact]
    public async Task Signing_in_in_the_browser_then_on_another_device_with_a_link_code_and_out_again()
    {
        string person = "dana-" + Guid.CreateVersion7();
        await using SlothCli laptop = new SlothCli(person);
        await using SlothCli phone = new SlothCli(person);

        int added = await laptop.RunAsync("host", "add", controlPlane.WebApiUrl.AbsoluteUri);
        string addedOutput = laptop.Output;
        int linked = await laptop.RunAsync("host", "link");
        string code = LinkCode().Match(laptop.Output).Groups["code"].Value;
        int wrong = await phone.RunAsync("host", "add", controlPlane.WebApiUrl.AbsoluteUri, "--code", "AAAABBBBCCCCDDDD");
        int linkedIn = await phone.RunAsync("host", "add", controlPlane.WebApiUrl.AbsoluteUri, "--code", code);
        string? phoneToken = await phone.TokenForAsync(Host);
        int signedOut = await phone.RunAsync("host", "remove", Host);
        int afterSignOut = await phone.RunAsync("workspace", "list");
        using HttpClient endedSession = controlPlane.ClientWithToken(phoneToken);
        await Api.ExpectAsync(endedSession.SendGetAsync("/users/me"), HttpStatusCode.Unauthorized);
        int laptopStill = await laptop.RunAsync("workspace", "list");

        Assert.Equal(0, added);
        Assert.Contains("Signed in to " + Host + " as ", addedOutput, StringComparison.Ordinal);
        Assert.Contains("Commands use the workspace", addedOutput, StringComparison.Ordinal);
        Assert.Equal(0, linked);
        Assert.Equal(1, wrong);
        Assert.Equal(0, linkedIn);
        Assert.NotNull(phoneToken);
        Assert.Equal(0, signedOut);
        Assert.Equal(1, afterSignOut);
        Assert.Contains("You aren't signed in to a host", phone.Errors, StringComparison.Ordinal);
        Assert.Equal(0, laptopStill);
        Assert.StartsWith("* ", laptop.Output, StringComparison.Ordinal);
    }

    [Fact]
    public async Task Adding_an_account_lists_each_kind_by_a_name_that_cant_be_confused()
    {
        await using SlothCli sloth = await SignedInAsync();

        int listed = await sloth.RunAsync("account", "add");

        Assert.Equal(0, listed);
        Assert.Matches("chatgpt-plan +ChatGPT Plus or Pro +sign in with ChatGPT +Codex, pi", sloth.Output);
        Assert.Matches("claude-plan +Claude Pro or Max +not allowed on ", sloth.Output);
        Assert.Matches("openai-api-key +OpenAI's API", sloth.Output);
        Assert.Matches("anthropic-api-key +Anthropic's API.+Claude Code, pi", sloth.Output);
    }

    [Fact]
    public async Task A_secret_read_from_standard_input_is_listed_by_name_only()
    {
        await using SlothCli sloth = await SignedInAsync();

        int set = await sloth.RunWithInputAsync("ghp_from_stdin\n", "secret", "set", "GH_TOKEN");
        int listed = await sloth.RunAsync("secret", "list");

        Assert.Equal(0, set);
        Assert.Equal(0, listed);
        Assert.Matches("^GH_TOKEN +set just now", sloth.Output);
        Assert.DoesNotContain("ghp_from_stdin", sloth.Output, StringComparison.Ordinal);
    }

    [Fact]
    public async Task A_chat_started_with_sloth_shows_the_agents_work_until_its_turn_ends()
    {
        await using SlothCli sloth = await SignedInAsync();
        await sloth.RunWithInputAsync(FakeModel.ApiKey + "\n", "account", "add", "anthropic-api-key", "--name", "Fake", "--endpoint", controlPlane.Model.Url.AbsoluteUri);

        int chatted = await sloth.RunAsync("chat", "Please write hello.txt for me", "--harness", "claude-code");
        string chat = sloth.Output;
        string shortId = chat[5..11];
        int listed = await sloth.RunAsync("chat", "list");

        Assert.Equal(0, chatted);
        Assert.Matches("^Chat [0-9a-f]{6} · Claude Code · Fake · docker", chat);
        Assert.Contains("› You: Please write hello.txt for me\n  ▸ Write hello.txt\nDone.\n", chat, StringComparison.Ordinal);
        Assert.Matches("── done · [0-9]+s ──", chat);
        Assert.Equal(0, listed);
        Assert.StartsWith(shortId + "  claude-code", sloth.Output, StringComparison.Ordinal);
    }

    [Fact]
    public async Task A_repository_goes_from_GitHub_into_a_chat_and_back_as_a_pull_request()
    {
        string person = "erin-" + Guid.CreateVersion7();
        string owner = "acme-" + Guid.CreateVersion7().ToString("N", CultureInfo.InvariantCulture)[^8..];
        await controlPlane.GitHub.CreateRepositoryAsync(owner, "api", person);
        await using SlothCli sloth = new SlothCli(person);
        await sloth.RunAsync("host", "add", controlPlane.WebApiUrl.AbsoluteUri);
        await sloth.RunWithInputAsync(FakeModel.ApiKey + "\n", "account", "add", "anthropic-api-key", "--name", "Fake", "--endpoint", controlPlane.Model.Url.AbsoluteUri);

        int connected = await sloth.RunAsync("github", "connect");
        string connectedOutput = sloth.Output;
        int added = await sloth.RunAsync("repo", "add", owner + "/api");
        int chatted = await sloth.RunAsync("chat", "Please write hello.txt for me", "--harness", "claude-code", "--repo", "api");
        string chat = sloth.Output;
        string shortId = chat[5..11];
        await ChangeAsync(sloth, "echo hi > /work/api/CHANGE.md");
        int pushed = await sloth.RunAsync("chat", "push", shortId, "--pr");
        string pushedOutput = sloth.Output;
        string archive = Path.Combine(Path.GetTempPath(), "sloth-e2e-" + shortId + ".tar.gz");
        int downloaded = await sloth.RunAsync("chat", "download", shortId, "--source", "api", "--out", archive);

        Assert.Equal(0, connected);
        Assert.Contains("Connected GitHub as " + person + ".", connectedOutput, StringComparison.Ordinal);
        Assert.Equal(0, added);
        Assert.Equal(0, chatted);
        Assert.Matches("^Chat [0-9a-f]{6} · Claude Code · Fake · docker · api\n", chat);
        Assert.Equal(0, pushed);
        Assert.Matches("^api: 1 commits on aisloth/" + shortId + "  http://127.0.0.1:[0-9]+/" + owner + "/api/pull/1\n", pushedOutput);
        Assert.Equal(0, downloaded);
        Assert.True(new FileInfo(archive).Length > 0);
        File.Delete(archive);
    }

    // Changes the chat's files as an agent would, through the API with the CLI's own session.
    private async Task ChangeAsync(SlothCli sloth, string script)
    {
        string? token = await sloth.TokenForAsync(Host);
        using HttpClient client = controlPlane.ClientWithToken(token);
        Page<WorkspaceSummary> workspaces = await Api.ReadAsync<Page<WorkspaceSummary>>(client.SendGetAsync("/workspaces"), HttpStatusCode.OK);
        Page<ChatSummary> chats = await Api.ReadAsync<Page<ChatSummary>>(client.SendGetAsync(string.Create(CultureInfo.InvariantCulture, $"/workspaces/{workspaces.Items[0].Id.Value}/chats")), HttpStatusCode.OK);
        int? changed = await NookProcesses.ExitCodeAsync(client, chats.Items[0].NookId, "sh", "-c", script);
        Assert.Equal(0, changed);
    }

    [GeneratedRegex("--code (?<code>[A-Z0-9]{16})", RegexOptions.None, matchTimeoutMilliseconds: 1000)]
    private static partial Regex LinkCode();

    private async Task<SlothCli> SignedInAsync()
    {
        SlothCli sloth = new SlothCli("erin-" + Guid.CreateVersion7());
        int added = await sloth.RunAsync("host", "add", controlPlane.WebApiUrl.AbsoluteUri);
        Assert.Equal(0, added);
        return sloth;
    }
}
