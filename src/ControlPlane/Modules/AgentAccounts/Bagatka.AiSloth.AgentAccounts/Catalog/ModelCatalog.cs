using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Net;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Text.Json;
using System.Text.RegularExpressions;
using System.Threading;
using System.Threading.Tasks;
using Bagatka.AiSloth.AgentAccounts.Contracts;
using Microsoft.Extensions.Logging;

namespace Bagatka.AiSloth.AgentAccounts.Catalog;

// What models.dev's open catalog says of the models of the vendors accounts reach: whether each
// reasons and how, its limits, and its prices. It starts from the snapshot built into the module
// (models-dev.json, written by update-models-dev.sh) and is read again from models.dev once a day old,
// by the next caller, which every caller meanwhile waits for; a failed read keeps what it has and tries
// again in an hour. Thread-safe.
internal sealed partial class ModelCatalog : IDisposable
{
    public const string Anthropic = "anthropic";
    public const string OpenAI = "openai";
    public const string OpenRouter = "openrouter";
    public const string Copilot = "github-copilot";

    private static readonly string[] Vendors = [Anthropic, OpenAI, OpenRouter, Copilot];
    private static readonly TimeSpan MaxAge = TimeSpan.FromDays(1);
    private static readonly TimeSpan RetryAfter = TimeSpan.FromHours(1);
    private static readonly JsonSerializerOptions Json = new JsonSerializerOptions(JsonSerializerDefaults.Web) { PropertyNamingPolicy = JsonNamingPolicy.SnakeCaseLower };

    private readonly AgentAccountsSettings _settings;
    private readonly TimeProvider _time;
    private readonly ILogger<ModelCatalog> _logger;
    private readonly HttpClient _http;
    private readonly Lock _gate = new Lock();
    private Read _current;
    private Task? _reading;

    public ModelCatalog(AgentAccountsSettings settings, TimeProvider time, ILogger<ModelCatalog> logger)
    {
        _settings = settings;
        _time = time;
        _logger = logger;
        _current = new Read(Snapshot(), DueAt: DateTimeOffset.MinValue, ETag: null);
        _http = new HttpClient(new SocketsHttpHandler { PooledConnectionLifetime = TimeSpan.FromMinutes(5) }, disposeHandler: true) { Timeout = TimeSpan.FromSeconds(30) };
        _http.DefaultRequestHeaders.UserAgent.Add(new ProductInfoHeaderValue("AiSloth", "1.0"));
    }

    // The model in the vendor's part of the catalog, by its ID there, or with a date the catalog leaves
    // out (gpt-4.1-2025-04-14), or with dots for dashes (Copilot's claude-opus-5.5, Anthropic's
    // claude-opus-5-5); null when the catalog lacks it.
    public async Task<ModelsDev.Model?> FindAsync(string vendor, string model, CancellationToken ct)
    {
        await FreshAsync(ct);
        IReadOnlyDictionary<string, ModelsDev.Model>? models = _current.Vendors.GetValueOrDefault(vendor);
        if (models is null)
        {
            return null;
        }

        string undated = Dated().Replace(model, string.Empty);
        foreach (string id in (string[])[model, undated, undated.Replace('.', '-'), undated.Replace('-', '.')])
        {
            ModelsDev.Model? found = models.GetValueOrDefault(id);
            if (found is not null)
            {
                return found;
            }
        }

        return null;
    }

    // What the catalog says of a model, priced as the account pays: at the vendor's price, as an
    // estimate at the API's for a plan, or not at all when the account names an endpoint of its own.
    public static ModelFacts Facts(string vendor, string model, ModelsDev.Model? found, bool estimate, bool priced)
    {
        if (found is null)
        {
            return ModelFacts.Unknown with { CacheLifetime = CacheLifetime(vendor, model) };
        }

        IReadOnlyList<string> efforts = found.ReasoningOptions?.FirstOrDefault(option => string.Equals(option.Type, "effort", StringComparison.Ordinal))?.Values ?? [];
        ModelPrice? price = priced && found.Cost is ModelsDev.Cost cost ? PriceOf(cost, estimate) : null;
        return new ModelFacts(found.Reasoning, efforts, Tokens(found.Limit?.Context), Tokens(found.Limit?.Output), price, CacheLifetime(vendor, model));
    }

    public void Dispose()
    {
        _http.Dispose();
    }

    // How long the vendor keeps a prompt's cache after its last use, at least: Anthropic's five
    // minutes; OpenAI's five, of five to ten, or thirty for GPT-5.6 and later; unknown elsewhere,
    // Copilot's included. Not handled: a cache written to last longer (Anthropic's hour, OpenAI's
    // day); a call says when it wrote one.
    private static TimeSpan? CacheLifetime(string vendor, string model)
    {
        bool anthropic = string.Equals(vendor, Anthropic, StringComparison.Ordinal) || model.StartsWith("anthropic/", StringComparison.Ordinal);
        bool openAI = string.Equals(vendor, OpenAI, StringComparison.Ordinal) || model.StartsWith("openai/", StringComparison.Ordinal);
        if (anthropic)
        {
            return TimeSpan.FromMinutes(5);
        }

        if (!openAI)
        {
            return null;
        }

        Match version = GptVersion().Match(model);
        bool newest = version.Success && decimal.Parse(version.Groups["version"].Value, CultureInfo.InvariantCulture) >= 5.6m;
        return TimeSpan.FromMinutes(newest ? 30 : 5);
    }

