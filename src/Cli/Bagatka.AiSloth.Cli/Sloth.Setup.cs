using System;
using System.Globalization;
using System.IO;
using System.Net.Http;
using System.Net.ServerSentEvents;
using System.Text;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;

namespace Bagatka.AiSloth.Cli;

// The setup of a chat's files: their .agents/setup and .agents/resume scripts, which run whenever its
// nook gets the files, before the agent starts; and preparing it.
internal sealed partial class Sloth
{
    // Prints the latest setup run's output from its start, following it while it runs; fails when
    // the setup did.
    private async Task<int> ShowSetupAsync(string id, CancellationToken ct)
    {
        HostsFile hosts = await ReadHostsAsync(ct);
        SignedInHost? host = await CurrentHostAsync(hosts);
        if (host is null)
        {
            return 1;
        }

        using HostApi api = ApiFor(host);
        Wire.Chat? chat = await FindChatAsync(api, host, id, ct);
        if (chat is null)
        {
            return 1;
        }

        Wire.NookSetup setup = await api.GetAsync("/nooks/" + chat.NookId + "/setup", CliJsonContext.Default.NookSetup, ct);
        if (setup.Run is null)
        {
            await terminal.WriteLineAsync("The chat's files have no setup: .agents/setup and .agents/resume scripts at their top, or at the top of a repository in them. Have the agent write one: sloth chat prepare " + ShortId(chat.Id));
            return 0;
        }

        string path = string.Create(CultureInfo.InvariantCulture, $"/nooks/{chat.NookId}/processes/{setup.Run.Process}/output?fromOffset=0");
        using HttpResponseMessage response = await api.SendAsync(HttpMethod.Get, path, content: null, ct, HttpCompletionOption.ResponseHeadersRead);
        await api.EnsureSuccessAsync(response, ct);
        await using Stream stream = await response.Content.ReadAsStreamAsync(ct);
        await foreach (SseItem<string> item in SseParser.Create(stream).EnumerateAsync(ct))
        {
            if (item.EventType is "output")
            {
                Wire.ProcessOutput output = JsonSerializer.Deserialize(item.Data, CliJsonContext.Default.ProcessOutput)!;
                await terminal.WriteAsync(Encoding.UTF8.GetString(output.Data));
            }
            else if (item.EventType is "exit")
            {
                Wire.ProcessExit exit = JsonSerializer.Deserialize(item.Data, CliJsonContext.Default.ProcessExit)!;
                return exit.ExitCode == 0 ? 0 : 1;
            }
        }

        await terminal.FailAsync("The host ended the output before the setup ended. Try again: sloth chat setup " + ShortId(chat.Id));
        return 1;
    }

    // Asks the agent to prepare the chat, then follows the chat until the setup's last test ended,
    // without taking input: the setup is the point. Ctrl+C leaves it working.
    private async Task<int> PrepareChatAsync(string id, bool anyway, CancellationToken ct)
    {
        HostsFile hosts = await ReadHostsAsync(ct);
        SignedInHost? host = await CurrentHostAsync(hosts);
        if (host is null)
        {
            return 1;
        }

        using HostApi api = ApiFor(host);
        Wire.Chat? chat = await FindChatAsync(api, host, id, ct);
        if (chat is null)
        {
            return 1;
        }

        Wire.SentMessage sent = await api.SendAsync(
            HttpMethod.Post, "/chats/" + chat.Id + "/prepare", new Wire.Prepare(anyway), CliJsonContext.Default.Prepare, CliJsonContext.Default.SentMessage, ct);
        await terminal.WriteLineAsync("Asked the agent to write a setup for this chat's files, so new nooks start with everything installed. Ctrl+C leaves it working.");
        ChatPrinter printer = new ChatPrinter(terminal, api, host.UserId, chat.Id, suggestPrepare: false);
        int tested;
        try
        {
            tested = await WatchAsync(api, chat, printer, sent.Id, untilTested: true, ct);
        }
        catch (OperationCanceledException) when (ct.IsCancellationRequested)
        {
            await terminal.WriteLineAsync();
            await terminal.WriteLineAsync("Left the chat; the agent keeps preparing. Follow it: sloth chat open " + ShortId(chat.Id));
            return 0;
        }

        Wire.Nook nook = await api.GetAsync("/nooks/" + chat.NookId, CliJsonContext.Default.Nook, ct);
        if (tested == 0 && nook.Sources.Count > 0)
        {
            await terminal.WriteLineAsync("Push it: sloth chat push " + ShortId(chat.Id) + " --pr");
        }

        return tested;
    }
}
