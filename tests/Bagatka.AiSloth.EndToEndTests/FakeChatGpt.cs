using System;
using System.Buffers.Text;
using System.Collections.Generic;
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
/// OpenAI's authorization server for Codex's own sign-in, as <c>codex login --device-auth</c> uses it,
/// at the HTTP boundary: a code the person enters, the authorization code it turns into, and tokens
/// for Codex's OAuth client whose access token is due for renewal at once, so every use renews it and
/// its refresh token works only once.
/// </summary>
internal sealed class FakeChatGpt : IAsyncDisposable
{
    /// <summary>Codex's OAuth client at OpenAI.</summary>
    public const string CodexClientId = "app_EMoamEEZ73f0CkXaXp7hrann";

    private readonly WebApplication _app;
    private readonly Lock _lock = new Lock();
    private readonly Dictionary<string, CodexDevice> _codexDevices = new Dictionary<string, CodexDevice>(StringComparer.Ordinal);
    private readonly Dictionary<string, (string Subject, string Verifier)> _codexCodes = new Dictionary<string, (string, string)>(StringComparer.Ordinal);
    private readonly Dictionary<string, string> _codexRefreshTokens = new Dictionary<string, string>(StringComparer.Ordinal);
    private readonly HashSet<string> _usedCodexRefreshTokens = new HashSet<string>(StringComparer.Ordinal);

    private FakeChatGpt(WebApplication app)
    {
        _app = app;
    }

    /// <summary>Where it runs, such as <c>http://127.0.0.1:41236/</c>.</summary>
    public Uri Url { get; private set; } = new Uri("http://localhost");

    private string Issuer => Url.AbsoluteUri.TrimEnd('/');

    public static async Task<FakeChatGpt> StartAsync()
    {
        WebApplicationBuilder builder = WebApplication.CreateSlimBuilder();
        builder.Logging.ClearProviders();
        builder.WebHost.ConfigureKestrel(kestrel => kestrel.Listen(IPAddress.Loopback, 0));
        WebApplication app = builder.Build();
        FakeChatGpt chatGpt = new FakeChatGpt(app);
        app.MapPost("/api/accounts/deviceauth/usercode", chatGpt.CodexUserCodeAsync);
        app.MapPost("/api/accounts/deviceauth/token", chatGpt.CodexApprovalAsync);
        app.MapPost("/oauth/token", chatGpt.CodexTokenAsync);
        await app.StartAsync();
        IServerAddressesFeature? addresses = app.Services.GetRequiredService<IServer>().Features.Get<IServerAddressesFeature>();
        if (addresses is null)
        {
            throw new InvalidOperationException("Kestrel reported no addresses.");
        }

        chatGpt.Url = new Uri(addresses.Addresses.Single());
        return chatGpt;
    }

    public async ValueTask DisposeAsync()
    {
        await _app.DisposeAsync();
    }

    /// <summary>How many times Codex's sign-in was renewed.</summary>
    public int CodexRenewals { get; private set; }

    /// <summary>How many renewals came with a refresh token already used: renewals that raced.</summary>
    public int CodexReusedRefreshTokens { get; private set; }

    /// <summary>The person signed in as <paramref name="subject"/> enters Codex's <paramref name="userCode"/> at OpenAI and approves.</summary>
    public void ApproveCodex(string userCode, string subject)
    {
        lock (_lock)
        {
            foreach (CodexDevice device in _codexDevices.Values)
            {
                if (string.Equals(device.UserCode, userCode, StringComparison.Ordinal))
                {
                    device.ApprovedBy = subject;
                }
            }
        }
    }

    /// <summary>The ChatGPT account Codex's sign-in for <paramref name="subject"/> uses.</summary>
    public static string CodexAccountOf(string subject)
    {
        return "account-" + subject;
    }

    private async Task<IResult> CodexUserCodeAsync(HttpRequest request)
    {
        JsonNode? body = await JsonNode.ParseAsync(request.Body, cancellationToken: request.HttpContext.RequestAborted);
        if (!string.Equals((string?)body?["client_id"], CodexClientId, StringComparison.Ordinal))
        {
            return Results.StatusCode(StatusCodes.Status400BadRequest);
        }

        string id = "device-" + RandomValue()[..12];
        string code = RandomValue()[..4].ToUpperInvariant() + "-" + RandomValue()[..5].ToUpperInvariant();
        lock (_lock)
        {
            _codexDevices[id] = new CodexDevice(code);
        }

        // OpenAI gives the interval as a string, which Codex reads.
        return Results.Json(new JsonObject { ["device_auth_id"] = id, ["user_code"] = code, ["interval"] = "1" });
    }

