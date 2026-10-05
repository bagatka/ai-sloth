using System;
using System.Globalization;
using System.Linq;
using System.Net;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
using System.Text.Json.Serialization.Metadata;
using System.Threading;
using System.Threading.Tasks;

namespace Bagatka.AiSloth.Cli;

// Calls to one host's public API, as the person whose session token it carries, or anonymously to
// sign in. A host that can't be reached or refuses a call throws HttpRequestException whose message
// says why, in the host's words where it gave them; sloth prints it as the command's failure. Its
// status code is set for a refusal and null when the host couldn't be reached or didn't answer.
internal sealed class HostApi : IDisposable
{
    // Where a refusal's error code is, in the exception's data.
    public const string ProblemCode = "code";

    private readonly HttpClient _http;
    private readonly Uri _url;

    public HostApi(HttpMessageHandler handler, Uri url, string? token)
    {
        _url = url;
        _http = new HttpClient(handler, disposeHandler: false) { BaseAddress = url };
        if (token is not null)
        {
            _http.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", token);
        }
    }

    public async Task<T> GetAsync<T>(string path, JsonTypeInfo<T> answer, CancellationToken ct)
    {
        using HttpResponseMessage response = await SendAsync(HttpMethod.Get, path, content: null, ct);
        return await ReadAsync(response, answer, ct);
    }

    public async Task<T> SendAsync<TBody, T>(HttpMethod method, string path, TBody body, JsonTypeInfo<TBody> request, JsonTypeInfo<T> answer, CancellationToken ct)
    {
        using JsonContent content = JsonContent.Create(body, request);
        using HttpResponseMessage response = await SendAsync(method, path, content, ct);
        return await ReadAsync(response, answer, ct);
    }

    // A call without a body either way, such as a removal.
    public async Task CallAsync(HttpMethod method, string path, CancellationToken ct)
    {
        using HttpResponseMessage response = await SendAsync(method, path, content: null, ct);
        await EnsureSuccessAsync(response, ct);
    }

    // The host's answer whatever its status: failing to reach the host is the only failure here.
    public async Task<HttpResponseMessage> SendAsync(HttpMethod method, string path, HttpContent? content, CancellationToken ct, HttpCompletionOption completion = HttpCompletionOption.ResponseContentRead)
    {
        using HttpRequestMessage request = new HttpRequestMessage(method, new Uri(path, UriKind.Relative)) { Content = content };
        try
        {
            return await _http.SendAsync(request, completion, ct);
        }
        catch (HttpRequestException exception)
        {
            throw new HttpRequestException("Couldn't reach " + _url.Authority + ": " + exception.Message, exception);
        }
        catch (TaskCanceledException exception) when (!ct.IsCancellationRequested)
        {
            throw new HttpRequestException(string.Create(CultureInfo.InvariantCulture, $"{_url.Authority} didn't answer within {_http.Timeout.TotalSeconds} seconds."), exception);
        }
    }

    public async Task<T> ReadAsync<T>(HttpResponseMessage response, JsonTypeInfo<T> answer, CancellationToken ct)
    {
        await EnsureSuccessAsync(response, ct);
        T? value = await response.Content.ReadFromJsonAsync(answer, ct);
        if (value is null)
        {
            // Not handled: a host answering a call with JSON null; only a broken host would.
            throw new HttpRequestException(_url.Authority + " answered with nothing.", inner: null, response.StatusCode);
        }

        return value;
    }

    public async Task EnsureSuccessAsync(HttpResponseMessage response, CancellationToken ct)
    {
        if (response.IsSuccessStatusCode)
        {
            return;
        }

        string why;
        string? code = null;
        if (response.StatusCode == HttpStatusCode.Unauthorized)
        {
            why = "Your session on " + _url.Authority + " ended. Sign in again: sloth host add " + _url.AbsoluteUri.TrimEnd('/');
        }
        else
        {
            Wire.Problem problem = await ProblemAsync(response, ct);
            string[] fields = problem.Errors is null ? [] : [.. problem.Errors.Values.SelectMany(messages => messages)];
            why = fields.Length > 0 ? string.Join(" ", fields)
                : problem.Title ?? string.Create(CultureInfo.InvariantCulture, $"{_url.Authority} answered {(int)response.StatusCode} {response.ReasonPhrase}.");
            code = problem.Code;
        }

        // The host's error code rides along, for commands that can say what to do about it.
        HttpRequestException refused = new HttpRequestException(why, inner: null, response.StatusCode);
        refused.Data[ProblemCode] = code;
        throw refused;
    }

    // What the host said about a refusal; empty when it said nothing sloth can read.
    public static async Task<Wire.Problem> ProblemAsync(HttpResponseMessage response, CancellationToken ct)
    {
        string? type = response.Content.Headers.ContentType?.MediaType;
        if (type is not ("application/problem+json" or "application/json"))
        {
            return new Wire.Problem(Title: null, Code: null, Errors: null);
        }

        try
        {
            Wire.Problem? problem = await response.Content.ReadFromJsonAsync(CliJsonContext.Default.Problem, ct);
            return problem ?? new Wire.Problem(Title: null, Code: null, Errors: null);
        }
        catch (JsonException)
        {
            // The status code alone says what happened.
            return new Wire.Problem(Title: null, Code: null, Errors: null);
        }
    }

    public void Dispose()
    {
        _http.Dispose();
    }
}