    private static ModelPrice PriceOf(ModelsDev.Cost cost, bool estimate)
    {
        List<PriceTier> tiers = [.. (cost.Tiers ?? [])
            .Where(tier => string.Equals(tier.Tier.Type, "context", StringComparison.Ordinal))
            .Select(tier => new PriceTier(checked((int)tier.Tier.Size), tier.Input, tier.Output, tier.CacheRead, tier.CacheWrite))];
        if (tiers.Count == 0 && cost.ContextOver200k is ModelsDev.Prices over)
        {
            tiers.Add(new PriceTier(200_000, over.Input, over.Output, over.CacheRead, over.CacheWrite));
        }

        return new ModelPrice(cost.Input, cost.Output, cost.CacheRead, cost.CacheWrite, [.. tiers.OrderBy(tier => tier.AboveInputTokens)], estimate);
    }

    private static int? Tokens(long? count)
    {
        return count is > 0 and <= int.MaxValue ? (int)count : null;
    }

    // Reads models.dev again when due, once for everyone waiting.
    private Task FreshAsync(CancellationToken ct)
    {
        Task reading;
        lock (_gate)
        {
            if (_reading is null && _time.GetUtcNow() >= _current.DueAt)
            {
                _reading = ReadAsync(_settings.ModelsDevUrl);
            }

            reading = _reading ?? Task.CompletedTask;
        }

        return reading.WaitAsync(ct);
    }

    // Not cancelled by any caller: every caller waits for the same read, within the client's timeout.
    private async Task ReadAsync(Uri url)
    {
        Read read = _current;
        try
        {
            using HttpRequestMessage request = new HttpRequestMessage(HttpMethod.Get, url);
            if (read.ETag is string etag)
            {
                request.Headers.TryAddWithoutValidation("If-None-Match", etag);
            }

            using HttpResponseMessage response = await _http.SendAsync(request, HttpCompletionOption.ResponseHeadersRead);
            DateTimeOffset due = _time.GetUtcNow() + MaxAge;
            if (response.StatusCode == HttpStatusCode.NotModified)
            {
                read = read with { DueAt = due };
            }
            else
            {
                response.EnsureSuccessStatusCode();
                await using Stream body = await response.Content.ReadAsStreamAsync();
                Dictionary<string, IReadOnlyDictionary<string, ModelsDev.Model>> vendors = await ParseAsync(body);
                read = new Read(vendors, due, response.Headers.ETag?.Tag);
            }
        }
        catch (Exception exception) when (exception is HttpRequestException or JsonException or TaskCanceledException)
        {
            Log.CatalogNotRead(_logger, exception);
            read = read with { DueAt = _time.GetUtcNow() + RetryAfter };
        }

        lock (_gate)
        {
            _current = read;
            _reading = null;
        }
    }

    private static Dictionary<string, IReadOnlyDictionary<string, ModelsDev.Model>> Snapshot()
    {
        using Stream? snapshot = typeof(ModelCatalog).Assembly.GetManifestResourceStream("Bagatka.AiSloth.AgentAccounts.Catalog.models-dev.json");
        if (snapshot is null)
        {
            throw new InvalidOperationException("The module lacks its catalog snapshot.");
        }

        using JsonDocument document = JsonDocument.Parse(snapshot);
        return VendorsOf(document);
    }

    private static async Task<Dictionary<string, IReadOnlyDictionary<string, ModelsDev.Model>>> ParseAsync(Stream json)
    {
        using JsonDocument document = await JsonDocument.ParseAsync(json);
        return VendorsOf(document);
    }

    // The vendors' models, each read alone: one models.dev writes unlike the others is left out, not the
    // whole catalog.
    private static Dictionary<string, IReadOnlyDictionary<string, ModelsDev.Model>> VendorsOf(JsonDocument document)
    {
        Dictionary<string, IReadOnlyDictionary<string, ModelsDev.Model>> vendors = new Dictionary<string, IReadOnlyDictionary<string, ModelsDev.Model>>(StringComparer.Ordinal);
        foreach (string vendor in Vendors)
        {
            Dictionary<string, ModelsDev.Model> models = new Dictionary<string, ModelsDev.Model>(StringComparer.Ordinal);
            vendors[vendor] = models;
            bool hasVendor = document.RootElement.TryGetProperty(vendor, out JsonElement provider);
            if (!hasVendor)
            {
                continue;
            }

            foreach (JsonProperty entry in provider.GetProperty("models").EnumerateObject())
            {
                ModelsDev.Model? model = ModelOf(entry.Value);
                if (model is not null)
                {
                    models[entry.Name] = model;
                }
            }
        }

        return vendors;
    }

    private static ModelsDev.Model? ModelOf(JsonElement entry)
    {
        try
        {
            return entry.Deserialize<ModelsDev.Model>(Json);
        }
        catch (JsonException)
        {
            return null;
        }
    }

    [GeneratedRegex(@"-\d{4}-?\d{2}-?\d{2}$", RegexOptions.CultureInvariant, matchTimeoutMilliseconds: 100)]
    private static partial Regex Dated();

    [GeneratedRegex(@"gpt-(?<version>\d+(\.\d+)?)", RegexOptions.CultureInvariant | RegexOptions.ExplicitCapture, matchTimeoutMilliseconds: 100)]
    private static partial Regex GptVersion();

    private sealed record Read(IReadOnlyDictionary<string, IReadOnlyDictionary<string, ModelsDev.Model>> Vendors, DateTimeOffset DueAt, string? ETag);
}
