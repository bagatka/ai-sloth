using System;

namespace Bagatka.AiSloth.Sources;

/// <summary>
/// What the Sources module needs from its host.
/// </summary>
public sealed record SourcesSettings
{
    /// <summary>Creates the settings.</summary>
    /// <param name="connectionString">The PostgreSQL database that holds the <c>sources</c> schema.</param>
    /// <param name="encryptionKey">
    /// The secret that encrypts people's GitHub tokens at rest: at least 32 random characters. Changing
    /// it makes every stored token unreadable, so everyone connects GitHub again.
    /// </param>
    /// <param name="gitHubApp">The host's GitHub App, or <see langword="null"/> for a host without one.</param>
    public SourcesSettings(string connectionString, string encryptionKey, SourcesGitHubApp? gitHubApp = null)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(connectionString);
        ArgumentNullException.ThrowIfNull(encryptionKey);
        if (encryptionKey.Length < 32)
        {
            throw new ArgumentException("The encryption key must be at least 32 characters.", nameof(encryptionKey));
        }

        ConnectionString = connectionString;
        EncryptionKey = encryptionKey;
        GitHubApp = gitHubApp;
    }

    /// <summary>The PostgreSQL database that holds the <c>sources</c> schema.</summary>
    public string ConnectionString { get; }

    /// <summary>The secret that encrypts people's GitHub tokens at rest.</summary>
    public string EncryptionKey { get; }

    /// <summary>The host's GitHub App, if it has one.</summary>
    public SourcesGitHubApp? GitHubApp { get; }

    /// <inheritdoc />
    public override string ToString()
    {
        return "SourcesSettings { EncryptionKey = ***, GitHubApp = " + GitHubApp + " }";
    }
}
