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
    /// Every model the API at <paramref name="apiUrl"/> serves, such as <c>https://api.openai.com/v1</c>,
    /// asked with <paramref name="headers"/>, which hold the credentials and are never logged.
    /// </summary>
    public async Task<IReadOnlyList<OpenAIModel>> ListModelsAsync(Uri apiUrl, IReadOnlyDictionary<string, string> headers, CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(apiUrl);
        ArgumentNullException.ThrowIfNull(headers);
        using HttpRequestMessage request = new HttpRequestMessage(HttpMethod.Get, new Uri(apiUrl.AbsoluteUri.TrimEnd('/') + "/models"));
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

        if (list is null)
        {
            throw new HttpRequestException("The OpenAI API answered with nothing.");
        }

        return [.. list.Data.Select(model => new OpenAIModel(model.Id, model.Created is long seconds ? DateTimeOffset.FromUnixTimeSeconds(seconds) : null))];
    }

    /// <inheritdoc />
    public void Dispose()
    {
        _http.Dispose();
    }
}
