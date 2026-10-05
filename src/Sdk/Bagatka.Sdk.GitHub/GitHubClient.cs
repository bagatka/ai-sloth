using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Net;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
using System.Text.Json.Serialization.Metadata;
using System.Threading;
using System.Threading.Tasks;
using Bagatka.Foundation;

namespace Bagatka.Sdk.GitHub;

/// <summary>
/// The parts of GitHub a GitHub App acting as a person uses: signing the person in with the device
/// flow, renewing their token, and reading their installations, repositories, and pull requests, and
/// opening pull requests. Calls are never retried: the OAuth ones change state at GitHub, and the
/// others are cheap for the caller to repeat. A call GitHub answers unexpectedly throws
/// <see cref="HttpRequestException"/> with its status code, such as 401 once the person revoked the
/// app. Thread-safe.
/// </summary>
public sealed class GitHubClient : IDisposable
{
    private const string ApiVersion = "2022-11-28";
    private const int PageSize = 100;

    // Not handled: a person with more than 1,000 repositories in one installation; the rest aren't listed.
    private const int MaxPages = 10;

    private readonly HttpClient _http;

    /// <summary>Creates a client for the GitHub in the settings.</summary>
    public GitHubClient(GitHubSettings settings)
    {
        ArgumentNullException.ThrowIfNull(settings);
        Settings = settings;
        SocketsHttpHandler handler = new SocketsHttpHandler { PooledConnectionLifetime = TimeSpan.FromMinutes(5) };
        _http = new HttpClient(handler, disposeHandler: true) { Timeout = TimeSpan.FromSeconds(30) };
        _http.DefaultRequestHeaders.UserAgent.Add(new ProductInfoHeaderValue("Bagatka.Sdk.GitHub", "1.0"));
    }

    /// <summary>Where GitHub runs.</summary>
    public GitHubSettings Settings { get; }

    /// <summary>Where a person installs the app on their account or organization, choosing its repositories.</summary>
    public Uri InstallationUrl(string appSlug)
    {
        return new Uri(Settings.WebUrl, "apps/" + Uri.EscapeDataString(appSlug) + "/installations/new");
    }

    /// <summary>Starts a device flow sign-in for the app (its "Enable Device Flow" setting must be on).</summary>
    public async Task<GitHubDeviceCode> RequestDeviceCodeAsync(string clientId, CancellationToken ct)
    {
        using HttpResponseMessage response = await PostFormAsync("login/device/code", new Dictionary<string, string>(StringComparer.Ordinal) { ["client_id"] = clientId }, ct).ConfigureAwait(false);
        GitHubWire.DeviceCodeResponse code = await ReadAsync(response, GitHubJsonContext.Default.DeviceCodeResponse, ct).ConfigureAwait(false);
        bool complete = code.DeviceCode is not null && code.UserCode is not null && code.VerificationUri is not null && code.ExpiresIn is not null;
        if (!complete)
        {
            throw new HttpRequestException("GitHub didn't start the device flow: " + (code.Error ?? "its answer lacks the codes") + ".");
        }

        return new GitHubDeviceCode(code.DeviceCode!, code.UserCode!, new Uri(code.VerificationUri!), TimeSpan.FromSeconds(code.ExpiresIn!.Value), TimeSpan.FromSeconds(code.Interval ?? 5));
    }

    /// <summary>Asks whether the person approved a device flow sign-in yet.</summary>
    public async Task<GitHubDevicePoll> PollDeviceAsync(string clientId, string deviceCode, CancellationToken ct)
    {
        Dictionary<string, string> form = new Dictionary<string, string>(StringComparer.Ordinal)
        {
            ["client_id"] = clientId,
            ["device_code"] = deviceCode,
            ["grant_type"] = "urn:ietf:params:oauth:grant-type:device_code",
        };
        using HttpResponseMessage response = await PostFormAsync("login/oauth/access_token", form, ct).ConfigureAwait(false);
        GitHubWire.TokenResponse tokens = await ReadAsync(response, GitHubJsonContext.Default.TokenResponse, ct).ConfigureAwait(false);
        if (tokens.AccessToken is null)
        {
            TimeSpan? interval = tokens.Interval is int seconds ? TimeSpan.FromSeconds(seconds) : null;
            return new GitHubDevicePoll(Tokens: null, tokens.Error ?? "no_token", interval);
        }

        return new GitHubDevicePoll(ToTokens(tokens), Error: null, Interval: null);
    }