    // Not approved yet is a 403, which Codex keeps waiting on.
    private async Task<IResult> CodexApprovalAsync(HttpRequest request)
    {
        JsonNode? body = await JsonNode.ParseAsync(request.Body, cancellationToken: request.HttpContext.RequestAborted);
        string id = (string?)body?["device_auth_id"] ?? string.Empty;
        lock (_lock)
        {
            CodexDevice? device = _codexDevices.GetValueOrDefault(id);
            if (device?.ApprovedBy is null || !string.Equals(device.UserCode, (string?)body?["user_code"], StringComparison.Ordinal))
            {
                return Results.StatusCode(StatusCodes.Status403Forbidden);
            }

            _codexDevices.Remove(id);
            string code = "codex-code-" + RandomValue()[..12];
            string verifier = RandomValue();
            _codexCodes[code] = (device.ApprovedBy, verifier);
            return Results.Json(new JsonObject { ["authorization_code"] = code, ["code_challenge"] = "unused", ["code_verifier"] = verifier });
        }
    }

    // The code exchange comes as a form, a renewal as JSON, as Codex sends them.
    private async Task<IResult> CodexTokenAsync(HttpRequest request)
    {
        if (request.HasFormContentType)
        {
            IFormCollection form = await request.ReadFormAsync(request.HttpContext.RequestAborted);
            lock (_lock)
            {
                bool known = _codexCodes.Remove(form["code"].ToString(), out (string Subject, string Verifier) issued);
                bool genuine = known
                    && string.Equals(form["grant_type"], "authorization_code", StringComparison.Ordinal)
                    && string.Equals(form["client_id"], CodexClientId, StringComparison.Ordinal)
                    && string.Equals(form["code_verifier"], issued.Verifier, StringComparison.Ordinal)
                    && string.Equals(form["redirect_uri"], Issuer + "/deviceauth/callback", StringComparison.Ordinal);
                return genuine ? CodexTokens(issued.Subject, withIdToken: true) : Refused("invalid_grant");
            }
        }

        JsonNode? body = await JsonNode.ParseAsync(request.Body, cancellationToken: request.HttpContext.RequestAborted);
        string refreshToken = (string?)body?["refresh_token"] ?? string.Empty;
        lock (_lock)
        {
            if (_usedCodexRefreshTokens.Contains(refreshToken))
            {
                CodexReusedRefreshTokens++;
                return Refused("refresh_token_reused");
            }

            bool issued = _codexRefreshTokens.Remove(refreshToken, out string? subject);
            bool genuine = issued
                && string.Equals((string?)body?["grant_type"], "refresh_token", StringComparison.Ordinal)
                && string.Equals((string?)body?["client_id"], CodexClientId, StringComparison.Ordinal);
            if (!genuine)
            {
                return Refused("invalid_grant");
            }

            _usedCodexRefreshTokens.Add(refreshToken);
            CodexRenewals++;
            return CodexTokens(subject!, withIdToken: false);
        }
    }

    // Codex's tokens say when they expire in the access token, which lasts a minute here: due for
    // renewal at once. Callers hold the lock.
    private IResult CodexTokens(string subject, bool withIdToken)
    {
        long now = TimeProvider.System.GetUtcNow().ToUnixTimeSeconds();
        JsonObject auth = new JsonObject { ["chatgpt_account_id"] = CodexAccountOf(subject), ["chatgpt_plan_type"] = "plus" };
        string access = Unsigned(new JsonObject { ["client_id"] = CodexClientId, ["exp"] = now + 60, ["https://api.openai.com/auth"] = auth.DeepClone() });
        string refresh = "codex-refresh-" + RandomValue();
        _codexRefreshTokens[refresh] = subject;
        JsonObject body = new JsonObject { ["access_token"] = access, ["refresh_token"] = refresh };
        if (withIdToken)
        {
            body["id_token"] = Unsigned(new JsonObject
            {
                ["iss"] = Issuer,
                ["sub"] = subject,
                ["aud"] = CodexClientId,
                ["iat"] = now,
                ["exp"] = now + 3600,
                ["email"] = subject + "@example.com",
                ["https://api.openai.com/auth"] = auth,
            });
        }

        return Results.Text(body.ToJsonString(), "application/json");
    }

    private static string Unsigned(JsonObject payload)
    {
        return Base64Url.EncodeToString("""{"alg":"none","typ":"JWT"}"""u8) + "."
            + Base64Url.EncodeToString(Encoding.UTF8.GetBytes(payload.ToJsonString())) + ".unsigned";
    }

    private static IResult Refused(string error)
    {
        return Results.Json(new JsonObject { ["error"] = error, ["error_description"] = "Refused by the fake: " + error }, statusCode: StatusCodes.Status400BadRequest);
    }

    private static string RandomValue()
    {
        return Base64Url.EncodeToString(RandomNumberGenerator.GetBytes(24));
    }

    private sealed class CodexDevice(string userCode)
    {
        public string UserCode { get; } = userCode;

        public string? ApprovedBy { get; set; }
    }
}
