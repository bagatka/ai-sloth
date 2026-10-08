using System;

namespace Bagatka.AiSloth.WebApi;

/// <summary>
/// The host as people and its own clients see it: its address, its name, and who may sign up.
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
    public HostSettings(Uri publicUrl, string? name = null, bool? inviteSignUp = null, bool behindProxy = false, int? signInsPerMinute = null, int? startsPerHour = null)
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
}
