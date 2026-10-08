using System;
using System.Linq;
using System.Net;
using System.Net.Http;
using System.Threading.Tasks;
using Bagatka.AiSloth.Chats.Contracts;
using Bagatka.AiSloth.Nooks.Contracts;
using Bagatka.Foundation;
using Xunit;

namespace Bagatka.AiSloth.EndToEndTests;

/// <summary>
/// Journey: apps start a chat as someone starts writing its first message, so its agent is ready when
/// they send it. <c>sloth chat</c> without a message takes the one typed next and keeps nothing when
/// nothing is typed; a draft is listed from its first message, and one nobody writes in goes with its
/// nook after the draft lifetime.
/// </summary>
public sealed class DraftsJourney(ControlPlane app, SleepyControlPlane sleepy) : IDisposable
{
    private readonly string _person = "erin-" + Guid.CreateVersion7();
    private HttpClient? _erin;

    private HttpClient Erin => _erin!;

    [Fact]
    public async Task A_chat_starts_before_its_first_message_and_goes_if_nobody_writes_in_it()
    {
        // The sleepy app's drafts go after 30 seconds; the rest of the journey runs meanwhile.
        Task lifetime = ADraftNobodyWritesInGoesWithItsNookAsync();
        await using SlothCli sloth = new SlothCli(_person);
        await sloth.RunAsync("host", "add", app.WebApiUrl.AbsoluteUri);
        await sloth.RunWithInputAsync(FakeModel.ApiKey + "\n", "account", "add", "anthropic-api-key", "--name", "Fake", "--endpoint", app.Model.Url.AbsoluteUri);
        _erin = app.ClientFor(_person);
        await SlothTakesTheMessageTypedNextAndKeepsNothingWhenNothingIsTypedAsync(sloth);
        TestWorkspace acme = await TestWorkspace.CreateAsync(app, Erin);
        await ADraftsAgentStartsAtOnceAndTheDraftIsListedFromItsFirstMessageAsync(acme);
        await lifetime;
    }

    public void Dispose()
    {
        _erin?.Dispose();
    }

    private static async Task SlothTakesTheMessageTypedNextAndKeepsNothingWhenNothingIsTypedAsync(SlothCli sloth)
    {
        int chatted = await sloth.RunWithInputAsync("Please write hello.txt for me\n", "chat", "--harness", "claude-code");
        string chat = sloth.Output;
        int nothing = await sloth.RunWithInputAsync(string.Empty, "chat");

        Assert.Equal(0, chatted);
        Assert.Contains("› You: Please write hello.txt for me\n  ▸ Write hello.txt\nDone.\n", chat, StringComparison.Ordinal);
        Assert.Equal(1, nothing);
        Assert.Contains("Nothing sent, so the chat isn't kept.", sloth.Output, StringComparison.Ordinal);
    }

    private async Task ADraftsAgentStartsAtOnceAndTheDraftIsListedFromItsFirstMessageAsync(TestWorkspace acme)
    {
        ChatSummary draft = await acme.StartChatAsync();

        ProcessSummary agent = await Api.EventuallyAsync(async () =>
        {
            Page<ProcessSummary> listed = await Api.ReadAsync<Page<ProcessSummary>>(Erin.SendGetAsync(Paths.Nook(draft.NookId) + "/processes"), HttpStatusCode.OK);
            return listed.Items.SingleOrDefault(process => process.Command is "harness");
        });
        Page<ChatSummary> before = await Api.ReadAsync<Page<ChatSummary>>(Erin.SendGetAsync(acme.Path + "/chats"), HttpStatusCode.OK);
        await acme.SendAsync(draft, "say hello");
        Page<ChatSummary> after = await Api.ReadAsync<Page<ChatSummary>>(Erin.SendGetAsync(acme.Path + "/chats"), HttpStatusCode.OK);

        Assert.Null(agent.ExitCode);
        Assert.DoesNotContain(before.Items, listed => listed.Id == draft.Id);
        Assert.Contains(after.Items, listed => listed.Id == draft.Id);
    }

    private async Task ADraftNobodyWritesInGoesWithItsNookAsync()
    {
        ControlPlane sleepyApp = await sleepy.StartedAsync();
        using HttpClient frank = sleepyApp.ClientFor("frank-" + Guid.CreateVersion7());
        TestWorkspace workspace = await TestWorkspace.CreateAsync(sleepyApp, frank);
        ChatSummary draft = await workspace.StartChatAsync();

        // The chat goes first, and its nook in the same pass a moment later.
        long started = TimeProvider.System.GetTimestamp();
        HttpStatusCode status = HttpStatusCode.OK;
        bool nookGoes = false;
        while (!(status == HttpStatusCode.NotFound && nookGoes) && TimeProvider.System.GetElapsedTime(started) < SleepyControlPlane.Eviction)
        {
            await Task.Delay(TimeSpan.FromSeconds(1), TestContext.Current.CancellationToken);
            using HttpResponseMessage response = await frank.SendGetAsync(Paths.Chat(draft));
            status = response.StatusCode;
            nookGoes = await Api.GoneOrDeletingAsync(frank, draft.NookId);
        }

        Assert.Equal(HttpStatusCode.NotFound, status);
        Assert.True(nookGoes);
    }
}
