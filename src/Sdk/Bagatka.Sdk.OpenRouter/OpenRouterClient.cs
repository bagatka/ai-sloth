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

namespace Bagatka.Sdk.OpenRouter;

/// <summary>
/// The parts of OpenRouter's API a product uses besides its model calls: the models it offers. Calls
/// are never retried, since they are cheap for the caller to repeat. A call OpenRouter answers
/// unexpectedly throws <see cref="HttpRequestException"/> with its status code. Thread-safe.
/// </summary>
public sealed class OpenRouterClient : IDisposable
{
    private readonly HttpClient _http;

    /// <summary>Creates a client for the OpenRouter in the settings.</summary>
    public OpenRouterClient(OpenRouterSettings settings)
    {
        ArgumentNullException.ThrowIfNull(settings);
        Settings = settings;
        SocketsHttpHandler handler = new SocketsHttpHandler { PooledConnectionLifetime = TimeSpan.FromMinutes(5) };
        _http = new HttpClient(handler, disposeHandler: true) { Timeout = TimeSpan.FromSeconds(30) };
        _http.DefaultRequestHeaders.UserAgent.Add(new ProductInfoHeaderValue("Bagatka.Sdk.OpenRouter", "1.0"));
    }

    /// <summary>Where it calls.</summary>
    public OpenRouterSettings Settings { get; }

    /// <summary>
    /// Every model OpenRouter offers, as its catalog lists them; the catalog is public, so this takes no
    /// key. It is about 750 KB, read in one call.
    /// </summary>
    public async Task<IReadOnlyList<OpenRouterModel>> ListModelsAsync(CancellationToken ct)
    {
        using HttpResponseMessage response = await _http.GetAsync(new Uri(Settings.ApiUrl, "v1/models"), ct).ConfigureAwait(false);
        if (!response.IsSuccessStatusCode)
        {
            string message = string.Create(CultureInfo.InvariantCulture, $"OpenRouter answered {(int)response.StatusCode} to GET /v1/models.");
            throw new HttpRequestException(message, inner: null, response.StatusCode);
        }

        OpenRouterWire.ModelList? list;
        try
        {
            list = await response.Content.ReadFromJsonAsync(OpenRouterJsonContext.Default.ModelList, ct).ConfigureAwait(false);
        }
        catch (JsonException exception)
        {
            throw new HttpRequestException("OpenRouter's answer isn't the JSON expected.", exception);
        }

        if (list is null)
        {
            throw new HttpRequestException("OpenRouter answered with nothing.");
        }

        return [.. list.Data.Select(model => new OpenRouterModel(
            model.Id,
            model.Name ?? model.Id,
            model.SupportedParameters ?? [],
            model.ContextLength is > 0 and <= int.MaxValue ? (int)model.ContextLength : null,
            PriceOf(model.Pricing)))];
    }

    // A price OpenRouter sets ahead: an input and an output price that aren't negative.
    private static OpenRouterPrice? PriceOf(OpenRouterWire.Pricing? pricing)
    {
        decimal? prompt = Dollars(pricing?.Prompt);
        decimal? completion = Dollars(pricing?.Completion);
        return prompt is decimal input && completion is decimal output
            ? new OpenRouterPrice(input, output, Dollars(pricing?.InputCacheRead), Dollars(pricing?.InputCacheWrite))
            : null;
    }

    private static decimal? Dollars(string? text)
    {
        bool read = decimal.TryParse(text, NumberStyles.Float, CultureInfo.InvariantCulture, out decimal dollars);
        return read && dollars >= 0 ? dollars : null;
    }

    /// <inheritdoc />
    public void Dispose()
    {
        _http.Dispose();
    }
}
