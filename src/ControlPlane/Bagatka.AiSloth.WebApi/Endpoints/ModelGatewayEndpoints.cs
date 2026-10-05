using System;
using System.Collections.Generic;
using System.Net.Http;
using System.Threading.Tasks;
using Bagatka.AiSloth.AgentAccounts.Contracts;
using Bagatka.AiSloth.Chats.Contracts;
using Bagatka.Foundation;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Routing;
using Microsoft.Extensions.Primitives;

namespace Bagatka.AiSloth.WebApi.Endpoints;

/// <summary>
/// The model gateway: agents in nooks call their model through it, and it forwards each call to the
/// endpoint of their chat's agent account with the headers that pay for it, so no nook ever holds a
/// key or a plan's token. A call carries its chat's token, as a bearer token or an API key, and its
/// path follows the gateway's URL as it would follow the endpoint's. Everything else passes through
/// unchanged, streams included. Refusals answer in the shape OpenAI's and Anthropic's clients show.
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
        string authorization = request.Headers.Authorization.ToString();
        bool bearer = authorization.StartsWith("Bearer ", StringComparison.Ordinal);
        string token = bearer ? authorization["Bearer ".Length..] : request.Headers["X-Api-Key"].ToString();
        if (token.Length == 0)
        {
            await RefuseAsync(context, StatusCodes.Status401Unauthorized, "This call carries no chat's token.");
            return;
        }

        Result<ModelEndpoint> found = await chats.GetModelEndpointAsync(Actor.Anonymous, token, context.RequestAborted);
        if (found.Failed)
        {
            string message = found.Error == Error.Unauthorized ? "This call carries no chat's token." : found.Error.Message;
            await RefuseAsync(context, StatusCodes.Status401Unauthorized, message);
            return;
        }

        ModelEndpoint endpoint = found.Output;
        bool plainHttpRefused = !settings.AllowPrivateNetworks && !string.Equals(endpoint.Url.Scheme, Uri.UriSchemeHttps, StringComparison.Ordinal);
        if (plainHttpRefused)
        {
            await RefuseAsync(context, StatusCodes.Status502BadGateway, "The agent account's endpoint must use https.");
            return;
        }

        using HttpRequestMessage forwarded = Forwarded(request, endpoint, path);
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

    // The agent's call as it goes to the endpoint: the same method, path, query, body, and headers,
    // with the account's headers in place of the chat's token.
    private static HttpRequestMessage Forwarded(HttpRequest request, ModelEndpoint endpoint, string? path)
    {
        Uri target = new Uri(endpoint.Url.AbsoluteUri.TrimEnd('/') + "/" + path + request.QueryString);
        HttpRequestMessage forwarded = new HttpRequestMessage(new HttpMethod(request.Method), target);
        if (request.ContentLength > 0 || request.Headers.TransferEncoding.Count > 0)
        {
            forwarded.Content = new StreamContent(request.Body);
        }

        foreach (KeyValuePair<string, StringValues> header in request.Headers)
        {
            if (NotForwarded.Contains(header.Key))
            {
                continue;
            }

            IEnumerable<string?> values = header.Value;
            bool isRequestHeader = forwarded.Headers.TryAddWithoutValidation(header.Key, values);
            if (!isRequestHeader)
            {
                forwarded.Content?.Headers.TryAddWithoutValidation(header.Key, values);
            }
        }

        foreach (KeyValuePair<string, string> header in endpoint.Headers)
        {
            forwarded.Headers.TryAddWithoutValidation(header.Key, header.Value);
        }

        return forwarded;
    }

    private static async Task RefuseAsync(HttpContext context, int status, string message)
    {
        string type = status == StatusCodes.Status401Unauthorized ? "authentication_error" : "api_error";
        context.Response.StatusCode = status;
        await context.Response.WriteAsJsonAsync(new GatewayRefusal("error", new GatewayRefusalDetail(type, message)), context.RequestAborted);
    }

    // OpenAI's clients read error.message, Anthropic's type and error.message.
    internal sealed record GatewayRefusal(string Type, GatewayRefusalDetail Error);

    internal sealed record GatewayRefusalDetail(string Type, string Message);
}
