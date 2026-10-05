using System;
using Bagatka.Sdk.OpenAI;

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
    /// <param name="allowClaudePlans">
    /// Whether people may add Claude plans. Off unless Anthropic has given this deployment
    /// written permission: its terms forbid storing Claude sign-in tokens otherwise.
    /// </param>
    /// <param name="allowChatGptPlans">
    /// Whether people may sign in with ChatGPT to add their plans. OpenAI lets open-source and
    /// self-hosted deployments do so; a hosted service for other people needs OpenAI's approval first.
    /// </param>
    /// <param name="chatGptAuthority">OpenAI's authorization server; <see langword="null"/> for <c>https://auth.openai.com</c>. Tests change it.</param>
    /// <param name="chatGptApi">The API a ChatGPT plan's calls go to; <see langword="null"/> for <c>https://api.openai.com/v1</c>. Tests change it.</param>
    public AgentAccountsSettings(
        string connectionString,
        string encryptionKey,
        bool allowClaudePlans = false,
        bool allowChatGptPlans = false,
        Uri? chatGptAuthority = null,
        Uri? chatGptApi = null)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(connectionString);
        ArgumentNullException.ThrowIfNull(encryptionKey);
        if (encryptionKey.Length < 32)
        {
            throw new ArgumentException("The encryption key must be at least 32 characters.", nameof(encryptionKey));
        }

        ConnectionString = connectionString;
        EncryptionKey = encryptionKey;
        AllowClaudePlans = allowClaudePlans;
        AllowChatGptPlans = allowChatGptPlans;
        ChatGptAuthority = chatGptAuthority ?? new Uri("https://auth.openai.com");
        ChatGptApi = chatGptApi ?? ChatGptSignInClient.Resource;
        ChatGptSignIn = new ChatGptSignInSettings(ChatGptAuthority);
    }

    /// <summary>The PostgreSQL database that holds the <c>agent_accounts</c> schema.</summary>
    public string ConnectionString { get; }

    /// <summary>The secret that encrypts accounts' secrets at rest.</summary>
    public string EncryptionKey { get; }

    /// <summary>Whether people may add Claude plans.</summary>
    public bool AllowClaudePlans { get; }

    /// <summary>Whether people may sign in with ChatGPT to add their plans.</summary>
    public bool AllowChatGptPlans { get; }

    /// <summary>OpenAI's authorization server.</summary>
    public Uri ChatGptAuthority { get; }

    /// <summary>The API a ChatGPT plan's calls go to.</summary>
    public Uri ChatGptApi { get; }

    /// <summary>The Sign in with ChatGPT client's settings.</summary>
    public ChatGptSignInSettings ChatGptSignIn { get; }

    /// <inheritdoc />
    public override string ToString()
    {
        return "AgentAccountsSettings { EncryptionKey = *** }";
    }
}
