using System;

namespace Bagatka.PostHog;

/// <summary>
/// Where a <see cref="PostHogClient"/> sends, and how often.
/// </summary>
public sealed record PostHogClientOptions
{
    /// <summary>Creates the options.</summary>
    /// <param name="host">
    /// The project's ingestion host: <c>https://eu.i.posthog.com</c> or <c>https://us.i.posthog.com</c>
    /// for PostHog Cloud, or a self-hosted PostHog's address.
    /// </param>
    /// <param name="projectToken">The project's token, <c>phc_…</c>: public, and able only to send.</param>
    public PostHogClientOptions(Uri host, string projectToken)
    {
        ArgumentNullException.ThrowIfNull(host);
        ArgumentException.ThrowIfNullOrWhiteSpace(projectToken);
        if (!host.IsAbsoluteUri || host.Scheme is not ("https" or "http"))
        {
            throw new ArgumentException("The host must be an absolute http or https URL, such as https://eu.i.posthog.com.", nameof(host));
        }

        Host = host;
        ProjectToken = projectToken;
    }

    /// <summary>The project's ingestion host.</summary>
    public Uri Host { get; }

    /// <summary>The project's token.</summary>
    public string ProjectToken { get; }

    /// <summary>How long captured events wait to be sent together: five seconds unless given.</summary>
    public TimeSpan FlushInterval { get; init; } = TimeSpan.FromSeconds(5);

    /// <summary>The clock events are stamped and batches are timed by: the system's unless given.</summary>
    public TimeProvider TimeProvider { get; init; } = TimeProvider.System;

    /// <summary>
    /// Told when events are lost: PostHog refused them, stayed out of reach after retries, or the
    /// queue was full. It runs on the client's background work and must not throw or block.
    /// </summary>
    public Action<PostHogDeliveryFailure>? DeliveryFailed { get; init; }
}
