using System;
using System.Globalization;
using System.Text.Json;
using System.Threading.Tasks;
using Bagatka.AiSloth.Chats.Contracts;
using Xunit;

namespace Bagatka.AiSloth.EndToEndTests;

/// <summary>
/// The next chat on Azure with a repository sets up from the ready copy its slow setup left, when
/// asked for (<see cref="AzureControlPlane"/>).
/// </summary>
public sealed class AzureReadyCopyTests(AzureControlPlane azure)
{
    [Fact]
    public async Task The_next_chat_on_Azure_with_a_repository_sets_up_from_the_ready_copy_its_slow_setup_left()
    {
        ControlPlane? app = await azure.StartedAsync();
        Assert.SkipWhen(app is null, "Set BAGATKA_AZURE_SANDBOXES_GROUP and BAGATKA_NGROK_ENV_FILE to run nooks on Azure.");
        using TestChats chats = new TestChats(app, "azure");
        SlowSetupRepository repository = await SlowSetupRepository.CreateAsync(app, chats.Person);
        ChatSummary first = await repository.StartChatAsync("azure");
        await using ChatWatch firstWatch = await ChatWatch.OpenAsync(chats.Person, first);
        await chats.SendAsync(first, "Please write hello.txt for me");
        await firstWatch.NextAsync("checkpoint-saved", AzureControlPlane.FirstTurn);

        ChatSummary second = await repository.StartChatAsync("azure");
        await using ChatWatch watch = await ChatWatch.OpenAsync(chats.Person, second);
        JsonElement started = await watch.NextAsync("setup-started", AzureControlPlane.FirstTurn);
        JsonElement ended = await watch.NextAsync("setup-ended");
        int? installed = await NookProcesses.ExitCodeAsync(chats.Person, second.NookId, "test", "-f", "/work/api/deps/installed");

        Assert.True(started.GetProperty("fromReadyCopy").GetBoolean());
        Assert.True(TimeSpan.Parse(ended.GetProperty("took").GetString()!, CultureInfo.InvariantCulture) < TimeSpan.FromSeconds(SlowSetupRepository.SetupSeconds));
        Assert.Equal(0, installed);
    }
}