    /// <summary>
    /// The next token set for a person. The refresh token it replaces stops working, so refreshes of
    /// one token set must never run at the same time. Fails with <c>github.&lt;error&gt;</c> when GitHub
    /// refuses, such as <c>github.bad_refresh_token</c> once the person revoked the app.
    /// </summary>
    public async Task<Result<GitHubUserTokens>> RefreshAsync(string clientId, string clientSecret, string refreshToken, CancellationToken ct)
    {
        Dictionary<string, string> form = new Dictionary<string, string>(StringComparer.Ordinal)
        {
            ["client_id"] = clientId,
            ["client_secret"] = clientSecret,
            ["grant_type"] = "refresh_token",
            ["refresh_token"] = refreshToken,
        };
        using HttpResponseMessage response = await PostFormAsync("login/oauth/access_token", form, ct).ConfigureAwait(false);
        GitHubWire.TokenResponse tokens = await ReadAsync(response, GitHubJsonContext.Default.TokenResponse, ct).ConfigureAwait(false);
        if (tokens.AccessToken is null)
        {
            string code = tokens.Error ?? "no_token";
            return new Result<GitHubUserTokens>(Error.Conflict("github." + code, tokens.ErrorDescription ?? code));
        }

        return new Result<GitHubUserTokens>(ToTokens(tokens));
    }

    /// <summary>The person a token acts as.</summary>
    public async Task<GitHubUser> GetAuthenticatedUserAsync(string token, CancellationToken ct)
    {
        using HttpResponseMessage response = await SendAsync(HttpMethod.Get, "user", token, content: null, ct).ConfigureAwait(false);
        GitHubWire.User user = await ReadAsync(response, GitHubJsonContext.Default.User, ct).ConfigureAwait(false);
        return new GitHubUser(user.Id, user.Login, user.Name);
    }

    /// <summary>An account by its login, such as an app's bot <c>my-app[bot]</c>; <see langword="null"/> when there's none.</summary>
    public async Task<GitHubUser?> GetUserAsync(string token, string login, CancellationToken ct)
    {
        using HttpResponseMessage response = await SendAsync(HttpMethod.Get, "users/" + Uri.EscapeDataString(login), token, content: null, ct).ConfigureAwait(false);
        if (response.StatusCode == HttpStatusCode.NotFound)
        {
            return null;
        }

        GitHubWire.User user = await ReadAsync(response, GitHubJsonContext.Default.User, ct).ConfigureAwait(false);
        return new GitHubUser(user.Id, user.Login, user.Name);
    }

    /// <summary>The app's installations the person can reach: their own account's and their organizations'.</summary>
    public async Task<IReadOnlyList<GitHubInstallation>> ListUserInstallationsAsync(string token, CancellationToken ct)
    {
        List<GitHubInstallation> installations = [];
        for (int page = 1; page <= MaxPages; page++)
        {
            string path = string.Create(CultureInfo.InvariantCulture, $"user/installations?per_page={PageSize}&page={page}");
            using HttpResponseMessage response = await SendAsync(HttpMethod.Get, path, token, content: null, ct).ConfigureAwait(false);
            GitHubWire.InstallationPage found = await ReadAsync(response, GitHubJsonContext.Default.InstallationPage, ct).ConfigureAwait(false);
            installations.AddRange(found.Installations.Select(installation => new GitHubInstallation(installation.Id, installation.Account.Login)));
            if (found.Installations.Count < PageSize)
            {
                break;
            }
        }

        return installations;
    }

    /// <summary>The repositories of an installation the person can reach.</summary>
    public async Task<IReadOnlyList<GitHubRepository>> ListInstallationRepositoriesAsync(string token, long installationId, CancellationToken ct)
    {
        List<GitHubRepository> repositories = [];
        for (int page = 1; page <= MaxPages; page++)
        {
            string path = string.Create(CultureInfo.InvariantCulture, $"user/installations/{installationId}/repositories?per_page={PageSize}&page={page}");
            using HttpResponseMessage response = await SendAsync(HttpMethod.Get, path, token, content: null, ct).ConfigureAwait(false);
            GitHubWire.RepositoryPage found = await ReadAsync(response, GitHubJsonContext.Default.RepositoryPage, ct).ConfigureAwait(false);
            repositories.AddRange(found.Repositories.Select(ToRepository));
            if (found.Repositories.Count < PageSize)
            {
                break;
            }
        }

        return repositories;
    }

    /// <summary>A repository the token can see; <see langword="null"/> when it can't.</summary>
    public async Task<GitHubRepository?> GetRepositoryAsync(string token, string owner, string name, CancellationToken ct)
    {
        using HttpResponseMessage response = await SendAsync(HttpMethod.Get, RepositoryPath(owner, name), token, content: null, ct).ConfigureAwait(false);
        if (response.StatusCode == HttpStatusCode.NotFound)
        {
            return null;
        }

        GitHubWire.Repository repository = await ReadAsync(response, GitHubJsonContext.Default.Repository, ct).ConfigureAwait(false);
        return ToRepository(repository);
    }

    /// <summary>The open pull request from a branch of the repository, if there is one.</summary>
    public async Task<GitHubPullRequest?> FindOpenPullRequestAsync(string token, string owner, string name, string head, CancellationToken ct)
    {
        string path = RepositoryPath(owner, name) + "/pulls?state=open&head=" + Uri.EscapeDataString(owner + ":" + head);
        using HttpResponseMessage response = await SendAsync(HttpMethod.Get, path, token, content: null, ct).ConfigureAwait(false);
        IReadOnlyList<GitHubWire.PullRequest> pulls = await ReadAsync(response, GitHubJsonContext.Default.IReadOnlyListPullRequest, ct).ConfigureAwait(false);
        return pulls.Count == 0 ? null : new GitHubPullRequest(pulls[0].Number, new Uri(pulls[0].HtmlUrl));
    }

