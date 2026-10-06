using System;
using System.Globalization;
using System.Net;
using System.Net.Http;
using System.Threading.Tasks;
using Bagatka.AiSloth.Chats.Contracts;
using Xunit;

namespace Bagatka.AiSloth.EndToEndTests;

/// <summary>
/// A chat nobody writes in goes with its nook after the draft lifetime.
/// </summary>
public sealed class DraftLifetimeTests(SleepyControlPlane sleepy)
{
    [Fact]
    public async Task A_chat_nobody_writes_in_goes_with_its_nook()
    {
        ControlPlane app = await sleepy.StartedAsync();
        using TestChats chats = new TestChats(app, "docker");
        ChatSummary chat = await chats.StartChatAsync();

        long started = TimeProvider.System.GetTimestamp();
        HttpStatusCode chatStatus = HttpStatusCode.OK;
        while (chatStatus != HttpStatusCode.NotFound && TimeProvider.System.GetElapsedTime(started) < SleepyControlPlane.Eviction)
        {
            await Task.Delay(TimeSpan.FromSeconds(1), TestContext.Current.CancellationToken);
            using HttpResponseMessage response = await chats.Person.SendGetAsync(string.Create(CultureInfo.InvariantCulture, $"/chats/{chat.Id.Value}"));
            chatStatus = response.StatusCode;
        }

        bool nookGoes = await Api.GoneOrDeletingAsync(chats.Person, chat.NookId);

        Assert.Equal(HttpStatusCode.NotFound, chatStatus);
        Assert.True(nookGoes);
    }
}
