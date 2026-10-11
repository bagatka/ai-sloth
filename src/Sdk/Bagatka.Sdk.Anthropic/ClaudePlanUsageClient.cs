using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;

namespace Bagatka.Sdk.Anthropic;

/// <summary>
/// A Claude plan's usage windows, as Claude Code's own <c>/usage</c> reads them (<c>GET /api/oauth/usage</c>,
/// undocumented) with the plan's sign-in token: the five-hour window and the weekly ones, each with the
/// percent used. Anthropic answers in two shapes, both read: named windows (<c>five_hour</c>,
/// <c>seven_day</c>, <c>seven_day_opus</c>) and a list of limits by kind. Never retried. An unexpected
/// answer throws <see cref="HttpRequestException"/> with its status code. Thread-safe.
/// </summary>
public sealed class ClaudePlanUsageClient : IDisposable
{
    /// <summary>The beta Anthropic's API wants with a Claude plan's sign-in token.</summary>
    public const string OAuthBeta = "oauth-2025-04-20";

    private readonly HttpClient _http;

    /// <summary>Creates a client.</summary>
    public ClaudePlanUsageClient()
    {
        SocketsHttpHandler handler = new SocketsHttpHandler { PooledConnectionLifetime = TimeSpan.FromMinutes(5) };
        _http = new HttpClient(handler, disposeHandler: true) { Timeout = TimeSpan.FromSeconds(30) };
        _http.DefaultRequestHeaders.UserAgent.Add(new ProductInfoHeaderValue("Bagatka.Sdk.Anthropic", "1.0"));
    }

    /// <summary>
    /// The windows of the plan of <paramref name="token"/>, from Anthropic's API at
    /// <paramref name="apiUrl"/>, such as <c>https://api.anthropic.com</c>. The token is never logged.
    /// </summary>
    public async Task<IReadOnlyList<ClaudePlanWindow>> GetWindowsAsync(Uri apiUrl, string token, CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(apiUrl);
        ArgumentException.ThrowIfNullOrEmpty(token);
        using HttpRequestMessage request = new HttpRequestMessage(HttpMethod.Get, new Uri(apiUrl.AbsoluteUri.TrimEnd('/') + "/api/oauth/usage"));
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token);
        request.Headers.TryAddWithoutValidation("anthropic-beta", OAuthBeta);
        using HttpResponseMessage response = await _http.SendAsync(request, ct).ConfigureAwait(false);
        if (!response.IsSuccessStatusCode)
        {
            string message = string.Create(CultureInfo.InvariantCulture, $"Anthropic answered {(int)response.StatusCode} to GET /api/oauth/usage.");
            throw new HttpRequestException(message, inner: null, response.StatusCode);
        }

        string json = await response.Content.ReadAsStringAsync(ct).ConfigureAwait(false);
        JsonDocument document;
        try
        {
            document = JsonDocument.Parse(json);
        }
        catch (JsonException exception)
        {
            throw new HttpRequestException("Anthropic's answer isn't the JSON expected.", exception);
        }

        using (document)
        {
            return Windows(document.RootElement);
        }
    }

    /// <inheritdoc />
    public void Dispose()
    {
        _http.Dispose();
    }

    // The list of limits by kind when Anthropic gives one, the named windows otherwise.
    private static List<ClaudePlanWindow> Windows(JsonElement root)
    {
        if (root.ValueKind != JsonValueKind.Object)
        {
            return [];
        }

        JsonElement limits = Property(root, "limits");
        if (limits.ValueKind == JsonValueKind.Array)
        {
            return [.. limits.EnumerateArray().Select(Limit).OfType<ClaudePlanWindow>()];
        }

        (string Key, string Name)[] named = [("five_hour", "5 hours"), ("seven_day", "Week"), ("seven_day_opus", "Week, Opus"), ("seven_day_sonnet", "Week, Sonnet")];
        return [.. named.Select(window => Named(root, window.Key, window.Name)).OfType<ClaudePlanWindow>()];
    }

    private static ClaudePlanWindow? Named(JsonElement root, string key, string name)
    {
        JsonElement window = Property(root, key);
        return window.ValueKind == JsonValueKind.Object ? WindowOf(name, window, "utilization") : null;
    }

    // { "kind": "session" | "weekly_all" | "weekly_scoped", "scope"?: "opus", "percent", "resets_at" }
    private static ClaudePlanWindow? Limit(JsonElement limit)
    {
        if (limit.ValueKind != JsonValueKind.Object)
        {
            return null;
        }

        string? kind = Text(limit, "kind");
        string? scope = Text(limit, "scope");
        string? name = kind switch
        {
            "session" => "5 hours",
            "weekly_all" => "Week",
            "weekly_scoped" => scope is null ? "Week" : "Week, " + CultureInfo.InvariantCulture.TextInfo.ToTitleCase(scope),
            _ => null,
        };
        return name is null ? null : WindowOf(name, limit, "percent");
    }

    private static ClaudePlanWindow? WindowOf(string name, JsonElement window, string percentKey)
    {
        JsonElement percent = Property(window, percentKey);
        if (percent.ValueKind != JsonValueKind.Number)
        {
            return null;
        }

        bool dated = DateTimeOffset.TryParse(Text(window, "resets_at"), CultureInfo.InvariantCulture, DateTimeStyles.AssumeUniversal, out DateTimeOffset resetsAt);
        return new ClaudePlanWindow(name, percent.GetDecimal(), dated ? resetsAt : null);
    }

    private static string? Text(JsonElement holder, string name)
    {
        JsonElement text = Property(holder, name);
        return text.ValueKind == JsonValueKind.String ? text.GetString() : null;
    }

    // The property, or an undefined element when the holder lacks it.
    private static JsonElement Property(JsonElement holder, string name)
    {
        bool present = holder.TryGetProperty(name, out JsonElement property);
        return present ? property : default;
    }
}
