using System;
using System.Collections.Generic;
using System.Globalization;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;

namespace Bagatka.Sdk.OpenAI;

/// <summary>
/// A ChatGPT plan's usage windows, as Codex's own <c>/status</c> reads them from ChatGPT
/// (<c>GET /wham/usage</c> at <c>https://chatgpt.com/backend-api</c>, undocumented): the short window
/// and the long one, each with the percent used. Never retried. An unexpected answer throws
/// <see cref="HttpRequestException"/> with its status code. Thread-safe.
/// </summary>
public sealed class ChatGptUsageClient : IDisposable
{
    private readonly HttpClient _http;

    /// <summary>Creates a client.</summary>
    public ChatGptUsageClient()
    {
        SocketsHttpHandler handler = new SocketsHttpHandler { PooledConnectionLifetime = TimeSpan.FromMinutes(5) };
        _http = new HttpClient(handler, disposeHandler: true) { Timeout = TimeSpan.FromSeconds(30) };
        _http.DefaultRequestHeaders.UserAgent.Add(new ProductInfoHeaderValue("Bagatka.Sdk.OpenAI", "1.0"));
    }

    /// <summary>
    /// The windows of the plan that <paramref name="accessToken"/> uses, from ChatGPT's backend at
    /// <paramref name="backendUrl"/>, for the ChatGPT account the sign-in chose
    /// (<see cref="ChatGptTokens.AccountId"/>) when known. The token is never logged.
    /// </summary>
    public async Task<IReadOnlyList<ChatGptUsageWindow>> GetWindowsAsync(Uri backendUrl, string accessToken, string? accountId, CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(backendUrl);
        ArgumentException.ThrowIfNullOrEmpty(accessToken);
        using HttpRequestMessage request = new HttpRequestMessage(HttpMethod.Get, new Uri(backendUrl.AbsoluteUri.TrimEnd('/') + "/wham/usage"));
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", accessToken);
        if (accountId is not null)
        {
            request.Headers.TryAddWithoutValidation("ChatGPT-Account-Id", accountId);
        }

        using HttpResponseMessage response = await _http.SendAsync(request, ct).ConfigureAwait(false);
        if (!response.IsSuccessStatusCode)
        {
            string message = string.Create(CultureInfo.InvariantCulture, $"ChatGPT answered {(int)response.StatusCode} to GET /wham/usage.");
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
            throw new HttpRequestException("ChatGPT's answer isn't the JSON expected.", exception);
        }

        using (document)
        {
            JsonElement limits = Property(document.RootElement, "rate_limit");
            List<ChatGptUsageWindow> windows = [];
            foreach (string name in (string[])["primary_window", "secondary_window"])
            {
                JsonElement window = Property(limits, name);
                if (window.ValueKind == JsonValueKind.Object && Number(window, "used_percent") is decimal used)
                {
                    windows.Add(new ChatGptUsageWindow(Seconds(Number(window, "limit_window_seconds")), used, ResetOf(window)));
                }
            }

            return windows;
        }
    }

    /// <inheritdoc />
    public void Dispose()
    {
        _http.Dispose();
    }

    // When the window starts over, as a Unix time.
    private static DateTimeOffset? ResetOf(JsonElement window)
    {
        return Number(window, "reset_at") is decimal at && at > 0 ? DateTimeOffset.FromUnixTimeSeconds((long)at) : null;
    }

    private static TimeSpan? Seconds(decimal? seconds)
    {
        return seconds is decimal value && value > 0 ? TimeSpan.FromSeconds((double)value) : null;
    }

    private static decimal? Number(JsonElement holder, string name)
    {
        JsonElement value = Property(holder, name);
        return value.ValueKind == JsonValueKind.Number ? value.GetDecimal() : null;
    }

    private static JsonElement Property(JsonElement holder, string name)
    {
        if (holder.ValueKind != JsonValueKind.Object)
        {
            return default;
        }

        bool present = holder.TryGetProperty(name, out JsonElement property);
        return present ? property : default;
    }
}
