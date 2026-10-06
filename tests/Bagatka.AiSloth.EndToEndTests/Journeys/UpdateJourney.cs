using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Text;
using System.Threading.Tasks;
using Bagatka.AiSloth.Cli;
using Xunit;

namespace Bagatka.AiSloth.EndToEndTests;

/// <summary>
/// Journey: <c>sloth</c> keeps itself up to date from its releases on GitHub. At a keyboard it tells
/// once a day when a newer release is out; it updates to the newest or a chosen version; it never
/// installs a download that isn't the file GitHub has; and one built from source says to build it
/// again. GitHub is fake; the file sloth runs from is a stand-in in a folder of the journey's own.
/// </summary>
public sealed class UpdateJourney(ControlPlane app) : IDisposable
{
    private readonly string _owner = "acme-" + Guid.CreateVersion7().ToString("N", CultureInfo.InvariantCulture)[^8..];
    private readonly DirectoryInfo _installed = Directory.CreateTempSubdirectory("sloth-installed-");

    private string Executable => Path.Combine(_installed.FullName, "sloth");

    [Fact]
    public async Task Sloth_tells_of_a_newer_release_and_updates_itself()
    {
        Publish("sloth-v0.1.0", "the first sloth");
        Publish("sloth-v0.2.0", "the second sloth");
        await File.WriteAllTextAsync(Executable, "the first sloth", TestContext.Current.CancellationToken);
        await SlothTellsOnceADayThatANewerReleaseIsOutAsync();
        await SlothUpdatesToTheNewestReleaseAsync();
        await SlothGoesToAChosenVersionAsync();
        await ADamagedDownloadIsNeverInstalledAsync();
        await SlothBuiltFromSourceSaysToBuildItAgainAsync();
    }

    public void Dispose()
    {
        _installed.Delete(recursive: true);
    }

    private async Task SlothTellsOnceADayThatANewerReleaseIsOutAsync()
    {
        await using SlothCli sloth = Installed("0.1.0");

        int shown = await sloth.RunAsync("version");
        (string output, string told) = (sloth.Output, sloth.Errors);
        await sloth.RunAsync("version");

        Assert.Equal(0, shown);
        Assert.Equal("sloth 0.1.0\n", output);
        Assert.Equal("sloth 0.2.0 is out; you have 0.1.0. Update: sloth update\n", told);
        Assert.Empty(sloth.Errors);
    }

    private async Task SlothUpdatesToTheNewestReleaseAsync()
    {
        await using SlothCli sloth = Installed("0.1.0");

        int updated = await sloth.RunAsync("update");
        string installed = await File.ReadAllTextAsync(Executable, TestContext.Current.CancellationToken);

        Assert.Equal(0, updated);
        Assert.Equal("Updated sloth from 0.1.0 to 0.2.0.\n", sloth.Output);
        Assert.Equal("the second sloth", installed);
        Assert.True(File.GetUnixFileMode(Executable).HasFlag(UnixFileMode.OtherExecute));
    }

    private async Task SlothGoesToAChosenVersionAsync()
    {
        await using SlothCli sloth = Installed("0.2.0");

        int newest = await sloth.RunAsync("update");
        string already = sloth.Output;
        int chosen = await sloth.RunAsync("update", "0.1.0");
        string downgraded = sloth.Output;
        int missing = await sloth.RunAsync("update", "9.9.9");
        string installed = await File.ReadAllTextAsync(Executable, TestContext.Current.CancellationToken);

        Assert.Equal((0, 0, 1), (newest, chosen, missing));
        Assert.Equal("You have sloth 0.2.0, the newest.\n", already);
        Assert.Equal("Updated sloth from 0.2.0 to 0.1.0.\n", downgraded);
        Assert.Equal("the first sloth", installed);
        Assert.Equal("There's no sloth 9.9.9.\n", sloth.Errors);
    }

    private async Task ADamagedDownloadIsNeverInstalledAsync()
    {
        Publish("sloth-v0.3.0", "the third sloth", wrongDigests: true);
        await using SlothCli sloth = Installed("0.1.0");

        int updated = await sloth.RunAsync("update");
        string installed = await File.ReadAllTextAsync(Executable, TestContext.Current.CancellationToken);

        Assert.Equal(1, updated);
        Assert.Equal("The download of sloth-linux-x64 isn't the file GitHub has, so it wasn't installed; try again.\n", sloth.Errors);
        Assert.Equal("the first sloth", installed);
        Assert.Equal(["sloth"], Array.ConvertAll(_installed.GetFiles(), file => file.Name));
    }

    private static async Task SlothBuiltFromSourceSaysToBuildItAgainAsync()
    {
        await using SlothCli sloth = new SlothCli("erin-" + Guid.CreateVersion7());

        int shown = await sloth.RunAsync("version");
        string output = sloth.Output;
        int updated = await sloth.RunAsync("update");

        Assert.Equal((0, 1), (shown, updated));
        Assert.Equal("sloth, built from source\n", output);
        Assert.Equal("This sloth was built from source: build it again to update it.\n", sloth.Errors);
    }

    // sloth at the version, released from the journey's repository and run at a keyboard.
    private SlothCli Installed(string version)
    {
        Uri releases = new Uri(app.GitHub.ApiUrl, "repos/" + _owner + "/sloth/releases/");
        SlothBuild build = new SlothBuild(new SlothRelease(Version.Parse(version), releases), "linux-x64", Executable);
        return new SlothCli("erin-" + Guid.CreateVersion7(), build, interactive: true);
    }

    private void Publish(string tag, string content, bool wrongDigests = false)
    {
        Dictionary<string, byte[]> files = new Dictionary<string, byte[]>(StringComparer.Ordinal) { ["sloth-linux-x64"] = Encoding.UTF8.GetBytes(content) };
        app.GitHub.PublishRelease(_owner, "sloth", tag, files, wrongDigests);
    }
}
