using System;

namespace Bagatka.AiSloth.Secrets;

/// <summary>
/// What the Secrets module needs from its host.
/// </summary>
public sealed record SecretsSettings
{
    /// <summary>Creates the settings.</summary>
    /// <param name="connectionString">The PostgreSQL database that holds the <c>secrets</c> schema.</param>
    /// <param name="encryptionKey">
    /// The secret that encrypts secrets' values at rest: at least 32 random characters. Changing it
    /// makes every stored value unreadable.
    /// </param>
    public SecretsSettings(string connectionString, string encryptionKey)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(connectionString);
        ArgumentNullException.ThrowIfNull(encryptionKey);
        if (encryptionKey.Length < 32)
        {
            throw new ArgumentException("The encryption key must be at least 32 characters.", nameof(encryptionKey));
        }

        ConnectionString = connectionString;
        EncryptionKey = encryptionKey;
    }

    /// <summary>The PostgreSQL database that holds the <c>secrets</c> schema.</summary>
    public string ConnectionString { get; }

    /// <summary>The secret that encrypts secrets' values at rest.</summary>
    public string EncryptionKey { get; }

    /// <inheritdoc />
    public override string ToString()
    {
        return "SecretsSettings { EncryptionKey = *** }";
    }
}
