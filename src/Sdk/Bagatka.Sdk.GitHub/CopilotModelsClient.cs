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

namespace Bagatka.Sdk.GitHub;

/// <summary>
/// The models a Copilot plan can chat with, as Copilot's API lists them (<c>GET /models</c>), asked with
/// the plan's <see cref="CopilotApi.Headers"/>. Copilot doesn't document this endpoint; what it lists may
/// change. Calls are never retried, since they are cheap for the caller to repeat. An unexpected answer
/// throws <see cref="HttpRequestException"/> with its status code. Thread-safe.
/// </summary>
public sealed class CopilotModelsClient : IDisposable
{
    private readonly HttpClient _http;

    /// <summary>Creates a client.</summary>
    public CopilotModelsClient()
    {
        SocketsHttpHandler handler = new SocketsHttpHandler { PooledConnectionLifetime = TimeSpan.FromMinutes(5) };
        _http = new HttpClient(handler, disposeHandler: true) { Timeout = TimeSpan.FromSeconds(30) };
        _http.DefaultRequestHeaders.UserAgent.Add(new ProductInfoHeaderValue("Bagatka.Sdk.GitHub", "1.0"));
    }

    /// <summary>
    /// The chat models the plan allows, in Copilot's order, from Copilot's API at
    /// <paramref name="apiUrl"/>, such as <see cref="CopilotApi.Public"/>, asked with
    /// <paramref name="headers"/>, which hold the plan's token and are never logged.
    /// </summary>
    public async Task<IReadOnlyList<CopilotModel>> ListModelsAsync(Uri apiUrl, IReadOnlyDictionary<string, string> headers, CancellationToken ct)
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
            string message = string.Create(CultureInfo.InvariantCulture, $"Copilot's API answered {(int)response.StatusCode} to GET /models.");
            throw new HttpRequestException(message, inner: null, response.StatusCode);
        }

        GitHubWire.CopilotModelList? list;
        try
        {
            list = await response.Content.ReadFromJsonAsync(GitHubJsonContext.Default.CopilotModelList, ct).ConfigureAwait(false);
        }
        catch (JsonException exception)
        {
            throw new HttpRequestException("Copilot's answer isn't the JSON expected.", exception);
        }

        if (list is null)
        {
            throw new HttpRequestException("Copilot answered with nothing.");
        }

        // The chat models the plan's policy enables; others are for embeddings, off for the plan, or
        // Copilot's own, without a policy. Not handled: a model the policy enables but the plan's
        // billing doesn't include, such as gpt-5-mini on Copilot Free; its turns fail with Copilot's
        // answer, and telling it apart needs the plan's billing, which the list doesn't show.
        return [.. list.Data
            .Where(model => model is { Capabilities.Type: "chat", Policy.State: "enabled" })
            .Select(model => new CopilotModel(model.Id, model.Name ?? model.Id, model.Capabilities?.Supports?.ToolCalls == true))];
    }

    /// <inheritdoc />
    public void Dispose()
    {
        _http.Dispose();
    }
}
