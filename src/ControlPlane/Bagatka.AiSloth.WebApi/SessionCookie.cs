using System;
using Microsoft.AspNetCore.Http;

namespace Bagatka.AiSloth.WebApi;

/// <summary>
/// How a browser carries its session: in a cookie no script can read, sent only to this host's
/// address and only with requests from this host's own site. Other clients send the same session's
/// token as <c>Authorization: Bearer</c> instead.
/// </summary>
internal static class SessionCookie
{
    // __Host-: the browser keeps it only when it is secure, for the whole host, and for no other host.
    public const string Name = "__Host-aisloth-session";

    // The session decides when it ends (90 days unused); the cookie lasts as long as browsers let one.
    private static readonly TimeSpan Lifetime = TimeSpan.FromDays(400);

    public static string? Read(HttpRequest request)
    {
        bool found = request.Cookies.TryGetValue(Name, out string? token);
        return found && token!.Length > 0 ? token : null;
    }

    public static void Write(HttpResponse response, string token)
    {
        response.Cookies.Append(Name, token, new CookieOptions
        {
            HttpOnly = true,
            Secure = true,
            SameSite = SameSiteMode.Strict,
            Path = "/",
            MaxAge = Lifetime,
            IsEssential = true,
        });
    }

    public static void Clear(HttpResponse response)
    {
        response.Cookies.Delete(Name, new CookieOptions { HttpOnly = true, Secure = true, SameSite = SameSiteMode.Strict, Path = "/" });
    }
}
