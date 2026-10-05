using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Net.Http;
using System.Net.ServerSentEvents;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;

namespace Bagatka.AiSloth.Cli;

// Chats: each starts in a nook of its own and goes on without sloth. Following one shows its events
// from the start; anything typed goes to the agent at once, joining the running turn or starting the
// next, and /stop stops it. Ctrl+C only leaves: the agent keeps working.
internal sealed partial class Sloth
{
    // Connections lost in a row before following a chat gives up.
    private const int MaxReconnects = 5;

    // How far back a chat's short ID is looked for: the newest chats, a page at a time.
    // Not handled: older chats, which need their whole ID.
    private const int ChatPagesSearched = 5;

    private async Task<int> StartChatAsync(string[] words, CancellationToken ct)
    {
        CommandLine? line = CommandLine.Parse(words, ["--harness", "--account", "--on"], []);
        if (line is not { Arguments: [string message] })
        {
            return await UsageAsync();
        }

        (SignedInHost Host, Guid Workspace)? inUse = await WorkspaceInUseAsync(ct);
        if (inUse is not (SignedInHost host, Guid workspace))
        {
            return 1;
        }

        using HostApi api = ApiFor(host);
        ChatSetup? setup = await ChooseSetupAsync(api, host, workspace, line, ct);
        if (setup is not (Wire.Account account, Wire.Harness harness, Wire.Provider provider))
        {
            return 1;
        }

        Wire.Chat chat = await api.SendAsync(
            HttpMethod.Post,
            "/workspaces/" + workspace + "/chats",
            new Wire.StartChat(provider.Id, harness.Id, account.Id),
            CliJsonContext.Default.StartChat,
            CliJsonContext.Default.Chat,
            ct);
        HostsFile hosts = await ReadHostsAsync(ct);
        await PrivateFile.WriteAsync(HostsPath, hosts.With(host with { Defaults = new ChatDefaults(harness.Id, account.Id, provider.Id) }), CliJsonContext.Default.HostsFile, ct);
        await terminal.WriteLineAsync("Chat " + ShortId(chat.Id) + " · " + harness.Name + " · " + account.Name + " · " + provider.Name);
        ChatPrinter printer = new ChatPrinter(terminal, api, host.UserId);
        Wire.SentMessage sent = await SendMessageAsync(api, chat.Id, message, ct);
        return await FollowAsync(api, chat, printer, terminal.Interactive ? null : sent.Id, ct);
    }

    // What a new chat runs with, from the command's options and the last chat's choices, or null after
    // saying what to choose.
    private async Task<ChatSetup?> ChooseSetupAsync(HostApi api, SignedInHost host, Guid workspace, CommandLine line, CancellationToken ct)
    {
        IReadOnlyList<Wire.Account> accounts = await api.GetAsync("/workspaces/" + workspace + "/agent-accounts", CliJsonContext.Default.IReadOnlyListAccount, ct);
        Wire.Account? account = await ChooseAsync(
            [.. accounts.Where(found => !found.NeedsSignIn)],
            line.Value("--account"),
            host.Defaults?.Account.ToString("D", CultureInfo.InvariantCulture),
            found => found.Id.ToString("D", CultureInfo.InvariantCulture),
            found => found.Name,
            "account",
            "--account",
            "There's no agent account to run it on. Add one: sloth account add");
        if (account is null)
        {
            return null;
        }

        IReadOnlyList<Wire.Harness> harnesses = await api.GetAsync("/harnesses", CliJsonContext.Default.IReadOnlyListHarness, ct);
        Wire.Harness? harness = await ChooseAsync(
            [.. harnesses.Where(found => found.Accepts.Contains(account.Kind, StringComparer.Ordinal))],
            line.Value("--harness"),
            host.Defaults?.Harness,
            found => found.Id,
            found => found.Name,
            "harness",
            "--harness",
            "No harness on " + host.Name + " runs on \"" + account.Name + "\".");
        if (harness is null)
        {
            return null;
        }

        IReadOnlyList<Wire.Provider> providers = await api.GetAsync("/workspaces/" + workspace + "/providers", CliJsonContext.Default.IReadOnlyListProvider, ct);
        Wire.Provider? provider = await ChooseAsync(
            [.. providers.Where(found => found.Available)],
            line.Value("--on"),
            host.Defaults?.Provider,
            found => found.Id,
            found => found.Name,
            "place to run",
            "--on",
            "Nowhere can start nooks for the workspace right now; its machines are offline.");
        if (provider is null)
        {
            return null;
        }

        return new ChatSetup(account, harness, provider);
    }

