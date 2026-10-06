using System;
using System.IO;
using System.Net.Http;
using System.Threading;
using System.Threading.Tasks;

namespace Bagatka.AiSloth.Cli;

// Which sloth this is, and updating it from its releases on GitHub: the newest, or a chosen version.
// While a command runs at a keyboard, sloth looks for a newer release once a day and tells of it after.
internal sealed partial class Sloth
{
    private static readonly TimeSpan LookupEvery = TimeSpan.FromDays(1);

    // How long a lookup still going may hold up the end of a command.
    private static readonly TimeSpan LookupGrace = TimeSpan.FromSeconds(1);

    private string UpdateCheckPath => Path.Combine(home, "update.json");

    private async Task<int> ShowVersionAsync()
    {
        string line = build.Release is SlothRelease release ? "sloth " + release.Version.ToString(3) : "sloth, built from source";
        await terminal.WriteLineAsync(line);
        return 0;
    }

    // Replaces the file sloth runs from with the release's build for this platform, once it arrived
    // whole; the running sloth goes on until it ends.
    private async Task<int> UpdateAsync(string? wanted, CancellationToken ct)
    {
        if (build.Release is not SlothRelease current || build.Executable is not string executable)
        {
            await terminal.FailAsync("This sloth was built from source: build it again to update it.");
            return 1;
        }

        Version? version = wanted is null ? null : Releases.ParseVersion(wanted);
        if (wanted is not null && version is null)
        {
            await terminal.FailAsync("A version looks like 0.2.0.");
            return 2;
        }

        Releases releases = new Releases(http, current.Releases);
        GitHubRelease? release = await releases.FindAsync(version, ct);
        Version? found = release is null ? null : Releases.VersionOf(release);
        if (release is null || found is null)
        {
            await terminal.FailAsync(wanted is null ? "sloth has no releases yet." : "There's no sloth " + wanted + ".");
            return 1;
        }

        if (found == current.Version)
        {
            await terminal.WriteLineAsync("You have sloth " + found.ToString(3) + (wanted is null ? ", the newest." : " already."));
            return 0;
        }

        GitHubRelease.Asset? asset = Releases.BuildFor(release, build.Platform);
        if (asset is null)
        {
            await terminal.FailAsync("sloth " + found.ToString(3) + " has no build for " + build.Platform + ".");
            return 1;
        }

        // Saved beside the running file and moved over it, so sloth is never half replaced.
        string replacement = executable + ".new";
        try
        {
            await releases.SaveAsync(asset, replacement, ct);
        }
        catch (UnauthorizedAccessException)
        {
            await terminal.FailAsync("sloth can't write to " + Path.GetDirectoryName(executable) + ": update it with sudo, or install sloth in a folder of your own.");
            return 1;
        }
        catch (InvalidDataException damaged)
        {
            await terminal.FailAsync(damaged.Message);
            return 1;
        }

        File.Move(replacement, executable, overwrite: true);
        await terminal.WriteLineAsync("Updated sloth from " + current.Version.ToString(3) + " to " + found.ToString(3) + ".");
        return 0;
    }

    // A release newer than this one, looked for at most once a day while a command runs at a keyboard,
    // except an update, which says it anyway. Null when there's none or it isn't time to look; also
    // when GitHub can't be reached, which says nothing: the next run looks again.
    private async Task<Version?> NewerReleaseAsync(string[] args, CancellationToken ct)
    {
        if (build.Release is not SlothRelease current || !terminal.Interactive || args is ["update", ..])
        {
            return null;
        }

        UpdateCheck? last = await PrivateFile.ReadAsync(UpdateCheckPath, CliJsonContext.Default.UpdateCheck, ct);
        if (last is not null && time.GetUtcNow() - last.CheckedAt < LookupEvery)
        {
            return null;
        }

        GitHubRelease? newest;
        try
        {
            newest = await new Releases(http, current.Releases).FindAsync(version: null, ct);
        }
        catch (HttpRequestException)
        {
            return null;
        }

        await PrivateFile.WriteAsync(UpdateCheckPath, new UpdateCheck(time.GetUtcNow()), CliJsonContext.Default.UpdateCheck, ct);
        Version? version = newest is null ? null : Releases.VersionOf(newest);
        return version > current.Version ? version : null;
    }

    // Tells of a newer release once the command ended, giving a lookup still going a moment more.
    private async Task TellNewerReleaseAsync(Task<Version?> lookup, CancellationTokenSource stopping)
    {
        stopping.CancelAfter(LookupGrace);
        Version? newer;
        try
        {
            newer = await lookup;
        }
        catch (OperationCanceledException)
        {
            return;
        }

        if (newer is not null && build.Release is SlothRelease current)
        {
            await terminal.NoteAsync("sloth " + newer.ToString(3) + " is out; you have " + current.Version.ToString(3) + ". Update: sloth update");
        }
    }
}
