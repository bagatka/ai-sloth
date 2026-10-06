using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Net;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json.Nodes;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Hosting.Server;
using Microsoft.AspNetCore.Hosting.Server.Features;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;

namespace Bagatka.AiSloth.EndToEndTests;

/// <summary>
/// GitHub at the HTTP boundary, as a GitHub App acting for people sees it: the device flow signs in
/// whoever approves a code, their tokens reach the repositories they collaborate on, pull requests
/// open, and git clones and pushes over HTTP through the real <c>git http-backend</c>, with the
/// token as the password. The site is at its root, the REST API under <c>/api/</c>.
/// </summary>
internal sealed class FakeGitHub : IAsyncDisposable
{
    /// <summary>The app's client ID, client secret, and slug, as the host is configured.</summary>
    public const string ClientId = "Iv23e2e";

    public const string ClientSecret = "e2e-github-secret";

    public const string AppSlug = "aisloth-e2e";

    /// <summary>The app's bot, which commits credit as their co-author.</summary>
    public const long BotId = 4242;

    private readonly WebApplication _app;
    private readonly DirectoryInfo _root = Directory.CreateTempSubdirectory("fake-github-");
    private readonly Lock _lock = new Lock();
    private readonly Dictionary<string, DeviceCode> _devices = new Dictionary<string, DeviceCode>(StringComparer.Ordinal);
    private readonly Dictionary<string, string> _tokens = new Dictionary<string, string>(StringComparer.Ordinal);
    private readonly Dictionary<string, HashSet<string>> _collaborators = new Dictionary<string, HashSet<string>>(StringComparer.Ordinal);
    private readonly List<PullRequest> _pullRequests = [];

    private FakeGitHub(WebApplication app)
    {
        _app = app;
    }

    /// <summary>Where it runs, such as <c>http://127.0.0.1:41236/</c>.</summary>
    public Uri Url { get; private set; } = new Uri("http://localhost");

    /// <summary>The REST API.</summary>
    public Uri ApiUrl => new Uri(Url, "api/");

    public static async Task<FakeGitHub> StartAsync()
    {
        WebApplicationBuilder builder = WebApplication.CreateSlimBuilder();
        builder.Logging.ClearProviders();
        builder.WebHost.ConfigureKestrel(kestrel => kestrel.Listen(IPAddress.Loopback, 0));
        WebApplication app = builder.Build();
        FakeGitHub gitHub = new FakeGitHub(app);
        app.MapPost("/login/device/code", gitHub.StartDeviceAsync);
        app.MapGet("/login/device", gitHub.ApproveInBrowser);
        app.MapPost("/login/oauth/access_token", gitHub.TokenAsync);
        app.MapGet("/api/user", gitHub.User);
        app.MapGet("/api/users/{login}", FakeGitHub.Account);
        app.MapGet("/api/user/installations", gitHub.Installations);
        app.MapGet("/api/user/installations/{id:long}/repositories", gitHub.InstallationRepositories);
        app.MapGet("/api/repos/{owner}/{name}", gitHub.Repository);
        app.MapGet("/api/repos/{owner}/{name}/pulls", gitHub.ListPullRequests);
        app.MapPost("/api/repos/{owner}/{name}/pulls", gitHub.OpenPullRequestAsync);
        app.Map("/git/{**path}", gitHub.ServeGitAsync);
        await app.StartAsync();
        IServerAddressesFeature? addresses = app.Services.GetRequiredService<IServer>().Features.Get<IServerAddressesFeature>();
        if (addresses is null)
        {
            throw new InvalidOperationException("Kestrel reported no addresses.");
        }

        gitHub.Url = new Uri(addresses.Addresses.Single() + "/");
        return gitHub;
    }

