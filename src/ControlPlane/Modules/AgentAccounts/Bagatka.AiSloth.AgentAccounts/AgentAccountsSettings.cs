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
    public AgentAccountsSettings(string connectionString, string encryptionKey, bool allowClaudeSubscriptions = false)
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
    }

    /// <summary>The PostgreSQL database that holds the <c>agent_accounts</c> schema.</summary>
    public string ConnectionString { get; }

    /// <summary>The secret that encrypts accounts' secrets at rest.</summary>
    public string EncryptionKey { get; }

    /// <summary>Whether people may add Claude subscriptions.</summary>
    public bool AllowClaudeSubscriptions { get; }

    /// <inheritdoc />
    public override string ToString()
    {
        return "AgentAccountsSettings { EncryptionKey = *** }";
    }
}
