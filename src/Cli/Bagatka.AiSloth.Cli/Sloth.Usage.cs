using System;
using System.Collections.Frozen;
using System.Globalization;
using System.Net.Http;
using System.Text.Json.Nodes;
using System.Threading;
using System.Threading.Tasks;
using Bagatka.PostHog;

namespace Bagatka.AiSloth.Cli;

// Usage reports: each command's name, exit code, and time, and what made sloth fail, to the PostHog
// project of the host in use, as the person signed in there, when the host names one and
// DO_NOT_TRACK doesn't turn them off. Learning the project runs beside the command; sending waits two
// seconds at most, so a PostHog out of reach never holds sloth's exit.
internal sealed partial class Sloth
{
    private static readonly TimeSpan UsageDiscoveryLimit = TimeSpan.FromSeconds(3);
    private static readonly TimeSpan UsageSendLimit = TimeSpan.FromSeconds(2);

    // sloth's commands by their first two words, as CommandAsync dispatches them; anything else is
    // named by its first word, which every command starts with, never by what follows, such as a
    // chat's message or a secret's name.
    private static readonly FrozenSet<string> TwoWordCommands = FrozenSet.Create(
        StringComparer.Ordinal,
        "host add", "host list", "host use", "host link", "host remove",
        "workspace list", "workspace use", "workspace invite", "workspace join",
        "account list", "account add", "account remove",
        "secret list", "secret set", "secret remove",
        "chat list", "chat open", "chat send", "chat stop", "chat push", "chat download", "chat checkpoints", "chat setup", "chat prepare",
        "harness state", "instructions set", "instructions clear",
        "github connect", "github disconnect", "github create-app",
        "repo list", "repo add", "repo remove",
        "git author", "git committer", "git co-author", "git branch-prefix",
        "machine connect", "machine run");

    private static readonly FrozenSet<string> FirstWords = FrozenSet.Create(
        StringComparer.Ordinal,
        "help", "host", "workspace", "account", "secret", "chat", "harness", "instructions", "github", "repo", "git", "machine", "version", "update");

    // A command's name, such as "chat open", or "chat" for one that starts a chat with a message.
    private static string CommandName(string[] args)
    {
        string twoWords = args.Length >= 2 ? args[0] + " " + args[1] : string.Empty;
        if (TwoWordCommands.Contains(twoWords))
        {
            return twoWords;
        }

        if (args.Length == 0)
        {
            return "help";
        }

        return FirstWords.Contains(args[0]) ? args[0] : "unknown";
    }

    // The host in use and its PostHog project, learned while the command runs, or null when there is
    // nothing to report to.
    private async Task<UsageReport?> UsageReportAsync(CancellationToken ct)
    {
        if (!reportsUsage)
        {
            return null;
        }

        HostsFile hosts = await ReadHostsAsync(ct);
        SignedInHost? host = hosts.CurrentHost();
        if (host is null)
        {
            return null;
        }

        using CancellationTokenSource limit = CancellationTokenSource.CreateLinkedTokenSource(ct);
        limit.CancelAfter(UsageDiscoveryLimit);
        try
        {
            using HostApi anonymous = new HostApi(http, host.Url, token: null);
            Wire.HostDiscovery discovery = await anonymous.GetAsync("/.well-known/aisloth", CliJsonContext.Default.HostDiscovery, limit.Token);
            return discovery.PostHog is null ? null : new UsageReport(host, discovery.PostHog);
        }
        catch (Exception exception) when (exception is HttpRequestException or OperationCanceledException)
        {
            // Not handled: a host out of reach just now; its commands' reports are skipped.
            return null;
        }
    }

    private async Task ReportUsageAsync(Task<UsageReport?> learning, string[] args, int exitCode, Exception? failure, TimeSpan took)
    {
        UsageReport? report = await learning;
        if (report is null)
        {
            return;
        }

        PostHogClientOptions options = new PostHogClientOptions(report.Project.Host, report.Project.ProjectToken) { ShutdownTimeout = UsageSendLimit };
        await using PostHogClient postHog = new PostHogClient(options, http);
        string person = report.Host.UserId.ToString("D", CultureInfo.InvariantCulture);
        string command = CommandName(args);
        PostHogEvent ran = new PostHogEvent("command_ran", person)
        {
            Properties =
            {
                ["command"] = command,
                ["exit_code"] = exitCode,
                ["seconds"] = took.TotalSeconds,
                ["version"] = build.Release?.Version.ToString() ?? "source",
                ["platform"] = build.Platform,
            },
        };
        if (report.Host.Workspace is Guid workspace)
        {
            ran.Groups["workspace"] = workspace.ToString("D", CultureInfo.InvariantCulture);
        }

        postHog.Capture(ran);
        if (failure is not null)
        {
            postHog.CaptureException(failure, person, new JsonObject { ["command"] = command });
        }
    }

    private sealed record UsageReport(SignedInHost Host, Wire.PostHogProject Project);
}
