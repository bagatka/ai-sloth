using System;
using System.Globalization;
using System.Linq;
using System.Net;
using System.Net.Http;
using System.Text.Json;
using System.Threading.Tasks;
using Bagatka.AiSloth.Chats.Contracts;
using Bagatka.AiSloth.Nooks.Contracts;
using Xunit;

namespace Bagatka.AiSloth.EndToEndTests;

/// <summary>
/// Journey: a nook starts with everything its files need. Their setups, then resumes, run in their
/// folders before the agent starts; a failed setup shows in <c>sloth</c>, in full on request, and the
/// agent starts knowing it; <c>sloth chat prepare</c> has the agent write a setup the next chat's nook
/// runs; a slow setup leaves a ready copy the next chat starts from; and a nook runs containers.
/// Product analytics see a setup that failed.
/// </summary>
public sealed class FastStartJourney(ControlPlane app) : IDisposable
{
    private readonly string _person = "dev-" + Guid.CreateVersion7();
    private HttpClient? _dev;

    private HttpClient Dev => _dev!;

    [Fact]
    public async Task A_nook_starts_with_everything_its_files_need()
    {
        await using SlothCli sloth = new SlothCli(_person);
        await sloth.RunAsync("host", "add", app.WebApiUrl.AbsoluteUri);
        await sloth.RunWithInputAsync(FakeModel.ApiKey + "\n", "account", "add", "anthropic-api-key", "--name", "Fake", "--endpoint", app.Model.Url.AbsoluteUri);
        _dev = app.ClientFor(_person);
        ChatSummary first = await SlothShowsAFailedSetupAndTheAgentStartsKnowingItAsync(sloth);
        await SetupsThenResumesRunInTheirFoldersBeforeTheAgentStartsAsync(first);
        await SlothHasTheAgentPrepareAChatAndTheNextChatStartsSetUpAsync(sloth);
        await Task.WhenAll(ASlowSetupLeavesAReadyCopyTheNextChatStartsFromAsync(), ANookRunsContainersOfItsOwnAsync(first));
    }

    public void Dispose()
    {
        _dev?.Dispose();
    }

    private async Task<ChatSummary> SlothShowsAFailedSetupAndTheAgentStartsKnowingItAsync(SlothCli sloth)
    {
        await sloth.RunAsync("chat", "say hello", "--harness", "claude-code");
        string first = sloth.Output[5..11];
        ChatSummary firstChat = await SlothCli.NewestChatAsync(Dev);
        await WriteAsync(firstChat, "/work/.agents/setup", "#!/bin/sh\necho 'npm ERR! 404 Not Found: @acme/ui'\nexit 2\n", "755");

        int started = await sloth.RunAsync("chat", "say hello", "--from", first);
        string chat = sloth.Output;
        string second = chat[5..11];
        int shown = await sloth.RunAsync("chat", "setup", second);
        string setup = sloth.Output;
        ChatSummary secondChat = await SlothCli.NewestChatAsync(Dev);
        NookSetup reported = await Api.ReadAsync<NookSetup>(Dev.SendGetAsync(Paths.Nook(secondChat.NookId) + "/setup"), HttpStatusCode.OK);
        string workspace = secondChat.WorkspaceId.Value.ToString("D", CultureInfo.InvariantCulture);
        JsonElement setupEnded = await app.PostHog.EventAsync(
            "setup_ended", captured => FakePostHog.InWorkspace(captured, workspace) && !captured.GetProperty("properties").GetProperty("succeeded").GetBoolean(), TimeSpan.FromSeconds(30));

        Assert.Equal(0, started);
        Assert.Contains("\nSetting up: .agents/setup\n", chat, StringComparison.Ordinal);
        Assert.Matches("\nSetup failed after [0-9]+s \\(exit 2\\):\n  ==> \\.agents/setup\n  npm ERR! 404 Not Found: @acme/ui\n", chat);
        Assert.Contains("The agent knows and can fix it. Full output: sloth chat setup " + second + "\n", chat, StringComparison.Ordinal);
        Assert.Contains("── done · ", chat, StringComparison.Ordinal);
        Assert.Equal(1, shown);
        Assert.Equal("==> .agents/setup\nnpm ERR! 404 Not Found: @acme/ui\n==> .agents/setup failed with exit code 2\n", setup);
        Assert.Equal([".agents/setup"], reported.Scripts);
        Assert.Equal(2, reported.Run?.ExitCode);
        Assert.Contains(app.Model.Requests, body => body.Contains("setup failed with exit code 2", StringComparison.Ordinal));
        Assert.Equal(1, setupEnded.GetProperty("properties").GetProperty("scripts").GetInt32());
        return firstChat;
    }

