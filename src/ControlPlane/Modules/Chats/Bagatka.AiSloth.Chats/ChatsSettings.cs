using System;

namespace Bagatka.AiSloth.Chats;

/// <summary>
/// What the Chats module needs from its host.
/// </summary>
public sealed record ChatsSettings
{
    /// <summary>Creates the settings.</summary>
    /// <param name="modelGatewayUrl">
    /// The WebApi's model gateway as an agent in a nook reaches it, such as
    /// <c>http://host.docker.internal:5172/models</c>.
    /// </param>
    /// <param name="draftLifetime">
    /// How long a chat nobody wrote in yet, a draft, keeps its nook before both go; 30 minutes unless given.
    /// </param>
    public ChatsSettings(Uri modelGatewayUrl, TimeSpan? draftLifetime = null)
    {
        ArgumentNullException.ThrowIfNull(modelGatewayUrl);
        if (!modelGatewayUrl.IsAbsoluteUri || modelGatewayUrl.Scheme is not ("https" or "http"))
        {
            throw new ArgumentException("The model gateway URL must be an absolute http or https URL.", nameof(modelGatewayUrl));
        }

        DraftLifetime = draftLifetime ?? TimeSpan.FromMinutes(30);
        ArgumentOutOfRangeException.ThrowIfLessThanOrEqual(DraftLifetime, TimeSpan.Zero, nameof(draftLifetime));
        ModelGatewayUrl = modelGatewayUrl;
    }

    /// <summary>The model gateway as an agent in a nook reaches it.</summary>
    public Uri ModelGatewayUrl { get; }

    /// <summary>How long a draft keeps its nook before both go.</summary>
    public TimeSpan DraftLifetime { get; }
}
