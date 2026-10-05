using System;
using System.Buffers.Text;
using System.Net;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Security.Cryptography;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.AspNetCore.WebUtilities;

namespace Bagatka.AiSloth.EndToEndTests;

/// <summary>
/// Signs its client in on its first call, the way a person's browser and the CLI do through the host's
/// identity provider, then sends the session's token with every call: the browser's redirects are
/// followed by hand up to the loopback address the CLI would listen on, whose code becomes a session.
/// </summary>
internal sealed class ProviderSignIn(Uri webApi, string subject) : DelegatingHandler
{
    private const string Loopback = "http://127.0.0.1:1/callback";

    private readonly SemaphoreSlim _gate = new SemaphoreSlim(1, 1);
    private string? _token;

    protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
    {
        await _gate.WaitAsync(cancellationToken);
        try
        {
            _token ??= await SignInAsync(cancellationToken);
        }
        finally
        {
            _gate.Release();
        }

        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", _token);
        return await base.SendAsync(request, cancellationToken);
    }

    protected override void Dispose(bool disposing)
    {
        if (disposing)
        {
            _gate.Dispose();
        }

        base.Dispose(disposing);
    }

    private async Task<string> SignInAsync(CancellationToken ct)
    {
        string verifier = Base64Url.EncodeToString(RandomNumberGenerator.GetBytes(32));
        string challenge = Base64Url.EncodeToString(SHA256.HashData(Encoding.ASCII.GetBytes(verifier)));
        string state = Base64Url.EncodeToString(RandomNumberGenerator.GetBytes(16));
        string start = QueryHelpers.AddQueryString(new Uri(webApi, "sign-in").AbsoluteUri, new System.Collections.Generic.Dictionary<string, string?>(StringComparer.Ordinal)
        {
            ["redirect_uri"] = Loopback,
            ["state"] = state,
            ["code_challenge"] = challenge,
            ["code_challenge_method"] = "S256",
            ["device"] = "e2e",
            ["login_hint"] = subject,
        });

        using HttpClientHandler handler = new HttpClientHandler { AllowAutoRedirect = false, CheckCertificateRevocationList = true };
        using HttpClient browser = new HttpClient(handler);
        Uri next = new Uri(start);
        while (!next.AbsoluteUri.StartsWith(Loopback, StringComparison.Ordinal))
        {
            using HttpResponseMessage response = await browser.GetAsync(next, ct);
            if (response.StatusCode != HttpStatusCode.Redirect || response.Headers.Location is null)
            {
                string body = await response.Content.ReadAsStringAsync(ct);
                throw new InvalidOperationException("Sign-in stopped at " + next.AbsolutePath + ": " + body);
            }

            next = response.Headers.Location;
        }

        string code = QueryHelpers.ParseQuery(next.Query)["code"].ToString();
        using JsonContent exchange = JsonContent.Create(new { code, codeVerifier = verifier, redirectUri = Loopback });
        using HttpResponseMessage token = await browser.PostAsync(new Uri(webApi, "sign-in/token"), exchange, ct);
        SignInEndpointsShapes.SignedIn signedIn = await Api.ReadAsync<SignInEndpointsShapes.SignedIn>(token, HttpStatusCode.OK);
        return signedIn.Token;
    }
}