    /// <summary>
    /// A repository on <c>main</c> with one commit adding README.md, which only the collaborators reach,
    /// through an installation of the app on its owner.
    /// </summary>
    public async Task CreateRepositoryAsync(string owner, string name, params string[] collaborators)
    {
        string bare = BarePath(owner, name);
        string work = Path.Combine(_root.FullName, "work-" + owner + "-" + name);
        await GitAsync(_root.FullName, "init", "--quiet", "--bare", "--initial-branch=main", bare);
        await GitAsync(_root.FullName, "init", "--quiet", "--initial-branch=main", work);
        await File.WriteAllTextAsync(Path.Combine(work, "README.md"), "hello from " + name + "\n");
        await GitAsync(work, "add", "README.md");
        await GitAsync(work, "-c", "user.name=Founder", "-c", "user.email=founder@example.com", "commit", "--quiet", "-m", "First commit");
        await GitAsync(work, "push", "--quiet", bare, "main");
        lock (_lock)
        {
            _collaborators[owner + "/" + name] = new HashSet<string>(collaborators, StringComparer.Ordinal);
        }
    }

    /// <summary>Someone else's commit on a branch, as when a teammate pushed to it first.</summary>
    public async Task CommitElsewhereAsync(string owner, string name, string branch)
    {
        string work = Path.Combine(_root.FullName, "elsewhere-" + Guid.CreateVersion7().ToString("N", CultureInfo.InvariantCulture));
        await GitAsync(_root.FullName, "clone", "--quiet", BarePath(owner, name), work);
        await File.WriteAllTextAsync(Path.Combine(work, "THEIRS.md"), "theirs\n");
        await GitAsync(work, "checkout", "--quiet", "-b", branch);
        await GitAsync(work, "add", "THEIRS.md");
        await GitAsync(work, "-c", "user.name=Teammate", "-c", "user.email=teammate@example.com", "commit", "--quiet", "-m", "Their commit");
        await GitAsync(work, "push", "--quiet", "origin", branch);
    }

    /// <summary>A commit adding or changing files on an existing branch, as when someone pushed to it.</summary>
    public async Task CommitFilesAsync(string owner, string name, string branch, IReadOnlyDictionary<string, string> files, string message)
    {
        string work = Path.Combine(_root.FullName, "elsewhere-" + Guid.CreateVersion7().ToString("N", CultureInfo.InvariantCulture));
        await GitAsync(_root.FullName, "clone", "--quiet", "--branch", branch, BarePath(owner, name), work);
        foreach (KeyValuePair<string, string> file in files)
        {
            string path = Path.Combine(work, file.Key);
            Directory.CreateDirectory(Path.GetDirectoryName(path)!);
            await File.WriteAllTextAsync(path, file.Value);
            await GitAsync(work, "add", file.Key);
        }

        await GitAsync(work, "-c", "user.name=Teammate", "-c", "user.email=teammate@example.com", "commit", "--quiet", "-m", message);
        await GitAsync(work, "push", "--quiet", "origin", branch);
    }

    /// <summary>The person signs in at GitHub and approves the code.</summary>
    public void Approve(string userCode, string login)
    {
        lock (_lock)
        {
            DeviceCode device = _devices.Values.Single(found => string.Equals(found.UserCode, userCode, StringComparison.Ordinal));
            device.ApprovedBy = login;
        }
    }

    /// <summary>What git prints for a command in a repository, such as <c>show branch:file</c>; null when git fails.</summary>
    public async Task<string?> ReadAsync(string owner, string name, params string[] command)
    {
        (int exitCode, string output) = await RunGitAsync(BarePath(owner, name), command);
        return exitCode == 0 ? output : null;
    }

    /// <summary>The pull requests opened on a repository, oldest first.</summary>
    public IReadOnlyList<PullRequest> PullRequests(string owner, string name)
    {
        lock (_lock)
        {
            return [.. _pullRequests.Where(pull => string.Equals(pull.Repository, owner + "/" + name, StringComparison.Ordinal))];
        }
    }

    public async ValueTask DisposeAsync()
    {
        await _app.DisposeAsync();
        _root.Delete(recursive: true);
    }

    private string BarePath(string owner, string name)
    {
        return Path.Combine(_root.FullName, owner, name + ".git");
    }

    private static string Random(string prefix)
    {
        return prefix + Convert.ToHexString(RandomNumberGenerator.GetBytes(12));
    }

    private static long IdOf(string login)
    {
        return BitConverter.ToUInt32(SHA256.HashData(Encoding.UTF8.GetBytes(login))) % 1_000_000;
    }