    private sealed record ChatSetup(Wire.Account Account, Wire.Harness Harness, Wire.Provider Provider);

    private async Task<int> ListChatsAsync(CancellationToken ct)
    {
        (SignedInHost Host, Guid Workspace)? inUse = await WorkspaceInUseAsync(ct);
        if (inUse is not (SignedInHost host, Guid workspace))
        {
            return 1;
        }

        using HostApi api = ApiFor(host);
        Wire.ChatPage chats = await api.GetAsync("/workspaces/" + workspace + "/chats?limit=20", CliJsonContext.Default.ChatPage, ct);
        if (chats.Items.Count == 0)
        {
            await terminal.WriteLineAsync("No chats yet. Start one: sloth chat \"<message>\"");
            return 0;
        }

        IReadOnlyList<Wire.Person> people = await api.GetAsync(PeoplePath(chats.Items.Select(chat => chat.StartedBy)), CliJsonContext.Default.IReadOnlyListPerson, ct);
        foreach (Wire.Chat chat in chats.Items)
        {
            string startedBy = chat.StartedBy == host.UserId ? "you" : people.FirstOrDefault(person => person.Id == chat.StartedBy)?.Name ?? "someone";
            await terminal.WriteLineAsync(
                ShortId(chat.Id) + "  " + chat.Harness.PadRight(12) + "  " + (chat.Working ? "working" : "idle   ") + "  started " + Ago(chat.StartedAt) + " by " + startedBy);
        }

        return 0;
    }

    private async Task<int> OpenChatAsync(string id, CancellationToken ct)
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

