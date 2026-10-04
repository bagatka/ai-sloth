using System;
using System.Collections.Generic;
using System.Net.Http;
using System.Threading.Tasks;
using Bagatka.AiSloth.Chats.Contracts;
using Bagatka.Foundation;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Routing;
using Microsoft.Extensions.Primitives;

namespace Bagatka.AiSloth.WebApi.Endpoints;

/// <summary>
/// The model gateway: agents in nooks call the model provider through it, and it adds the
/// deployment's key, so no nook ever holds one. A call must carry its chat's token, as a bearer token
/// or an API key; Chats decides whether it does. Everything else passes through unchanged, streams
/// included.
/// </summary>
internal static class ModelGatewayEndpoints
{
    public const string HttpClientName = "model-gateway";

    // Hop-by-hop headers and the caller's own credentials stay here.
    private static readonly HashSet<string> NotForwarded = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
    {
        "Host", "Authorization", "X-Api-Key", "Connection", "Keep-Alive", "Proxy-Connection", "Transfer-Encoding", "TE", "Trailer", "Upgrade",
    };

    public static IEndpointConventionBuilder MapModelGateway(this IEndpointRouteBuilder app)
    {
        return app.Map("/models/{**path}", ForwardAsync).AllowAnonymous().ExcludeFromDescription();
    }

    private static async Task ForwardAsync(
        HttpContext context,
        [FromRoute] string? path,
        [FromServices] IChatHarnessesApi chats,
        [FromServices] ModelGatewaySettings settings,
        [FromServices] IHttpClientFactory clients)
    {
        HttpRequest request = context.Request;
        string? token = request.Headers.Authorization.ToString() is { } authorization && authorization.StartsWith("Bearer ", StringComparison.Ordinal)
            ? authorization["Bearer ".Length..]
            : request.Headers["X-Api-Key"].ToString();
        if (string.IsNullOrEmpty(token) || (await chats.AuthorizeModelCallAsync(Actor.Anonymous, token, context.RequestAborted)).IsError(out _))
        {
            context.Response.StatusCode = StatusCodes.Status401Unauthorized;
            return;
        }

        using HttpRequestMessage forwarded = new HttpRequestMessage(new HttpMethod(request.Method), new Uri(settings.Upstream, path + request.QueryString));
        if (request.ContentLength > 0 || request.Headers.TransferEncoding.Count > 0)
        {
            forwarded.Content = new StreamContent(request.Body);
        }

        foreach (KeyValuePair<string, StringValues> header in request.Headers)
        {
            if (!NotForwarded.Contains(header.Key) && !forwarded.Headers.TryAddWithoutValidation(header.Key, (IEnumerable<string?>)header.Value))
            {
                forwarded.Content?.Headers.TryAddWithoutValidation(header.Key, (IEnumerable<string?>)header.Value);
            }
        }

        forwarded.Headers.Add("X-Api-Key", settings.ApiKey);

        using HttpResponseMessage response = await clients.CreateClient(HttpClientName)
            .SendAsync(forwarded, HttpCompletionOption.ResponseHeadersRead, context.RequestAborted);
        context.Response.StatusCode = (int)response.StatusCode;
        foreach (KeyValuePair<string, IEnumerable<string>> header in response.Headers)
        {
            if (!NotForwarded.Contains(header.Key))
            {
                context.Response.Headers[header.Key] = new StringValues([.. header.Value]);
            }
        }

        foreach (KeyValuePair<string, IEnumerable<string>> header in response.Content.Headers)
        {
            context.Response.Headers[header.Key] = new StringValues([.. header.Value]);
        }

        await response.Content.CopyToAsync(context.Response.Body, context.RequestAborted);
    }
}