    private async Task<IResult> StartDeviceAsync(HttpRequest request)
    {
        IFormCollection form = await request.ReadFormAsync(request.HttpContext.RequestAborted);
        if (!string.Equals(form["client_id"], ClientId, StringComparison.Ordinal))
        {
            return Results.Json(new JsonObject { ["error"] = "unauthorized_client" });
        }

        string userCode = Convert.ToHexString(RandomNumberGenerator.GetBytes(4));
        DeviceCode device = new DeviceCode(Random("device-"), userCode);
        lock (_lock)
        {
            _devices[device.Code] = device;
        }

        return Results.Json(new JsonObject
        {
            ["device_code"] = device.Code,
            ["user_code"] = userCode,
            ["verification_uri"] = new Uri(Url, "login/device?code=" + userCode).AbsoluteUri,
            ["expires_in"] = 900,
            ["interval"] = 0,
        });
    }

    // A browser at the verification page, signed in as whoever login_hint names.
    private IResult ApproveInBrowser(HttpRequest request)
    {
        string? login = request.Query["login_hint"];
        if (login is not null)
        {
            Approve(request.Query["code"].ToString(), login);
        }

        return Results.Text("Approved.");
    }

    private async Task<IResult> TokenAsync(HttpRequest request)
    {
        IFormCollection form = await request.ReadFormAsync(request.HttpContext.RequestAborted);
        string? login;
        lock (_lock)
        {
            if (string.Equals(form["grant_type"], "refresh_token", StringComparison.Ordinal))
            {
                login = string.Equals(form["client_secret"], ClientSecret, StringComparison.Ordinal) ? _tokens.GetValueOrDefault(form["refresh_token"].ToString()) : null;
                _tokens.Remove(form["refresh_token"].ToString());
            }
            else
            {
                DeviceCode? device = _devices.GetValueOrDefault(form["device_code"].ToString());
                if (device?.ApprovedBy is null)
                {
                    return Results.Json(new JsonObject { ["error"] = device is null ? "incorrect_device_code" : "authorization_pending" });
                }

                login = device.ApprovedBy;
                _devices.Remove(device.Code);
            }
        }

        return login is null ? Results.Json(new JsonObject { ["error"] = "bad_refresh_token" }) : Results.Json(IssueTokens(login));
    }

    private JsonObject IssueTokens(string login)
    {
        string access = Random("ghu_");
        string refresh = Random("ghr_");
        lock (_lock)
        {
            _tokens[access] = login;
            _tokens[refresh] = login;
        }

        return new JsonObject { ["access_token"] = access, ["expires_in"] = 28800, ["refresh_token"] = refresh, ["refresh_token_expires_in"] = 15897600, ["token_type"] = "bearer" };
    }

    // Whom the request's token acts as, from Authorization: Bearer for the API or Basic for git.
    private string? Login(HttpRequest request)
    {
        string authorization = request.Headers.Authorization.ToString();
        string? token = authorization.StartsWith("Bearer ", StringComparison.Ordinal) ? authorization["Bearer ".Length..] : null;
        if (authorization.StartsWith("Basic ", StringComparison.Ordinal))
        {
            string[] pair = Encoding.UTF8.GetString(Convert.FromBase64String(authorization["Basic ".Length..])).Split(':', 2);
            token = pair.Length == 2 ? pair[1] : null;
        }

        lock (_lock)
        {
            return token is null ? null : _tokens.GetValueOrDefault(token);
        }
    }

    private bool Reaches(string? login, string fullName)
    {
        lock (_lock)
        {
            return login is not null && _collaborators.GetValueOrDefault(fullName)?.Contains(login) == true;
        }
    }

    private IResult User(HttpRequest request)
    {
        string? login = Login(request);
        return login is null ? Results.Unauthorized() : Results.Json(new JsonObject { ["id"] = IdOf(login), ["login"] = login, ["name"] = "The " + login });
    }

    private static IResult Account(string login)
    {
        return string.Equals(login, AppSlug + "[bot]", StringComparison.Ordinal)
            ? Results.Json(new JsonObject { ["id"] = BotId, ["login"] = login, ["name"] = null })
            : Results.NotFound();
    }

