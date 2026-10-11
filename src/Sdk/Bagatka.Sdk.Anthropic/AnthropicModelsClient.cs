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

namespace Bagatka.Sdk.Anthropic;

/// <summary>
/// The models Anthropic's API serves, as its <c>GET /v1/models</c> lists them: Anthropic's own, or
/// another endpoint that speaks its API, with the caller's credentials. Calls are never retried, since
/// they are cheap for the caller to repeat. An unexpected answer throws
/// <see cref="HttpRequestException"/> with its status code. Thread-safe.
/// </summary>
public sealed class AnthropicModelsClient : IDisposable
{
    private const string ApiVersion = "2023-06-01";

    // Not handled: more models than one page holds; Anthropic serves a few dozen.
    private const int PageSize = 1000;

    private readonly HttpClient _http;

    /// <summary>Creates a client.</summary>
    public AnthropicModelsClient()
    {
        SocketsHttpHandler handler = new SocketsHttpHandler { PooledConnectionLifetime = TimeSpan.FromMinutes(5) };
        _http = new HttpClient(handler, disposeHandler: true) { Timeout = TimeSpan.FromSeconds(30) };
        _http.DefaultRequestHeaders.UserAgent.Add(new ProductInfoHeaderValue("Bagatka.Sdk.Anthropic", "1.0"));
    }

    /// <summary>
    /// The models the API at <paramref name="apiUrl"/> serves, newest first, such as
    /// <c>https://api.anthropic.com</c>, asked with <paramref name="headers"/>, which hold the
    /// credentials, such as <c>x-api-key</c>, and are never logged.
    /// </summary>
    public async Task<IReadOnlyList<AnthropicModel>> ListModelsAsync(Uri apiUrl, IReadOnlyDictionary<string, string> headers, CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(apiUrl);
        ArgumentNullException.ThrowIfNull(headers);
        Uri models = new Uri(string.Create(CultureInfo.InvariantCulture, $"{apiUrl.AbsoluteUri.TrimEnd('/')}/v1/models?limit={PageSize}"));
        using HttpRequestMessage request = new HttpRequestMessage(HttpMethod.Get, models);
        request.Headers.TryAddWithoutValidation("anthropic-version", ApiVersion);
        foreach (KeyValuePair<string, string> header in headers)
        {
            request.Headers.TryAddWithoutValidation(header.Key, header.Value);
        }

        using HttpResponseMessage response = await _http.SendAsync(request, ct).ConfigureAwait(false);
        if (!response.IsSuccessStatusCode)
        {
            string message = string.Create(CultureInfo.InvariantCulture, $"Anthropic's API answered {(int)response.StatusCode} to GET /v1/models.");
            throw new HttpRequestException(message, inner: null, response.StatusCode);
        }

        AnthropicWire.ModelList? list;
        try
        {
            list = await response.Content.ReadFromJsonAsync(AnthropicJsonContext.Default.ModelList, ct).ConfigureAwait(false);
        }
        catch (JsonException exception)
        {
            throw new HttpRequestException("Anthropic's answer isn't the JSON expected.", exception);
        }

        if (list is null)
        {
            throw new HttpRequestException("Anthropic answered with nothing.");
        }

        return [.. list.Data.Select(model => new AnthropicModel(model.Id, model.DisplayName ?? model.Id))];
    }

    /// <inheritdoc />
    public void Dispose()
    {
        _http.Dispose();
    }
}