    private async Task SetupsThenResumesRunInTheirFoldersBeforeTheAgentStartsAsync(ChatSummary first)
    {
        await WriteAsync(first, "/work/.agents/setup", "#!/bin/sh\necho \"setup in $(pwd)\" >> /tmp/ran\n", "755");
        await WriteAsync(first, "/work/app/.agents/setup", "echo \"setup in $(pwd)\" >> /tmp/ran\n", "644");
        await WriteAsync(first, "/work/app/.agents/resume", "#!/bin/sh\necho \"resume in $(pwd)\" >> /tmp/ran\n", "755");

        ChatSummary copy = await Api.ReadAsync<ChatSummary>(
            Dev.SendPostAsync(Paths.Workspace(first.WorkspaceId) + "/chats", new { provider = "docker", harness = "claude-code", account = first.Account, copyOf = first.Id }), HttpStatusCode.Created);
        await using ChatWatch watch = await ChatWatch.OpenAsync(Dev, copy);
        JsonElement started = await watch.NextAsync("setup-started");
        JsonElement ended = await watch.NextAsync("setup-ended");
        await Api.ExpectAsync(Dev.SendPostAsync(Paths.Chat(copy) + "/messages", new { text = "say hello" }), HttpStatusCode.OK);
        await watch.NextAsync("turn-ended");
        int? ran = await NookProcesses.ExitCodeAsync(Dev, copy.NookId, "sh", "-c", "printf 'setup in /work\\nsetup in /work/app\\nresume in /work/app\\n' | cmp -s - /tmp/ran");

        Assert.Equal([".agents/setup", "app/.agents/setup", "app/.agents/resume"], started.GetProperty("scripts").EnumerateArray().Select(script => script.GetString() ?? string.Empty), StringComparer.Ordinal);
        Assert.Equal((0, JsonValueKind.Null), (ended.GetProperty("exitCode").GetInt32(), ended.GetProperty("output").ValueKind));
        Assert.Equal(0, ran);
        Assert.True(IndexOf(watch, "setup-ended") < IndexOf(watch, "turn-started"));
    }

    private async Task SlothHasTheAgentPrepareAChatAndTheNextChatStartsSetUpAsync(SlothCli sloth)
    {
        await sloth.RunAsync("chat", "say hello", "--harness", "claude-code");
        string shortId = sloth.Output[5..11];

        int prepared = await sloth.RunAsync("chat", "prepare", shortId);
        string output = sloth.Output;
        int started = await sloth.RunAsync("chat", "say hello", "--from", shortId);
        string next = sloth.Output;
        ChatSummary nextChat = await SlothCli.NewestChatAsync(Dev);
        int? installed = await NookProcesses.ExitCodeAsync(Dev, nextChat.NookId, "test", "-f", "/opt/by-hand");

        Assert.Equal((0, 0), (prepared, started));
        Assert.StartsWith("Asked the agent to write a setup for this chat's files, so new nooks start with everything installed.", output, StringComparison.Ordinal);
        Assert.Contains("Try it in a fresh nook: sloth chat \"<message>\" --from " + shortId, output, StringComparison.Ordinal);
        Assert.Contains("\nSetting up: .agents/setup\n", next, StringComparison.Ordinal);
        Assert.Contains("\nSet up in ", next, StringComparison.Ordinal);
        Assert.Equal(0, installed);
    }