    private IResult Installations(HttpRequest request)
    {
        string? login = Login(request);
        List<string> owners;
        lock (_lock)
        {
            owners = [.. _collaborators.Where(entry => login is not null && entry.Value.Contains(login)).Select(entry => entry.Key.Split('/')[0]).Distinct(StringComparer.Ordinal)];
        }

        JsonArray installations = [.. owners.Select(owner => (JsonNode)new JsonObject { ["id"] = IdOf(owner), ["account"] = new JsonObject { ["login"] = owner } })];
        return login is null ? Results.Unauthorized() : Results.Json(new JsonObject { ["installations"] = installations });
    }

    private IResult InstallationRepositories(HttpRequest request, long id)
    {
        string? login = Login(request);
        List<string> names;
        lock (_lock)
        {
            names = [.. _collaborators.Keys.Where(fullName => IdOf(fullName.Split('/')[0]) == id && Reaches(login, fullName))];
        }

        JsonArray repositories = [.. names.Select(fullName => (JsonNode)RepositoryJson(fullName))];
        return login is null ? Results.Unauthorized() : Results.Json(new JsonObject { ["repositories"] = repositories });
    }

    private IResult Repository(HttpRequest request, string owner, string name)
    {
        return Reaches(Login(request), owner + "/" + name) ? Results.Json(RepositoryJson(owner + "/" + name)) : Results.NotFound();
    }

    private JsonObject RepositoryJson(string fullName)
    {
        string[] parts = fullName.Split('/');
        return new JsonObject
        {
            ["name"] = parts[1],
            ["full_name"] = fullName,
            ["owner"] = new JsonObject { ["login"] = parts[0] },
            ["private"] = true,
            ["default_branch"] = "main",
            ["clone_url"] = new Uri(Url, "git/" + fullName + ".git").AbsoluteUri,
            ["html_url"] = new Uri(Url, fullName).AbsoluteUri,
        };
    }

    private IResult ListPullRequests(HttpRequest request, string owner, string name)
    {
        string head = request.Query["head"].ToString();
        IEnumerable<PullRequest> open = Reaches(Login(request), owner + "/" + name)
            ? PullRequests(owner, name).Where(pull => string.Equals(owner + ":" + pull.Head, head, StringComparison.Ordinal))
            : [];
        return Results.Json(new JsonArray([.. open.Select(pull => (JsonNode)new JsonObject { ["number"] = pull.Number, ["html_url"] = pull.Url.AbsoluteUri })]));
    }

    private async Task<IResult> OpenPullRequestAsync(HttpRequest request, string owner, string name)
    {
        string? login = Login(request);
        JsonNode? body = await JsonNode.ParseAsync(request.Body, cancellationToken: request.HttpContext.RequestAborted);
        string head = body?["head"]?.GetValue<string>() ?? string.Empty;
        string? branch = await ReadAsync(owner, name, "rev-parse", "--verify", "refs/heads/" + head);
        if (!Reaches(login, owner + "/" + name) || branch is null)
        {
            return Results.Json(new JsonObject { ["message"] = "Validation Failed", ["errors"] = new JsonArray(new JsonObject { ["message"] = "No branch " + head }) }, statusCode: 422);
        }

        PullRequest pull;
        lock (_lock)
        {
            int number = _pullRequests.Count(other => string.Equals(other.Repository, owner + "/" + name, StringComparison.Ordinal)) + 1;
            pull = new PullRequest(owner + "/" + name, number, head, body?["base"]?.GetValue<string>() ?? string.Empty, body?["title"]?.GetValue<string>() ?? string.Empty, login!, new Uri(Url, owner + "/" + name + "/pull/" + number.ToString(CultureInfo.InvariantCulture)));
            _pullRequests.Add(pull);
        }

        return Results.Json(new JsonObject { ["number"] = pull.Number, ["html_url"] = pull.Url.AbsoluteUri }, statusCode: 201);
    }

