using System;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Net;
using System.Net.Http;
using System.Net.Http.Json;
using System.Security.Cryptography;
using System.Threading;
using System.Threading.Tasks;

namespace Bagatka.AiSloth.Cli;

// sloth's releases, through GitHub's REST API: the newest or a chosen one, tagged sloth-v<version>, and
// its build for a platform, saved only when its SHA-256 is the one GitHub keeps for it. A refusal or an
// unreachable GitHub is an HttpRequestException that says so.
internal sealed class Releases(HttpMessageHandler http, Uri api)
{
    private const string TagPrefix = "sloth-v";

    // A build's file is executable by everyone and writable by its owner, as installed programs are.
    private const UnixFileMode Executable = UnixFileMode.UserRead | UnixFileMode.UserWrite | UnixFileMode.UserExecute
        | UnixFileMode.GroupRead | UnixFileMode.GroupExecute | UnixFileMode.OtherRead | UnixFileMode.OtherExecute;

    // The release with the version, or the newest one when version is null; null when there's none.
    public async Task<GitHubRelease?> FindAsync(Version? version, CancellationToken ct)
    {
        Uri url = new Uri(api, version is null ? "latest" : "tags/" + TagPrefix + version.ToString(3));
        using HttpClient client = Client();
        using HttpResponseMessage response = await client.GetAsync(url, ct);
        if (response.StatusCode == HttpStatusCode.NotFound)
        {
            return null;
        }

        if (!response.IsSuccessStatusCode)
        {
            throw new HttpRequestException(string.Create(CultureInfo.InvariantCulture, $"GitHub answered {(int)response.StatusCode} when asked for sloth's releases; try again later."));
        }

        GitHubRelease? release = await response.Content.ReadFromJsonAsync(GitHubJsonContext.Default.GitHubRelease, ct);
        return release;
    }

    // The release's version, from its tag; null for a tag that isn't one of sloth's.
    public static Version? VersionOf(GitHubRelease release)
    {
        if (!release.TagName.StartsWith(TagPrefix, StringComparison.Ordinal))
        {
            return null;
        }

        return ParseVersion(release.TagName[TagPrefix.Length..]);
    }

    // A version as releases name them, such as 0.2.0; null for anything else.
    public static Version? ParseVersion(string text)
    {
        bool parsed = Version.TryParse(text, out Version? version);
        return parsed && version!.Build >= 0 && version.Revision < 0 ? version : null;
    }

    // Saves the platform's build as an executable file at path; throws InvalidDataException, leaving
    // nothing at path, when what arrived isn't the file GitHub has.
    public async Task SaveAsync(GitHubRelease.Asset asset, string path, CancellationToken ct)
    {
        FileStreamOptions options = new FileStreamOptions { Mode = FileMode.Create, Access = FileAccess.ReadWrite };
        if (!OperatingSystem.IsWindows())
        {
            options.UnixCreateMode = Executable;
        }

        using HttpClient client = Client();
        using HttpResponseMessage response = await client.GetAsync(asset.BrowserDownloadUrl, HttpCompletionOption.ResponseHeadersRead, ct);
        response.EnsureSuccessStatusCode();
        string hash;
        await using (FileStream file = new FileStream(path, options))
        {
            await response.Content.CopyToAsync(file, ct);
            file.Position = 0;
            byte[] digest = await SHA256.HashDataAsync(file, ct);
            hash = "sha256:" + Convert.ToHexStringLower(digest);
        }

        if (!string.Equals(hash, asset.Digest, StringComparison.Ordinal))
        {
            File.Delete(path);
            throw new InvalidDataException("The download of " + asset.Name + " isn't the file GitHub has, so it wasn't installed; try again.");
        }
    }

    // The release's file for the platform, such as sloth-linux-x64; null when it has none.
    public static GitHubRelease.Asset? BuildFor(GitHubRelease release, string platform)
    {
        return release.Assets.SingleOrDefault(asset => string.Equals(asset.Name, "sloth-" + platform, StringComparison.Ordinal));
    }

    // GitHub's API answers only clients that name themselves.
    private HttpClient Client()
    {
        HttpClient client = new HttpClient(http, disposeHandler: false);
        client.DefaultRequestHeaders.UserAgent.ParseAdd("sloth");
        return client;
    }
}
