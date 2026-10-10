using System;
using System.Collections.Frozen;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Security.Cryptography;
using System.Text;
using System.Text.RegularExpressions;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.StaticFiles;
using Microsoft.Extensions.DependencyInjection;

namespace Bagatka.AiSloth.WebApi;

/// <summary>
/// Serves the web app (<c>src/Apps/Web</c>, built) at the host's app address: each of its files as it
/// is, and its page for every other path there, where the browser's router takes over. The page
/// learns where the app lives and where the API is, and comes with a content security policy that
/// lets it run nothing but the app's own files. The host's own address opens the app.
/// </summary>
internal static partial class WebApp
{
    // What the built page marks for the host to fill in (src/Apps/Web/index.html).
    private const string BaseMarker = "<base href=\"/app/\" />";
    private const string ApiMarker = "<meta name=\"aisloth-api\" content=\"\" />";

    private static readonly FileExtensionContentTypeProvider ContentTypes = new FileExtensionContentTypeProvider();

    private const string CorsPolicy = "WebApp";

    /// <summary>
    /// From an address of its own, the app calls the API across origins, with the browser's session
    /// cookie: only that address may, with what the app sends.
    /// </summary>
    public static IServiceCollection AddWebAppCors(this IServiceCollection services, HostSettings host)
    {
        return services.AddCors(cors => cors.AddPolicy(CorsPolicy, policy => policy
            .WithOrigins(host.AppUrl.GetLeftPart(UriPartial.Authority))
            .AllowCredentials()
            .WithMethods(HttpMethods.Get, HttpMethods.Post, HttpMethods.Put, HttpMethods.Delete)
            .WithHeaders("Content-Type", "Last-Event-ID")
            .SetPreflightMaxAge(TimeSpan.FromHours(1))));
    }

    public static void UseWebAppCors(this WebApplication app)
    {
        app.UseCors(CorsPolicy);
    }

    public static void UseWebApp(this WebApplication app, HostSettings host)
    {
        Page? page = Page.Load(host);
        string appPath = host.AppUrl.AbsolutePath.TrimEnd('/');
        app.MapWhen(context => ForApp(context.Request, host, appPath), branch => branch.Run(context => ServeAsync(context, appPath, page)));
        app.MapGet("/", () => TypedResults.Redirect(host.AppUrl.AbsoluteUri)).AllowAnonymous().ExcludeFromDescription();
    }

    // The app's requests: its path on the host's address, or anything on its own address.
    private static bool ForApp(HttpRequest request, HostSettings host, string appPath)
    {
        bool onAppHost = !host.AppElsewhere || string.Equals(request.Host.Host, host.AppUrl.IdnHost, StringComparison.OrdinalIgnoreCase);
        return onAppHost && (appPath.Length == 0 || request.Path.StartsWithSegments(appPath, StringComparison.Ordinal));
    }

    private static async Task ServeAsync(HttpContext context, string appPath, Page? page)
    {
        HttpResponse response = context.Response;
        response.Headers.XContentTypeOptions = "nosniff";
        if (page is null)
        {
            response.StatusCode = StatusCodes.Status404NotFound;
            await response.WriteAsync("This host's web app isn't built in: run npm run build in src/Apps/Web, or use sloth.", context.RequestAborted);
            return;
        }

        // One of the app's files, by its name in the build; never a path the request makes up.
        string path = context.Request.Path.Value![appPath.Length..].TrimStart('/');
        string? file = page.Files.GetValueOrDefault(path);
        if (file is not null)
        {
            bool known = ContentTypes.TryGetContentType(file, out string? contentType);
            response.ContentType = known ? contentType : "application/octet-stream";

            // Built assets have their content's hash in their names, so they never change.
            response.Headers.CacheControl = path.StartsWith("assets/", StringComparison.Ordinal) ? "public, max-age=31536000, immutable" : "no-cache";
            await response.SendFileAsync(file, context.RequestAborted);
            return;
        }

        // A missing asset is missing, never the page in its place.
        if (path.StartsWith("assets/", StringComparison.Ordinal))
        {
            response.StatusCode = StatusCodes.Status404NotFound;
            return;
        }

        response.ContentType = "text/html; charset=utf-8";
        response.Headers.CacheControl = "no-cache";
        response.Headers.ContentSecurityPolicy = page.ContentSecurityPolicy;
        response.Headers["Referrer-Policy"] = "strict-origin-when-cross-origin";
        await response.Body.WriteAsync(page.Html, context.RequestAborted);
    }

    // The app's page as this host serves it, and its other files by their paths in the build, read
    // once at startup.
    private sealed record Page(byte[] Html, string ContentSecurityPolicy, FrozenDictionary<string, string> Files)
    {
        public static Page? Load(HostSettings host)
        {
            string index = Path.Combine(host.AppFolder, "index.html");
            if (!File.Exists(index))
            {
                return null;
            }

            string html = File.ReadAllText(index, Encoding.UTF8);
            if (!html.Contains(BaseMarker, StringComparison.Ordinal) || !html.Contains(ApiMarker, StringComparison.Ordinal))
            {
                throw new InvalidOperationException(index + " lacks the base element or the API meta element the host fills in; build src/Apps/Web again.");
            }

            string api = host.PublicUrl.AbsoluteUri;
            html = html.Replace(BaseMarker, "<base href=\"" + host.AppUrl.AbsolutePath + "\" />", StringComparison.Ordinal)
                .Replace(ApiMarker, "<meta name=\"aisloth-api\" content=\"" + api + "\" />", StringComparison.Ordinal);

            // The page's own inline scripts (applying the theme before the first paint) run by their
            // hashes; everything else comes from the app's files. Workers highlight code with
            // WebAssembly, and components set styles inline.
            string scripts = string.Join(' ', InlineScripts().Matches(html).Select(match => "'sha256-" + Convert.ToBase64String(SHA256.HashData(Encoding.UTF8.GetBytes(match.Groups["body"].Value))) + "'"));
            string apiOrigin = host.PublicUrl.GetLeftPart(UriPartial.Authority);
            string policy = "default-src 'self'; "
                + "script-src 'self' 'wasm-unsafe-eval' " + scripts + "; "
                + "style-src 'self' 'unsafe-inline'; "
                + "img-src 'self' data: blob:; "
                + "font-src 'self' data:; "
                + "connect-src 'self' " + apiOrigin + "; "
                + "worker-src 'self' blob:; "
                + "object-src 'none'; base-uri 'self'; form-action 'self'; frame-ancestors 'none'";
            string folder = Path.GetFullPath(host.AppFolder);
            FrozenDictionary<string, string> files = Directory.EnumerateFiles(folder, "*", SearchOption.AllDirectories)
                .Select(file => (Name: Path.GetRelativePath(folder, file).Replace(Path.DirectorySeparatorChar, '/'), File: file))
                .Where(entry => !string.Equals(entry.Name, "index.html", StringComparison.Ordinal))
                .ToFrozenDictionary(entry => entry.Name, entry => entry.File, StringComparer.Ordinal);
            return new Page(Encoding.UTF8.GetBytes(html), policy, files);
        }
    }

    [GeneratedRegex("<script(?![^>]*\\bsrc=)[^>]*>(?<body>[\\s\\S]*?)</script>", RegexOptions.CultureInvariant | RegexOptions.ExplicitCapture, matchTimeoutMilliseconds: 1000)]
    private static partial Regex InlineScripts();
}
