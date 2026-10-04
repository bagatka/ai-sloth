using System;

namespace Bagatka.AiSloth.AgentAccounts;

/// <summary>
/// What the AgentAccounts module needs from its host.
/// </summary>
public sealed record AgentAccountsSettings
{
    /// <summary>Creates the settings.</summary>
    /// <param name="connectionString">The PostgreSQL database that holds the <c>agent_accounts</c> schema.</param>
    /// <param name="encryptionKey">
    /// The secret that encrypts accounts' secrets at rest: at least 32 random characters. Changing it
    /// makes every stored secret unreadable.
    /// </param>
    /// <param name="allowClaudeSubscriptions">
    /// Whether people may add Claude subscriptions. Off unless Anthropic has given this deployment
    /// written permission: its terms forbid storing Claude sign-in tokens otherwise.
    /// </param>
    /// <param name="sharePersonalAccounts">
    /// Whether a personal account's owner may let other people message chats running on it. Off,
    /// because vendors' plans are for one person; turn it on only where a vendor's terms allow it.
    /// </param>
    public AgentAccountsSettings(string connectionString, string encryptionKey, bool allowClaudeSubscriptions = false, bool sharePersonalAccounts = false)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(connectionString);
        ArgumentNullException.ThrowIfNull(encryptionKey);
        if (encryptionKey.Length < 32)
        {
            throw new ArgumentException("The encryption key must be at least 32 characters.", nameof(encryptionKey));
        }

        ConnectionString = connectionString;
        EncryptionKey = encryptionKey;
        AllowClaudeSubscriptions = allowClaudeSubscriptions;
        SharePersonalAccounts = sharePersonalAccounts;
    }

    /// <summary>The PostgreSQL database that holds the <c>agent_accounts</c> schema.</summary>
    public string ConnectionString { get; }

    /// <summary>The secret that encrypts accounts' secrets at rest.</summary>
    public string EncryptionKey { get; }

    /// <summary>Whether people may add Claude subscriptions.</summary>
    public bool AllowClaudeSubscriptions { get; }

    /// <summary>Whether a personal account's owner may let other people message chats running on it.</summary>
    public bool SharePersonalAccounts { get; }

    /// <inheritdoc />
    public override string ToString()
    {
        return "AgentAccountsSettings { EncryptionKey = *** }";
    }
}
