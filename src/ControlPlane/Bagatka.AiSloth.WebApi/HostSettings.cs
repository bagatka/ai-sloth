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
    public HostSettings(Uri publicUrl, string? name = null, bool? inviteSignUp = null)
    {
        ArgumentNullException.ThrowIfNull(publicUrl);
        if (!publicUrl.IsAbsoluteUri || publicUrl.Scheme is not ("https" or "http"))
        {
            throw new ArgumentException("The public URL must be an absolute http or https URL.", nameof(publicUrl));
        }

        PublicUrl = publicUrl;
        Name = string.IsNullOrWhiteSpace(name) ? "AiSloth" : name.Trim();
        InviteSignUp = inviteSignUp;
    }

    /// <summary>The address people and clients reach this host at.</summary>
    public Uri PublicUrl { get; }

    /// <summary>The host's name in clients.</summary>
    public string Name { get; }

    /// <summary>Whether an invite code signs up someone new; <see langword="null"/> to decide by whether a provider is configured.</summary>
    public bool? InviteSignUp { get; }
}
