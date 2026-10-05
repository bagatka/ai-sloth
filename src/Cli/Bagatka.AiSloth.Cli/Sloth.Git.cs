using System.Net.Http;
using System.Text.RegularExpressions;
using System.Threading;
using System.Threading.Tasks;

namespace Bagatka.AiSloth.Cli;

// How a person's commits and branches look, in the nooks they start and the pushes they make.
internal sealed partial class Sloth
{
    private async Task<int> ShowGitSettingsAsync(CancellationToken ct)
    {
        HostsFile hosts = await ReadHostsAsync(ct);
        SignedInHost? host = await CurrentHostAsync(hosts);
        if (host is null)
        {
            return 1;
        }

        using HostApi api = ApiFor(host);
        Wire.GitSettings settings = await api.GetAsync("/git-settings", CliJsonContext.Default.GitSettings, ct);
        string author = settings.Author is null ? "your GitHub account" + Shown(settings.Effective?.Author) : Shown(settings.Author);
        string committer = settings.Committer is null ? "the author" : Shown(settings.Committer);
        await terminal.WriteLineAsync("Author:     " + author.Trim());
        await terminal.WriteLineAsync("Committer:  " + committer.Trim());
        await terminal.WriteLineAsync("Co-author:  AiSloth, " + (settings.AiSlothCoAuthor ? "on" : "off"));
        await terminal.WriteLineAsync("Branches:   " + settings.BranchPrefix + "<chat>, unless a push names one");
        return 0;
    }

    // Changes one choice and keeps the others: `author "Name <email>"` or `author github`,
    // `committer "Name <email>"` or `committer author`, `co-author on|off`, `branch-prefix <prefix>`.
    private async Task<int> SetGitSettingAsync(string setting, string value, CancellationToken ct)
    {
        HostsFile hosts = await ReadHostsAsync(ct);
        SignedInHost? host = await CurrentHostAsync(hosts);
        if (host is null)
        {
            return 1;
        }

        using HostApi api = ApiFor(host);
        Wire.GitSettings current = await api.GetAsync("/git-settings", CliJsonContext.Default.GitSettings, ct);
        Wire.SetGitSettings settings = new Wire.SetGitSettings(current.Author, current.Committer, current.AiSlothCoAuthor, current.BranchPrefix);
        Wire.GitIdentity? identity = Identity(value);
        Wire.SetGitSettings? changed = setting switch
        {
            "author" when value is "github" => settings with { Author = null },
            "author" when identity is not null => settings with { Author = identity },
            "committer" when value is "author" => settings with { Committer = null },
            "committer" when identity is not null => settings with { Committer = identity },
            "co-author" when value is "on" or "off" => settings with { AiSlothCoAuthor = value is "on" },
            "branch-prefix" => settings with { BranchPrefix = value },
            _ => null,
        };
        if (changed is null)
        {
            return await UsageAsync();
        }

        _ = await api.SendAsync(HttpMethod.Put, "/git-settings", changed, CliJsonContext.Default.SetGitSettings, CliJsonContext.Default.GitSettings, ct);
        return await ShowGitSettingsAsync(ct);
    }

    // "Name <email>", as git writes an identity.
    private static Wire.GitIdentity? Identity(string text)
    {
        Match match = IdentityPattern().Match(text);
        return match.Success ? new Wire.GitIdentity(match.Groups["name"].Value.Trim(), match.Groups["email"].Value.Trim()) : null;
    }

    private static string Shown(Wire.GitIdentity? identity)
    {
        return identity is null ? string.Empty : " " + identity.Name + " <" + identity.Email + ">";
    }

    [GeneratedRegex("^(?<name>[^<>]+)<(?<email>[^<>]+)>$", RegexOptions.None, matchTimeoutMilliseconds: 1000)]
    private static partial Regex IdentityPattern();
}
