using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Net.Http;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using Azure.Core;
using Bagatka.Azure.Sandboxes.Models;

namespace Bagatka.Sandboxing.Azure;

// Signs Azure in to the Azure Container Registries the provider pulls images from, with the
// provider's own credential: its Microsoft Entra token is exchanged for a refresh token of the
// registry, which Azure takes as the password of the zero GUID while it makes a disk image, and which
// lasts about three hours. The sandbox group itself has no identity, so sandboxes never get one.
internal sealed class ContainerRegistries(TokenCredential credential, IReadOnlyList<string> registries) : IDisposable
{
    private const string Username = "00000000-0000-0000-0000-000000000000";

    private static readonly TokenRequestContext RegistryScope = new TokenRequestContext(["https://containerregistry.azure.net/.default"]);

    private readonly HttpClient _http = new HttpClient();

    // What Azure signs in to the image's registry with; none for an image from a registry the provider
    // doesn't sign in to. A registry that refuses the exchange throws an HttpRequestException with its
    // status.
    public async Task<RegistryCredentials?> SignInAsync(string imageReference, CancellationToken ct)
    {
        string registry = imageReference.Split('/')[0];
        bool signsIn = registries.Contains(registry, StringComparer.OrdinalIgnoreCase);
        if (!signsIn)
        {
            return null;
        }

        AccessToken entra = await credential.GetTokenAsync(RegistryScope, ct);
        using FormUrlEncodedContent form = new FormUrlEncodedContent(
        [
            new KeyValuePair<string, string>("grant_type", "access_token"),
            new KeyValuePair<string, string>("service", registry),
            new KeyValuePair<string, string>("access_token", entra.Token),
        ]);
        using HttpResponseMessage response = await _http.PostAsync(new Uri("https://" + registry + "/oauth2/exchange"), form, ct);
        response.EnsureSuccessStatusCode();
        Stream body = await response.Content.ReadAsStreamAsync(ct);
        using JsonDocument answer = await JsonDocument.ParseAsync(body, cancellationToken: ct);
        string? refreshToken = answer.RootElement.GetProperty("refresh_token").GetString();
        if (string.IsNullOrEmpty(refreshToken))
        {
            throw new InvalidOperationException(registry + " answered the sign-in without a refresh token.");
        }

        return new RegistryCredentials(Username, refreshToken);
    }

    public void Dispose()
    {
        _http.Dispose();
    }
}