    /// <summary>
    /// Opens a pull request. Fails with <c>github.unprocessable</c> and GitHub's words when it refuses
    /// one, such as when the branch has no commits the base lacks.
    /// </summary>
    public async Task<Result<GitHubPullRequest>> CreatePullRequestAsync(string token, string owner, string name, GitHubNewPullRequest pullRequest, CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(pullRequest);
        GitHubWire.NewPullRequest body = new GitHubWire.NewPullRequest(pullRequest.Title, pullRequest.Head, pullRequest.Base, pullRequest.Body);
        using JsonContent content = JsonContent.Create(body, GitHubJsonContext.Default.NewPullRequest);
        using HttpResponseMessage response = await SendAsync(HttpMethod.Post, RepositoryPath(owner, name) + "/pulls", token, content, ct).ConfigureAwait(false);
        if (response.StatusCode == HttpStatusCode.UnprocessableEntity)
        {
            GitHubWire.Problem? problem = await response.Content.ReadFromJsonAsync(GitHubJsonContext.Default.Problem, ct).ConfigureAwait(false);
            string why = problem?.Errors?.Select(error => error.Message).FirstOrDefault(message => message is not null) ?? problem?.Message ?? "GitHub refused the pull request.";
            return new Result<GitHubPullRequest>(Error.Conflict("github.unprocessable", why));
        }

        GitHubWire.PullRequest created = await ReadAsync(response, GitHubJsonContext.Default.PullRequest, ct).ConfigureAwait(false);
        return new Result<GitHubPullRequest>(new GitHubPullRequest(created.Number, new Uri(created.HtmlUrl)));
    }

    /// <inheritdoc />
    public void Dispose()
    {
        _http.Dispose();
    }

    private static string RepositoryPath(string owner, string name)
    {
        return "repos/" + Uri.EscapeDataString(owner) + "/" + Uri.EscapeDataString(name);
    }

    private static GitHubRepository ToRepository(GitHubWire.Repository repository)
    {
        return new GitHubRepository(repository.Owner.Login, repository.Name, repository.Private, repository.DefaultBranch, new Uri(repository.CloneUrl), new Uri(repository.HtmlUrl));
    }

    private static GitHubUserTokens ToTokens(GitHubWire.TokenResponse tokens)
    {
        TimeSpan? expiresIn = tokens.ExpiresIn is int seconds ? TimeSpan.FromSeconds(seconds) : null;
        TimeSpan? refreshExpiresIn = tokens.RefreshTokenExpiresIn is int refreshSeconds ? TimeSpan.FromSeconds(refreshSeconds) : null;
        return new GitHubUserTokens(tokens.AccessToken!, expiresIn, tokens.RefreshToken, refreshExpiresIn);
    }

    private static async Task<T> ReadAsync<T>(HttpResponseMessage response, JsonTypeInfo<T> type, CancellationToken ct)
    {
        if (!response.IsSuccessStatusCode)
        {
            string message = string.Create(CultureInfo.InvariantCulture, $"GitHub answered {(int)response.StatusCode} to {response.RequestMessage?.Method} {response.RequestMessage?.RequestUri?.AbsolutePath}.");
            throw new HttpRequestException(message, inner: null, response.StatusCode);
        }

        T? value;
        try
        {
            value = await response.Content.ReadFromJsonAsync(type, ct).ConfigureAwait(false);
        }
        catch (JsonException exception)
        {
            throw new HttpRequestException("GitHub's answer isn't the JSON expected.", exception);
        }

        if (value is null)
        {
            throw new HttpRequestException("GitHub answered with nothing.");
        }

        return value;
    }

    private async Task<HttpResponseMessage> PostFormAsync(string path, Dictionary<string, string> form, CancellationToken ct)
    {
        using FormUrlEncodedContent content = new FormUrlEncodedContent(form);
        using HttpRequestMessage request = new HttpRequestMessage(HttpMethod.Post, new Uri(Settings.WebUrl, path)) { Content = content };
        request.Headers.Accept.Add(new MediaTypeWithQualityHeaderValue("application/json"));
        return await _http.SendAsync(request, ct).ConfigureAwait(false);
    }

    private async Task<HttpResponseMessage> SendAsync(HttpMethod method, string path, string token, HttpContent? content, CancellationToken ct)
    {
        using HttpRequestMessage request = new HttpRequestMessage(method, new Uri(Settings.ApiUrl, path)) { Content = content };
        request.Headers.Accept.Add(new MediaTypeWithQualityHeaderValue("application/vnd.github+json"));
        request.Headers.Add("X-GitHub-Api-Version", ApiVersion);
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token);
        return await _http.SendAsync(request, ct).ConfigureAwait(false);
    }
}
