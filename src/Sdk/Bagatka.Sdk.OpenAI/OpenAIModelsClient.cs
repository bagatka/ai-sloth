using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;

namespace Bagatka.Sdk.OpenAI;

/// <summary>
/// The models an OpenAI API serves, as its <c>GET /models</c> lists them: OpenAI's own, or another
/// endpoint that speaks its API, with the caller's credentials, such as an API key or a ChatGPT plan's
/// access token. Calls are never retried, since they are cheap for the caller to repeat. An unexpected
/// answer throws <see cref="HttpRequestException"/> with its status code. Thread-safe.
/// </summary>
public sealed class OpenAIModelsClient : IDisposable
{
    private readonly HttpClient _http;

    /// <summary>Creates a client.</summary>
    public OpenAIModelsClient()
    {
        SocketsHttpHandler handler = new SocketsHttpHandler { PooledConnectionLifetime = TimeSpan.FromMinutes(5) };
        _http = new HttpClient(handler, disposeHandler: true) { Timeout = TimeSpan.FromSeconds(30) };
        _http.DefaultRequestHeaders.UserAgent.Add(new ProductInfoHeaderValue("Bagatka.Sdk.OpenAI", "1.0"));
    }

    /// <summary>
    /// The models the API at <paramref name="apiUrl"/> serves, such as <c>https://api.openai.com/v1</c>,
    /// asked with <paramref name="headers"/>, which hold the credentials and are never logged: every
    /// model the API serves for an API key, or, for a ChatGPT plan's token, the models of the plan's
    /// catalog meant for display, in its order.
    /// </summary>
    public async Task<IReadOnlyList<OpenAIModel>> ListModelsAsync(Uri apiUrl, IReadOnlyDictionary<string, string> headers, CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(apiUrl);
        return await ReadModelsAsync(new Uri(apiUrl.AbsoluteUri.TrimEnd('/') + "/models"), headers, ct).ConfigureAwait(false);
    }

    /// <summary>
    /// The models ChatGPT's Codex backend at <paramref name="codexUrl"/>, such as
    /// <c>https://chatgpt.com/backend-api/codex</c>, offers the plan of a Codex sign-in, as it lists
    /// them for Codex <paramref name="clientVersion"/>: those meant for display, in its order, with how
    /// hard each can reason and its context. Asked with <paramref name="headers"/>, never logged.
    /// </summary>
    public async Task<IReadOnlyList<OpenAIModel>> ListCodexModelsAsync(Uri codexUrl, IReadOnlyDictionary<string, string> headers, string clientVersion, CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(codexUrl);
        string url = codexUrl.AbsoluteUri.TrimEnd('/') + "/models?client_version=" + Uri.EscapeDataString(clientVersion);
        return await ReadModelsAsync(new Uri(url), headers, ct).ConfigureAwait(false);
    }

    private async Task<IReadOnlyList<OpenAIModel>> ReadModelsAsync(Uri url, IReadOnlyDictionary<string, string> headers, CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(headers);
        using HttpRequestMessage request = new HttpRequestMessage(HttpMethod.Get, url);
        foreach (KeyValuePair<string, string> header in headers)
        {
            request.Headers.TryAddWithoutValidation(header.Key, header.Value);
        }

        using HttpResponseMessage response = await _http.SendAsync(request, ct).ConfigureAwait(false);
        if (!response.IsSuccessStatusCode)
        {
            string message = string.Create(CultureInfo.InvariantCulture, $"The OpenAI API answered {(int)response.StatusCode} to GET /models.");
            throw new HttpRequestException(message, inner: null, response.StatusCode);
        }

        OpenAIWire.ModelList? list;
        try
        {
            list = await response.Content.ReadFromJsonAsync(OpenAIJsonContext.Default.ModelList, ct).ConfigureAwait(false);
        }
        catch (JsonException exception)
        {
            throw new HttpRequestException("The OpenAI API's answer isn't the JSON expected.", exception);
        }

        if (list?.Data is IReadOnlyList<OpenAIWire.Model> models)
        {
            return [.. models.Select(model => new OpenAIModel(model.Id, DisplayName: null, model.Created is long seconds ? DateTimeOffset.FromUnixTimeSeconds(seconds) : null, [], ContextWindow: null))];
        }

        if (list?.Models is IReadOnlyList<OpenAIWire.CatalogModel> catalog)
        {
            return [.. catalog
                .Where(model => model.Visibility is null or "list")
                .Select(model => new OpenAIModel(model.Slug, model.DisplayName ?? model.Slug, Created: null, Efforts(model), model.ContextWindow is long tokens and > 0 and <= int.MaxValue ? (int)tokens : null))];
        }

        throw new HttpRequestException("The OpenAI API's answer has no list of models.");
    }

    // A catalog's reasoning levels, each an effort or an object naming one.
    private static List<string> Efforts(OpenAIWire.CatalogModel model)
    {
        List<string> efforts = [];
        foreach (JsonElement level in model.SupportedReasoningLevels ?? [])
        {
            JsonElement effort = level;
            if (level.ValueKind == JsonValueKind.Object)
            {
                bool named = level.TryGetProperty("effort", out JsonElement value);
                effort = named ? value : default;
            }

            if (effort.ValueKind == JsonValueKind.String)
            {
                efforts.Add(effort.GetString()!);
            }
        }

        return efforts;
    }

    /// <inheritdoc />
    public void Dispose()
    {
        _http.Dispose();
    }
}