        await terminal.WriteLineAsync("Chat " + ShortId(chat.Id) + " · " + chat.Harness + (chat.Working ? " · working" : string.Empty));
        return await FollowAsync(api, chat, new ChatPrinter(terminal, api, host.UserId), until: null, ct);
    }

    private async Task<int> SendToChatAsync(string id, string text, CancellationToken ct)
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

        Wire.SentMessage sent = await SendMessageAsync(api, chat.Id, text, ct);
        await terminal.WriteLineAsync(sent.IsProposal
            ? "Proposed: the chat runs on someone else's plan, so its owner decides whether it reaches the agent."
            : "Sent. Follow the chat: sloth chat open " + ShortId(chat.Id));
        return 0;
    }

    private async Task<int> StopChatAsync(string id, CancellationToken ct)
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

        await api.CallAsync(HttpMethod.Post, "/chats/" + chat.Id + "/stop", ct);
        await terminal.WriteLineAsync("Stopped: the turn ended, and messages the agent hadn't read were cancelled.");
        return 0;
    }

    // Shows the chat's events from the start while sending what the person types. Without a person at
    // the keyboard, it ends when the turn of the message `until` ends: 0 when it finished, 1 when it
    // failed or never started. Otherwise it ends only with Ctrl+C or the end of input, leaving the agent
    // working.
    private async Task<int> FollowAsync(HostApi api, Wire.Chat chat, ChatPrinter printer, Guid? until, CancellationToken ct)
    {
        if (terminal.Interactive)
        {
            await terminal.WriteLineAsync("Type to write to the agent, even while it works; /stop stops it. Ctrl+C leaves it working.");
        }

        using CancellationTokenSource leave = CancellationTokenSource.CreateLinkedTokenSource(ct);
        Task<int> watching = WatchAsync(api, chat, printer, until, leave.Token);
        Task typing = terminal.Interactive ? TypeAsync(api, chat, printer, leave.Token) : Task.Delay(Timeout.InfiniteTimeSpan, leave.Token);
        Task first = await Task.WhenAny(watching, typing);
        await leave.CancelAsync();
        await typing.ConfigureAwait(ConfigureAwaitOptions.SuppressThrowing);
        if (first == watching && !ct.IsCancellationRequested)
        {
            return await watching;
        }

        await ((Task)watching).ConfigureAwait(ConfigureAwaitOptions.SuppressThrowing);
        await terminal.WriteLineAsync();
        await terminal.WriteLineAsync("Left the chat; the agent keeps working. Follow it again: sloth chat open " + ShortId(chat.Id));
        return 0;
    }

    // The chat's events as they happen, resuming after the last one shown when the connection drops.
    private async Task<int> WatchAsync(HostApi api, Wire.Chat chat, ChatPrinter printer, Guid? until, CancellationToken ct)
    {
        long after = 0;
        bool ours = false;
        int reconnects = 0;
        string lost = "the host ended the stream";
        while (reconnects <= MaxReconnects)
        {
            try
            {
                string path = string.Create(CultureInfo.InvariantCulture, $"/chats/{chat.Id}/events?after={after}");
                using HttpResponseMessage response = await api.SendAsync(HttpMethod.Get, path, content: null, ct, HttpCompletionOption.ResponseHeadersRead);
                await api.EnsureSuccessAsync(response, ct);
                await using Stream stream = await response.Content.ReadAsStreamAsync(ct);
                await foreach (SseItem<string> item in SseParser.Create(stream).EnumerateAsync(ct))
                {
                    reconnects = 0;
                    Wire.ChatEvent chatEvent = JsonSerializer.Deserialize(item.Data, CliJsonContext.Default.ChatEvent)!;
                    after = chatEvent.Sequence;
                    await printer.PrintAsync(item.EventType, chatEvent, ct);
                    Guid? message = MessageOf(chatEvent.Event);
                    if (until is null)
                    {
                        continue;
                    }

                    if (message == until && item.EventType is "turn-started" or "message-steered")
                    {
                        ours = true;
                    }
                    else if (message == until && item.EventType is "message-cancelled")
                    {
                        return 1;
                    }
                    else if (ours && item.EventType is "turn-ended")
                    {
                        bool failed = chatEvent.Event.GetProperty("stopReason").GetString() is "failed";
                        return failed ? 1 : 0;
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

        await terminal.FailAsync("Lost the chat (" + lost + "). Follow it again: sloth chat open " + ShortId(chat.Id));
        return 1;
    }

    // What the person types while following: each line goes to the agent; /stop stops it. A message
    // that fails to send is reported, and typing goes on.
    private async Task TypeAsync(HostApi api, Wire.Chat chat, ChatPrinter printer, CancellationToken ct)
    {
        string? line = await terminal.ReadLineAsync(ct);
        while (line is not null)
        {
            string text = line.Trim();
            try
            {
                if (text is "/stop")
                {
                    await api.CallAsync(HttpMethod.Post, "/chats/" + chat.Id + "/stop", ct);
                }
                else if (text.Length > 0)
                {
                    printer.TypedHere(text);
                    await SendMessageAsync(api, chat.Id, text, ct);
                }
            }
            catch (HttpRequestException exception)
            {
                await terminal.FailAsync(exception.Message);
            }

            line = await terminal.ReadLineAsync(ct);
        }
    }

    // The chat people mean: by its whole ID, or by the short one sloth shows (the end of the whole ID)
    // among the workspace's newest chats. Null after saying why not.
    private async Task<Wire.Chat?> FindChatAsync(HostApi api, SignedInHost host, string id, CancellationToken ct)
    {
        bool whole = Guid.TryParse(id, CultureInfo.InvariantCulture, out Guid chatId);
        if (whole)
        {
            return await api.GetAsync("/chats/" + chatId, CliJsonContext.Default.Chat, ct);
        }

        if (host.Workspace is null)
        {
            await terminal.FailAsync(NoWorkspace(host));
            return null;
        }

        if (id.Length < 4 || !id.All(char.IsAsciiHexDigit))
        {
            await terminal.FailAsync("A chat's ID is like 7f3a2b, as sloth chat list shows it.");
            return null;
        }

        List<Wire.Chat> found = [];
        string? cursor = null;
        for (int page = 0; page < ChatPagesSearched && (page == 0 || cursor is not null); page++)
        {
            string query = cursor is null ? string.Empty : "&cursor=" + Uri.EscapeDataString(cursor);
            Wire.ChatPage chats = await api.GetAsync("/workspaces/" + host.Workspace + "/chats?limit=200" + query, CliJsonContext.Default.ChatPage, ct);
            found.AddRange(chats.Items.Where(chat => chat.Id.ToString("N", CultureInfo.InvariantCulture).EndsWith(id, StringComparison.OrdinalIgnoreCase)));
            cursor = chats.NextCursor;
        }

        if (found is not [Wire.Chat match])
        {
            await terminal.FailAsync(found.Count == 0
                ? "There's no chat " + id + " in \"" + host.WorkspaceName + "\". The chats: sloth chat list"
                : id + " is the end of several chats' IDs; give more of it.");
            return null;
        }

        return match;
    }

    // What a chat runs with: what the person named (by ID or name), else what the last chat on this host
    // ran with while it's still a choice, else the only choice. Null after saying what to choose from.
    private async Task<T?> ChooseAsync<T>(IReadOnlyList<T> choices, string? named, string? last, Func<T, string> idOf, Func<T, string> nameOf, string what, string option, string none)
        where T : class
    {
        string listed = string.Join(", ", choices.Select(nameOf));
        if (named is not null)
        {
            List<T> matches = [.. choices.Where(choice => string.Equals(idOf(choice), named, StringComparison.OrdinalIgnoreCase) || string.Equals(nameOf(choice), named, StringComparison.OrdinalIgnoreCase))];
            if (matches is [T match])
            {
                return match;
            }

            await terminal.FailAsync(matches.Count == 0
                ? choices.Count == 0 ? none : "Choose the " + what + " from: " + listed + "."
                : "Several of them are named \"" + named + "\"; use one's ID: " + string.Join(", ", matches.Select(idOf)));
            return null;
        }

        T? remembered = choices.FirstOrDefault(choice => string.Equals(idOf(choice), last, StringComparison.OrdinalIgnoreCase));
        if (remembered is not null)
        {
            return remembered;
        }

        if (choices is [T only])
        {
            return only;
        }

        await terminal.FailAsync(choices.Count == 0 ? none : "Choose the " + what + " with " + option + ": " + listed + ".");
        return null;
    }

    private static async Task<Wire.SentMessage> SendMessageAsync(HostApi api, Guid chat, string text, CancellationToken ct)
    {
        return await api.SendAsync(
            HttpMethod.Post, "/chats/" + chat + "/messages", new Wire.SendMessage(text), CliJsonContext.Default.SendMessage, CliJsonContext.Default.SentMessage, ct);
    }

    // The names of people, for showing who did what.
    private static string PeoplePath(IEnumerable<Guid> people)
    {
        return "/users?" + string.Join("&", people.Distinct().Select(person => "ids=" + person));
    }

    // The message an event is about, if it is about one.
    private static Guid? MessageOf(JsonElement body)
    {
        bool has = body.TryGetProperty("messageId", out JsonElement message);
        return has ? message.GetGuid() : null;
    }

    // The end of a chat's ID, which is random, unlike its start, which is when it started.
    private static string ShortId(Guid chat)
    {
        return chat.ToString("N", CultureInfo.InvariantCulture)[^6..];
    }
}