    // Git's smart HTTP, served by git http-backend as CGI, for whoever the token reaches.
    private async Task ServeGitAsync(HttpContext context)
    {
        string path = "/" + context.Request.RouteValues["path"];
        string[] parts = path.Split('/', StringSplitOptions.RemoveEmptyEntries);
        string? login = Login(context.Request);
        if (parts.Length < 2 || !Reaches(login, parts[0] + "/" + parts[1].Replace(".git", string.Empty, StringComparison.Ordinal)))
        {
            context.Response.StatusCode = 401;
            context.Response.Headers.WWWAuthenticate = "Basic realm=\"GitHub\"";
            return;
        }

        ProcessStartInfo start = new ProcessStartInfo("git", ["http-backend"]) { RedirectStandardInput = true, RedirectStandardOutput = true, UseShellExecute = false };
        start.Environment["GIT_PROJECT_ROOT"] = _root.FullName;
        start.Environment["GIT_HTTP_EXPORT_ALL"] = "1";
        start.Environment["PATH_INFO"] = path;
        start.Environment["REQUEST_METHOD"] = context.Request.Method;
        start.Environment["QUERY_STRING"] = context.Request.QueryString.Value?.TrimStart('?') ?? string.Empty;
        start.Environment["CONTENT_TYPE"] = context.Request.ContentType ?? string.Empty;
        start.Environment["REMOTE_USER"] = login;
        start.Environment["HTTP_CONTENT_ENCODING"] = context.Request.Headers.ContentEncoding.ToString();
        start.Environment["GIT_PROTOCOL"] = context.Request.Headers["Git-Protocol"].ToString();
        using Process git = Started(start);
        await context.Request.Body.CopyToAsync(git.StandardInput.BaseStream, context.RequestAborted);
        git.StandardInput.Close();
        using MemoryStream answer = new MemoryStream();
        await git.StandardOutput.BaseStream.CopyToAsync(answer, context.RequestAborted);
        await git.WaitForExitAsync(context.RequestAborted);
        await WriteCgiAsync(answer.ToArray(), context.Response);
    }

    // A CGI answer: headers, a blank line, then the body.
    private static async Task WriteCgiAsync(byte[] answer, HttpResponse response)
    {
        int split = answer.AsSpan().IndexOf("\r\n\r\n"u8);
        int bodyStart = split + 4;
        if (split < 0)
        {
            split = answer.AsSpan().IndexOf("\n\n"u8);
            bodyStart = split + 2;
        }

        foreach (string header in Encoding.ASCII.GetString(answer, 0, split).Split('\n', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))
        {
            string[] pair = header.Split(':', 2, StringSplitOptions.TrimEntries);
            if (string.Equals(pair[0], "Status", StringComparison.OrdinalIgnoreCase))
            {
                response.StatusCode = int.Parse(pair[1].Split(' ')[0], CultureInfo.InvariantCulture);
            }
            else
            {
                response.Headers[pair[0]] = pair[1];
            }
        }

        await response.Body.WriteAsync(answer.AsMemory(bodyStart));
    }

    private static async Task GitAsync(string directory, params string[] arguments)
    {
        (int exitCode, string output) = await RunGitAsync(directory, arguments);
        if (exitCode != 0)
        {
            throw new InvalidOperationException("git " + string.Join(' ', arguments) + " failed: " + output);
        }
    }

    private static async Task<(int ExitCode, string Output)> RunGitAsync(string directory, string[] arguments)
    {
        ProcessStartInfo start = new ProcessStartInfo("git", arguments) { WorkingDirectory = directory, RedirectStandardOutput = true, RedirectStandardError = true, UseShellExecute = false };
        start.Environment["GIT_CONFIG_GLOBAL"] = "/dev/null";
        using Process git = Started(start);
        Task<string> errors = git.StandardError.ReadToEndAsync(CancellationToken.None);
        string output = await git.StandardOutput.ReadToEndAsync(CancellationToken.None);
        await git.WaitForExitAsync(CancellationToken.None);
        string complaints = await errors;
        return (git.ExitCode, git.ExitCode == 0 ? output.Trim() : complaints);
    }

    private static Process Started(ProcessStartInfo start)
    {
        Process? process = Process.Start(start);
        if (process is null)
        {
            throw new InvalidOperationException("git didn't start.");
        }

        return process;
    }

    /// <summary>A pull request someone opened.</summary>
    internal sealed record PullRequest(string Repository, int Number, string Head, string Base, string Title, string OpenedBy, Uri Url);

    private sealed class DeviceCode(string code, string userCode)
    {
        public string Code { get; } = code;

        public string UserCode { get; } = userCode;

        public string? ApprovedBy { get; set; }
    }
}
