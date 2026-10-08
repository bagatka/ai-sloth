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

        return await FollowSetupAsync(api, chat, setup.Run.Process, ct);
    }

    // Prints the setup run's output until it exits, resuming after the last byte shown when the
    // connection drops, as when the host is replaced by a deploy; the run's exit decides the code.
    private async Task<int> FollowSetupAsync(HostApi api, Wire.Chat chat, Guid process, CancellationToken ct)
    {
        long offset = 0;
        int reconnects = 0;
        string lost = "the host ended the output";
        while (reconnects <= MaxReconnects)
        {
            try
            {
                string path = string.Create(CultureInfo.InvariantCulture, $"/nooks/{chat.NookId}/processes/{process}/output?fromOffset={offset}");
                using HttpResponseMessage response = await api.SendAsync(HttpMethod.Get, path, content: null, ct, HttpCompletionOption.ResponseHeadersRead);
                await api.EnsureSuccessAsync(response, ct);
                await using Stream stream = await response.Content.ReadAsStreamAsync(ct);
                await foreach (SseItem<string> item in SseParser.Create(stream).EnumerateAsync(ct))
                {
                    reconnects = 0;
                    if (item.EventType is "ProcessOutput")
                    {
                        Wire.ProcessOutput output = JsonSerializer.Deserialize(item.Data, CliJsonContext.Default.ProcessOutput)!;
                        await terminal.WriteAsync(Encoding.UTF8.GetString(output.Data));
                        offset = output.Offset + output.Data.Length;
                    }
                    else if (item.EventType is "ProcessExited")
                    {
                        Wire.ProcessExit exit = JsonSerializer.Deserialize(item.Data, CliJsonContext.Default.ProcessExit)!;
                        return exit.ExitCode == 0 ? 0 : 1;
                    }
                }
            }
            catch (Exception exception) when (exception is IOException or HttpRequestException { StatusCode: null })
            {
                lost = exception.Message;
            }

            reconnects++;
            await Task.Delay(TimeSpan.FromSeconds(reconnects), time, ct);
        }

        await terminal.FailAsync("Lost the setup's output (" + lost + "). Follow it again: sloth chat setup " + ShortId(chat.Id));
        return 1;
    }

    // What `sloth chat prepare` asks the agent; AiSloth's instructions tell every agent how setups work.
    private const string PrepareRequest = "Prepare this chat's files for AiSloth: write their setup, run it twice, and commit it.";

    // Asks the agent to prepare the chat, then follows its turn without taking input: the setup is
    // the point. Ctrl+C leaves it working.
    private async Task<int> PrepareChatAsync(string id, CancellationToken ct)
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

        Wire.SentMessage sent = await SendMessageAsync(api, chat.Id, PrepareRequest, ct);
        await terminal.WriteLineAsync("Asked the agent to write a setup for this chat's files, so new nooks start with everything installed. Ctrl+C leaves it working.");
        ChatPrinter printer = new ChatPrinter(terminal, api, host.UserId, chat.Id, suggestPrepare: false);
        int prepared;
        try
        {
            prepared = await WatchAsync(api, chat, printer, sent.Id, ct);
        }
        catch (OperationCanceledException) when (ct.IsCancellationRequested)
        {
            await terminal.WriteLineAsync();
            await terminal.WriteLineAsync("Left the chat; the agent keeps preparing. Follow it: sloth chat open " + ShortId(chat.Id));
            return 0;
        }

        if (prepared == 0)
        {
            await terminal.WriteLineAsync("Try it in a fresh nook: sloth chat \"<message>\" --from " + ShortId(chat.Id));
        }

        return prepared;
    }
}
