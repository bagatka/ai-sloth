using System;
using System.IO;

namespace Bagatka.AiSloth.WebApi;

/// <summary>
/// The host as people and its own clients see it: its address, its web app's, its name, and who may
/// sign up.
/// </summary>
internal sealed record HostSettings
{
    /// <summary>Creates the settings.</summary>
    /// <param name="publicUrl">
    /// The address people and clients reach this host at, such as <c>https://aisloth.example.com</c>:
    /// where an identity provider sends people back, and what the setup code's instructions name.
    /// </param>
    /// <param name="name">The host's name in clients; <c>AiSloth</c> when not given.</param>
    /// <param name="inviteSignUp">
    /// Whether an invite code also signs up someone new. When not given, it does exactly when no
    /// identity provider is configured: a host with single sign-on keeps everyone on its provider.
    /// </param>
    /// <param name="behindProxy">
    /// Whether every request comes through a proxy that adds the caller's address as the last
    /// <c>X-Forwarded-For</c> entry, such as Container Apps' ingress; the host then takes that address
    /// for the caller's. Off, the header is ignored, as anyone could write it.
    /// </param>
    /// <param name="signInsPerMinute">How many sign-in calls one address may make a minute: 30 unless given.</param>
    /// <param name="startsPerHour">How many chats and nooks one person may start an hour, 20 at once: 120 unless given.</param>
    /// <param name="appUrl">
    /// Where the web app is served, ending in a slash: another name of this host, such as
    /// <c>https://app.aisloth.dev/</c>, or a path on its address; <c>/app/</c> on its address unless given.
    /// </param>
    /// <param name="appFolder">
    /// The folder holding the built web app (<c>src/Apps/Web</c>'s <c>npm run build</c>); the
    /// <c>web</c> folder beside the WebApi unless given. Without it, the app's address says so.
    /// </param>
    public HostSettings(Uri publicUrl, string? name = null, bool? inviteSignUp = null, bool behindProxy = false, int? signInsPerMinute = null, int? startsPerHour = null, Uri? appUrl = null, string? appFolder = null)
    {
        ArgumentNullException.ThrowIfNull(publicUrl);
        if (!publicUrl.IsAbsoluteUri || publicUrl.Scheme is not ("https" or "http"))
        {
            throw new ArgumentException("The public URL must be an absolute http or https URL.", nameof(publicUrl));
        }

        PublicUrl = publicUrl;
        Name = string.IsNullOrWhiteSpace(name) ? "AiSloth" : name.Trim();
        InviteSignUp = inviteSignUp;
        BehindProxy = behindProxy;
        SignInsPerMinute = signInsPerMinute ?? 30;
        StartsPerHour = startsPerHour ?? 120;
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(SignInsPerMinute, nameof(signInsPerMinute));
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(StartsPerHour, nameof(startsPerHour));

        AppUrl = appUrl ?? new Uri(publicUrl, "/app/");
        if (!AppUrl.IsAbsoluteUri || AppUrl.Scheme is not ("https" or "http") || !AppUrl.AbsolutePath.EndsWith('/', StringComparison.Ordinal) || !string.IsNullOrEmpty(AppUrl.Query))
        {
            throw new ArgumentException("The app's URL must be an absolute http or https URL ending in a slash, without a query.", nameof(appUrl));
        }

        // On the host's own address, the app needs a path of its own: the API's paths are at the root.
        if (SameOrigin(AppUrl, PublicUrl) && string.Equals(AppUrl.AbsolutePath, "/", StringComparison.Ordinal))
        {
            throw new ArgumentException("On the host's own address, the app needs a path, such as /app/.", nameof(appUrl));
        }

        AppFolder = appFolder ?? Path.Combine(AppContext.BaseDirectory, "web");
    }

    /// <summary>The address people and clients reach this host at.</summary>
    public Uri PublicUrl { get; }

    /// <summary>The host's name in clients.</summary>
    public string Name { get; }

    /// <summary>Whether an invite code signs up someone new; <see langword="null"/> to decide by whether a provider is configured.</summary>
    public bool? InviteSignUp { get; }

    /// <summary>Whether a proxy in front adds the caller's address as the last X-Forwarded-For entry.</summary>
    public bool BehindProxy { get; }

    /// <summary>How many sign-in calls one address may make a minute.</summary>
    public int SignInsPerMinute { get; }

    /// <summary>How many chats and nooks one person may start an hour.</summary>
    public int StartsPerHour { get; }

    /// <summary>Where the web app is served, ending in a slash.</summary>
    public Uri AppUrl { get; }

    /// <summary>The folder holding the built web app.</summary>
    public string AppFolder { get; }

    /// <summary>Whether the web app is served from another origin than the API, so it calls the API across origins.</summary>
    public bool AppElsewhere => !SameOrigin(AppUrl, PublicUrl);

    /// <summary>Whether two addresses share a scheme, host, and port.</summary>
    public static bool SameOrigin(Uri left, Uri right)
    {
        ArgumentNullException.ThrowIfNull(left);
        ArgumentNullException.ThrowIfNull(right);
        return Uri.Compare(left, right, UriComponents.SchemeAndServer, UriFormat.Unescaped, StringComparison.OrdinalIgnoreCase) == 0;
    }
}
