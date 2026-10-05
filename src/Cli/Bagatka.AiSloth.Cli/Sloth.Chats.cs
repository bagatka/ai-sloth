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
        CommandLine? line = CommandLine.Parse(words, ["--harness", "--account", "--on", "--from"], [], repeatable: ["--repo"]);
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
        List<Wire.NookRepository>? repositories = await ResolveRepositoriesAsync(api, host, workspace, line.Values("--repo"), ct);
        Wire.Chat? copyOf = null;
        int? checkpoint = null;
        if (line.Value("--from") is string from)
        {
            (string fromChat, checkpoint) = ChatAndCheckpoint(from);
            copyOf = await FindChatAsync(api, host, fromChat, ct);
        }

        if (setup is not (Wire.Account account, Wire.Harness harness, Wire.Provider provider) || repositories is null || (line.Value("--from") is not null && copyOf is null))
        {
            return 1;
        }

        Wire.Chat chat = await api.SendAsync(
            HttpMethod.Post,
            "/workspaces/" + workspace + "/chats",
            new Wire.StartChat(provider.Id, harness.Id, account.Id, repositories, copyOf?.Id, checkpoint),
            CliJsonContext.Default.StartChat,
            CliJsonContext.Default.Chat,
            ct);
        HostsFile hosts = await ReadHostsAsync(ct);
        await PrivateFile.WriteAsync(HostsPath, hosts.With(host with { Defaults = new ChatDefaults(harness.Id, account.Id, provider.Id) }), CliJsonContext.Default.HostsFile, ct);
        string at = checkpoint is int number ? string.Create(CultureInfo.InvariantCulture, $" at checkpoint {number}") : string.Empty;
        string starting = line.Values("--repo").Count > 0 ? " · " + string.Join(", ", line.Values("--repo")) : copyOf is null ? string.Empty : " · a copy of " + ShortId(copyOf.Id) + at;
        await terminal.WriteLineAsync("Chat " + ShortId(chat.Id) + " · " + harness.Name + " · " + account.Name + " · " + provider.Name + starting);
        ChatPrinter printer = new ChatPrinter(terminal, api, host.UserId, chat.Id, suggestPrepare: repositories.Count > 0 || copyOf is not null);
        Wire.SentMessage sent = await SendMessageAsync(api, chat.Id, message, anyway: false, ct);
        return await FollowAsync(api, chat, printer, terminal.Interactive ? null : sent.Id, ct);
    }

    // `<chat>@<checkpoint>` as the chat and the checkpoint's number; without a number, the chat's
    // files as they are. A number that isn't one is left in the chat's ID, which then isn't found.
    private static (string Chat, int? Checkpoint) ChatAndCheckpoint(string from)
    {
        int at = from.LastIndexOf('@', StringComparison.Ordinal);
        if (at <= 0)
        {
            return (from, null);
        }

        bool numbered = int.TryParse(from.AsSpan(at + 1), NumberStyles.None, CultureInfo.InvariantCulture, out int number);
        return numbered ? (from[..at], number) : (from, null);
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
        return await FollowAsync(api, chat, new ChatPrinter(terminal, api, host.UserId, chat.Id, suggestPrepare: false), until: null, ct);
    }

    private async Task<int> SendToChatAsync(string id, string text, bool anyway, CancellationToken ct)
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

        Wire.SentMessage sent = await SendMessageAsync(api, chat.Id, text, anyway, ct);
        await terminal.WriteLineAsync(sent.IsProposal
            ? "Proposed: the chat runs on someone else's plan, so its owner decides whether it reaches the agent."
            : "Sent. Follow the chat: sloth chat open " + ShortId(chat.Id));
        return 0;
    }

    // Pushes the chat's changes to GitHub: every source that changed, onto one branch, with a pull
    // request each when asked. Exits with 1 when any repository's push was refused.
    private async Task<int> PushChatAsync(string id, string[] words, CancellationToken ct)
    {
        CommandLine? line = CommandLine.Parse(words, ["--branch", "--message"], ["--pr"], repeatable: ["--source"]);
        if (line is not { Arguments: [] })
        {
            return await UsageAsync();
        }

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

        IReadOnlyList<string>? names = line.Values("--source").Count > 0 ? line.Values("--source") : null;
        Wire.Push push = new Wire.Push(names, line.Value("--branch"), line.Has("--pr"), line.Value("--message"));
        IReadOnlyList<Wire.PushedSource> pushed = await api.SendAsync(
            HttpMethod.Post, "/chats/" + chat.Id + "/push", push, CliJsonContext.Default.Push, CliJsonContext.Default.IReadOnlyListPushedSource, ct);
        if (pushed.Count == 0)
        {
            await terminal.WriteLineAsync("The chat's nook has no repositories to push.");
        }

        foreach (Wire.PushedSource source in pushed)
        {
            string outcome = source.Problem is not null ? "not pushed: " + source.Problem
                : source.Branch is null ? "no changes"
                : string.Create(CultureInfo.InvariantCulture, $"{source.Commits} commits on {source.Branch}  {source.PullRequestUrl ?? source.BranchUrl}");
            await terminal.WriteLineAsync(source.Source + ": " + outcome);
        }

        return pushed.Any(source => source.Problem is not null) ? 1 : 0;
    }

    // The chat's checkpoints, newest first: the latest 50.
    private async Task<int> ListCheckpointsAsync(string id, CancellationToken ct)
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

        Wire.CheckpointPage checkpoints = await api.GetAsync("/nooks/" + chat.NookId + "/checkpoints?limit=50", CliJsonContext.Default.CheckpointPage, ct);
        if (checkpoints.Items.Count == 0)
        {
            await terminal.WriteLineAsync("No checkpoints yet: the chat saves one after each turn.");
            return 0;
        }

        foreach (Wire.Checkpoint checkpoint in checkpoints.Items)
        {
            string number = checkpoint.Number.ToString(CultureInfo.InvariantCulture);
            await terminal.WriteLineAsync(number.PadLeft(4) + "  " + Ago(checkpoint.CreatedAt).PadRight(10) + "  after: " + checkpoint.Note);
        }

        await terminal.WriteLineAsync("Start from one: sloth chat \"<message>\" --from " + ShortId(chat.Id) + "@<n>");
        return 0;
    }

    // Saves the chat's files as a gzipped tar archive: one of its sources, or all its files, as they
    // are or at a checkpoint.
    private async Task<int> DownloadChatAsync(string id, string[] words, CancellationToken ct)
    {
        CommandLine? line = CommandLine.Parse(words, ["--source", "--out", "--checkpoint"], []);
        if (line is not { Arguments: [] })
        {
            return await UsageAsync();
        }

        int? checkpoint = null;
        if (line.Value("--checkpoint") is string given)
        {
            bool numbered = int.TryParse(given, NumberStyles.None, CultureInfo.InvariantCulture, out int parsed);
            if (!numbered)
            {
                return await UsageAsync();
            }

            checkpoint = parsed;
        }

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

        string? source = line.Value("--source");
        string at = checkpoint is int saved ? "@" + saved.ToString(CultureInfo.InvariantCulture) : string.Empty;
        string path = Path.GetFullPath(line.Value("--out") ?? (source ?? "work") + "-" + ShortId(chat.Id) + at + ".tar.gz");
        List<string> query = [];
        if (source is not null)
        {
            query.Add("source=" + Uri.EscapeDataString(source));
        }

        if (checkpoint is int number)
        {
            query.Add("checkpoint=" + number.ToString(CultureInfo.InvariantCulture));
        }

        string download = "/nooks/" + chat.NookId + "/download" + (query.Count == 0 ? string.Empty : "?" + string.Join('&', query));
        using HttpResponseMessage response = await api.SendAsync(HttpMethod.Get, download, content: null, ct, HttpCompletionOption.ResponseHeadersRead);
        await api.EnsureSuccessAsync(response, ct);
        await using (FileStream file = new FileStream(path, FileMode.Create, FileAccess.Write, FileShare.None, bufferSize: 81920, FileOptions.Asynchronous))
        {
            await response.Content.CopyToAsync(file, ct);
        }

        await terminal.WriteLineAsync("Saved " + path + ".");
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
    // the keyboard, it ends when the turn of the message `until` ends, after its checkpoint: 0 when it
    // finished, 1 when it failed or never started. Otherwise it ends only with Ctrl+C or the end of input, leaving the agent
    // working.
    private async Task<int> FollowAsync(HostApi api, Wire.Chat chat, ChatPrinter printer, Guid? until, CancellationToken ct)
    {
        if (terminal.Interactive)
        {
            await terminal.WriteLineAsync("Type to write to the agent, even while it works; /stop stops it. Ctrl+C leaves it working.");
        }

        using CancellationTokenSource leave = CancellationTokenSource.CreateLinkedTokenSource(ct);
        Task<int> watching = WatchAsync(api, chat, printer, until, untilTested: false, leave.Token);
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

    // The chat's events as they happen, resuming after the last one shown when the connection drops;
    // until the turn of the message `until` ended, or with `untilTested` the last test of the setup it
    // asked to prepare.
    private async Task<int> WatchAsync(HostApi api, Wire.Chat chat, ChatPrinter printer, Guid? until, bool untilTested, CancellationToken ct)
    {
        long after = 0;
        bool ours = false;
        bool checkpointDue = false;
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
                    int? ended = until is not Guid message ? null
                        : untilTested ? EndOfTest(item.EventType, chatEvent, message, ref ours)
                        : EndOf(item.EventType, chatEvent, message, ref ours, ref checkpointDue);
                    if (ended is not null)
                    {
                        return ended.Value;
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

    // The exit code once the turn of the message `until` ended, after its checkpoint: a turn that
    // ran is followed by one, so its files are saved when following ends. Null until then.
    private static int? EndOf(string type, Wire.ChatEvent chatEvent, Guid until, ref bool ours, ref bool checkpointDue)
    {
        Guid? message = MessageOf(chatEvent.Event);
        if (message == until && type is "turn-started" or "message-steered")
        {
            ours = true;
        }
        else if (message == until && type is "message-cancelled")
        {
            return 1;
        }
        else if (ours && type is "turn-ended")
        {
            bool failed = chatEvent.Event.GetProperty("stopReason").GetString() is "failed";
            checkpointDue = !failed;
            return failed ? 1 : null;
        }
        else if (checkpointDue && type is "checkpoint-saved" or "checkpoint-failed")
        {
            return 0;
        }

        return null;
    }

    // The exit code once the last test of the setup that the message `until` asked to prepare ended,
    // or its turn was stopped, so no test follows; null until then.
    private static int? EndOfTest(string type, Wire.ChatEvent chatEvent, Guid until, ref bool ours)
    {
        if (MessageOf(chatEvent.Event) == until && type is "message-sent")
        {
            ours = true;
        }
        else if (ours && type is "setup-tested" && !chatEvent.Event.GetProperty("agentFixes").GetBoolean())
        {
            return chatEvent.Event.GetProperty("exitCode").GetInt32() == 0 ? 0 : 1;
        }
        else if (ours && type is "turn-ended" && chatEvent.Event.GetProperty("stopReason").GetString() is "cancelled")
        {
            return 1;
        }

        return null;
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
                    await SendMessageAsync(api, chat.Id, text, anyway: false, ct);
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

    // Sends the message. While the nook's disk is nearly full, the host wants it confirmed: `anyway`
    // does, and otherwise the person at the keyboard is asked.
    private async Task<Wire.SentMessage> SendMessageAsync(HostApi api, Guid chat, string text, bool anyway, CancellationToken ct)
    {
        try
        {
            return await PostMessageAsync(api, chat, text, anyway, ct);
        }
        catch (HttpRequestException refused) when (refused.Data[HostApi.ProblemCode] is "chats.disk_nearly_full" && terminal.Interactive)
        {
            await terminal.WriteAsync(refused.Message + " Send it anyway? [y/N] ");
            string? answer = await terminal.ReadLineAsync(ct);
            if (answer?.Trim() is not ("y" or "Y" or "yes"))
            {
                throw;
            }

            return await PostMessageAsync(api, chat, text, confirm: true, ct);
        }
    }

    private static async Task<Wire.SentMessage> PostMessageAsync(HostApi api, Guid chat, string text, bool confirm, CancellationToken ct)
    {
        return await api.SendAsync(
            HttpMethod.Post, "/chats/" + chat + "/messages", new Wire.SendMessage(text, confirm), CliJsonContext.Default.SendMessage, CliJsonContext.Default.SentMessage, ct);
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