    private async Task ASlowSetupLeavesAReadyCopyTheNextChatStartsFromAsync()
    {
        SlowSetupRepository repository = await SlowSetupRepository.CreateAsync(app, Dev);
        ChatSummary first = await repository.StartChatAsync("docker");
        await using ChatWatch firstWatch = await ChatWatch.OpenAsync(Dev, first);
        JsonElement firstStarted = await firstWatch.NextAsync("setup-started");
        await firstWatch.NextAsync("setup-ended", TimeSpan.FromMinutes(2));
        await repository.Workspace.SendAsync(first, "Please write hello.txt for me");
        await firstWatch.NextAsync("turn-ended");
        await repository.MoveOnAsync();

        ChatSummary second = await repository.StartChatAsync("docker");
        await using ChatWatch watch = await ChatWatch.OpenAsync(Dev, second);
        JsonElement started = await watch.NextAsync("setup-started", TimeSpan.FromMinutes(2));
        JsonElement ended = await watch.NextAsync("setup-ended");
        int? caughtUp = await repository.Workspace.RunAsync(second.NookId, "test -f /work/api/deps/installed && test -f /work/api/LATEST.md && test ! -e /work/hello.txt");

        Assert.Equal((false, true), (firstStarted.GetProperty("fromReadyCopy").GetBoolean(), started.GetProperty("fromReadyCopy").GetBoolean()));
        Assert.Equal(0, ended.GetProperty("exitCode").GetInt32());
        Assert.True(TimeSpan.Parse(ended.GetProperty("took").GetString()!, CultureInfo.InvariantCulture) < TimeSpan.FromSeconds(SlowSetupRepository.SetupSeconds));
        Assert.Equal(0, caughtUp);
    }

    // A project's containers run in its nook as on a laptop: an image built from a Dockerfile, a
    // published port, and compose services reaching each other by name. busybox keeps it to one pull.
    private async Task ANookRunsContainersOfItsOwnAsync(ChatSummary chat)
    {
        ProcessRun run = await NookProcesses.RunAsync(Dev, chat.NookId, "sh", "-c", """
            set -e
            mkdir -p /tmp/app && cd /tmp/app
            cat > Dockerfile <<'END'
            FROM busybox
            RUN echo built > /built
            CMD ["cat", "/built"]
            END
            docker build --quiet --tag app . >/dev/null
            docker run --rm app
            docker run --detach --publish 8080:80 busybox sh -c 'echo published > /index.html && httpd -f -h /' >/dev/null
            curl --silent --fail --retry 20 --retry-all-errors --retry-delay 1 http://localhost:8080/
            cat > compose.yaml <<'END'
            services:
              web:
                image: busybox
                command: sh -c "echo served > /index.html && httpd -f -h /"
              client:
                image: busybox
                depends_on: [web]
                command: sh -c "for i in $$(seq 50); do wget -qO- http://web/ && exit 0; sleep 0.2; done; exit 1"
            END
            docker compose run --rm --no-TTY client
            """);

        Assert.True(run.ExitCode == 0, run.StandardError);
        Assert.Equal("built\npublished\nserved\n", run.StandardOutput);
    }

    private static int IndexOf(ChatWatch watch, string type)
    {
        return watch.Seen.FindIndex(seen => string.Equals(seen.Type, type, StringComparison.Ordinal));
    }

    private async Task WriteAsync(ChatSummary chat, string path, string content, string mode)
    {
        int? written = await NookProcesses.ExitCodeAsync(Dev, chat.NookId, "sh", "-c", "mkdir -p \"$(dirname \"$1\")\" && printf %s \"$2\" > \"$1\" && chmod \"$3\" \"$1\"", "sh", path, content, mode);
        Assert.Equal(0, written);
    }
}
