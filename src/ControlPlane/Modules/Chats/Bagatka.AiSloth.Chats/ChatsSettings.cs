using System;

namespace Bagatka.AiSloth.Chats;

/// <summary>
/// What the Chats module needs from its host.
/// </summary>
public sealed record ChatsSettings
{
    /// <summary>Creates the settings.</summary>
    /// <param name="connectionString">The PostgreSQL database that holds the <c>chats</c> schema.</param>
    /// <param name="modelGatewayUrl">
    /// The WebApi's model gateway as an agent in a nook reaches it, such as
    /// <c>http://host.docker.internal:5172/models</c>.
    /// </param>
    /// <param name="nearlyFullDisk">
    /// How full a nook's disk is, from 0 to 1, when a message for its agent needs the sender's
    /// confirmation; 1 asks only for a full disk.
    /// </param>
    public ChatsSettings(string connectionString, Uri modelGatewayUrl, double nearlyFullDisk = 0.9)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(connectionString);
        ArgumentNullException.ThrowIfNull(modelGatewayUrl);
        if (!modelGatewayUrl.IsAbsoluteUri || modelGatewayUrl.Scheme is not ("https" or "http"))
        {
            throw new ArgumentException("The model gateway URL must be an absolute http or https URL.", nameof(modelGatewayUrl));
        }

        ArgumentOutOfRangeException.ThrowIfNegative(nearlyFullDisk);
        ArgumentOutOfRangeException.ThrowIfGreaterThan(nearlyFullDisk, 1);
        ConnectionString = connectionString;
        ModelGatewayUrl = modelGatewayUrl;
        NearlyFullDisk = nearlyFullDisk;
    }

    /// <summary>The PostgreSQL database that holds the <c>chats</c> schema.</summary>
    public string ConnectionString { get; }

    /// <summary>The model gateway as an agent in a nook reaches it.</summary>
    public Uri ModelGatewayUrl { get; }

    /// <summary>How full a nook's disk is, from 0 to 1, when a message for its agent needs the sender's confirmation.</summary>
    public double NearlyFullDisk { get; }
}
