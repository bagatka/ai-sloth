using System;
using System.Globalization;
using System.Net;
using System.Net.Http;
using System.Net.Http.Json;
using System.Threading;
using System.Threading.Tasks;
using Bagatka.Foundation;
using Xunit;

namespace Bagatka.AiSloth.EndToEndTests;

/// <summary>
/// Reading the public API's responses: a value with its expected status, or the problem an error became.
/// </summary>
internal static class Api
{
    // Background work such as the reconciler and the daemon's reports takes seconds; this bounds
    // waiting for it, so a broken test fails instead of hanging.
    private static readonly TimeSpan Patience = TimeSpan.FromSeconds(60);

    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    public static async Task<T> ReadAsync<T>(HttpResponseMessage response, HttpStatusCode expected)
    {
        await ExpectAsync(response, expected);
        return await response.Content.ReadFromJsonAsync<T>(FoundationJson.Options, Ct)
            ?? throw new InvalidOperationException("The response had no body.");
    }

    public static async Task<Problem> ProblemAsync(HttpResponseMessage response, HttpStatusCode expected)
    {
        await ExpectAsync(response, expected);
        return await response.Content.ReadFromJsonAsync<Problem>(FoundationJson.Options, Ct)
            ?? throw new InvalidOperationException("The response had no problem details.");
    }

    public static async Task ExpectAsync(HttpResponseMessage response, HttpStatusCode expected)
    {
        if (response.StatusCode != expected)
        {
            string body = await response.Content.ReadAsStringAsync(Ct);
            Assert.Fail("Expected " + expected + ", got " + response.StatusCode + ": " + body);
        }
    }

    public static Task<HttpResponseMessage> SendPostAsync(this HttpClient client, string path, object body)
    {
        return client.PostAsJsonAsync(new Uri(path, UriKind.Relative), body, FoundationJson.Options, Ct);
    }

    public static Task<HttpResponseMessage> SendGetAsync(this HttpClient client, string path)
    {
        return client.GetAsync(new Uri(path, UriKind.Relative), Ct);
    }

    // Polls until the value appears.
    public static async Task<T> EventuallyAsync<T>(Func<Task<T?>> read)
        where T : class
    {
        long started = TimeProvider.System.GetTimestamp();
        while (TimeProvider.System.GetElapsedTime(started) < Patience)
        {
            if (await read() is T value)
            {
                return value;
            }

            await Task.Delay(TimeSpan.FromMilliseconds(200), Ct);
        }

        throw new TimeoutException(string.Create(CultureInfo.InvariantCulture, $"The value didn't appear within {Patience}."));
    }
}
