using System;
using System.Globalization;
using System.Net;
using System.Net.Http;
using System.Text.Json;
using System.Threading.Tasks;
using Bagatka.AiSloth.Chats.Contracts;
using Xunit;

namespace Bagatka.AiSloth.EndToEndTests;

/// <summary>
/// Fast start: a setup that took a while leaves a ready copy of its nook, which the next nooks with the
/// same files start from, catching up to their files and finishing the setup quickly.
/// </summary>
public sealed class ReadyCopiesTests(ControlPlane controlPlane) : IDisposable
{
    private readonly HttpClient _dev = controlPlane.ClientFor("dev-" + Guid.CreateVersion7());

    [Fact]
    public async Task The_next_chat_with_a_repository_sets_up_from_the_ready_copy_its_slow_setup_left_and_catches_up()
    {
        SlowSetupRepository repository = await SlowSetupRepository.CreateAsync(controlPlane, _dev);
        ChatSummary first = await repository.StartChatAsync("docker");
        await using ChatWatch firstWatch = await ChatWatch.OpenAsync(_dev, first);
        JsonElement firstStarted = await firstWatch.NextAsync("setup-started");
        await firstWatch.NextAsync("setup-ended", TimeSpan.FromMinutes(2));
        await SendAsync(first, "Please write hello.txt for me");
        await firstWatch.NextAsync("checkpoint-saved");
        await repository.MoveOnAsync();

        ChatSummary second = await repository.StartChatAsync("docker");
        await using ChatWatch watch = await ChatWatch.OpenAsync(_dev, second);
        JsonElement started = await watch.NextAsync("setup-started", TimeSpan.FromMinutes(2));
        JsonElement ended = await watch.NextAsync("setup-ended");
        int? installed = await NookProcesses.ExitCodeAsync(_dev, second.NookId, "test", "-f", "/work/api/deps/installed");
        int? latest = await NookProcesses.ExitCodeAsync(_dev, second.NookId, "test", "-f", "/work/api/LATEST.md");
        int? onlySetupsWork = await NookProcesses.ExitCodeAsync(_dev, second.NookId, "test", "!", "-e", "/work/hello.txt");

        Assert.False(firstStarted.GetProperty("fromReadyCopy").GetBoolean());
        Assert.True(started.GetProperty("fromReadyCopy").GetBoolean());
        Assert.Equal(0, ended.GetProperty("exitCode").GetInt32());
        Assert.True(TimeSpan.Parse(ended.GetProperty("took").GetString()!, CultureInfo.InvariantCulture) < TimeSpan.FromSeconds(SlowSetupRepository.SetupSeconds));
        Assert.Equal(0, installed);
        Assert.Equal(0, latest);
        Assert.Equal(0, onlySetupsWork);
    }

    public void Dispose()
    {
        _dev.Dispose();
    }

    private async Task SendAsync(ChatSummary chat, string text)
    {
        await Api.ReadAsync<ChatMessage>(_dev.SendPostAsync(string.Create(CultureInfo.InvariantCulture, $"/chats/{chat.Id.Value}/messages"), new { text }), HttpStatusCode.OK);
    }
}
