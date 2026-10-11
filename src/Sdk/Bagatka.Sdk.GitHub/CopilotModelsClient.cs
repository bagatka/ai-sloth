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
/// the plan's GitHub token. Copilot doesn't document this endpoint; what it lists may change. Calls are
/// never retried, since they are cheap for the caller to repeat. An unexpected answer throws
/// <see cref="HttpRequestException"/> with its status code. Thread-safe.
/// </summary>
public sealed class CopilotModelsClient : IDisposable
{
    // How Copilot's API knows which tool asks: its command line, whose plan's token this is.
    private const string IntegrationId = "copilot-developer-cli";

    private readonly HttpClient _http;

    /// <summary>Creates a client.</summary>
    public CopilotModelsClient()
    {
        SocketsHttpHandler handler = new SocketsHttpHandler { PooledConnectionLifetime = TimeSpan.FromMinutes(5) };
        _http = new HttpClient(handler, disposeHandler: true) { Timeout = TimeSpan.FromSeconds(30) };
        _http.DefaultRequestHeaders.UserAgent.Add(new ProductInfoHeaderValue("Bagatka.Sdk.GitHub", "1.0"));
    }

    /// <summary>
    /// The chat models the plan of <paramref name="token"/> offers in its model picker, in Copilot's
    /// order, from Copilot's API at <paramref name="apiUrl"/>, such as <c>https://api.githubcopilot.com</c>.
    /// The token is never logged.
    /// </summary>
    public async Task<IReadOnlyList<CopilotModel>> ListModelsAsync(Uri apiUrl, string token, CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(apiUrl);
        ArgumentException.ThrowIfNullOrEmpty(token);
        using HttpRequestMessage request = new HttpRequestMessage(HttpMethod.Get, new Uri(apiUrl.AbsoluteUri.TrimEnd('/') + "/models"));
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token);
        request.Headers.TryAddWithoutValidation("Copilot-Integration-Id", IntegrationId);
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

        // The picker's chat models the plan allows; others are for embeddings, or off for the plan.
        return [.. list.Data
            .Where(model => model is { ModelPickerEnabled: true, Capabilities.Type: "chat" } && model.Policy?.State is null or "enabled")
            .Select(model => new CopilotModel(model.Id, model.Name ?? model.Id, model.Capabilities?.Supports?.ToolCalls == true))];
    }

    /// <inheritdoc />
    public void Dispose()
    {
        _http.Dispose();
    }
}
