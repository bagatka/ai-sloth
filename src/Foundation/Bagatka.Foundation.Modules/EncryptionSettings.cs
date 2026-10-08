using System;

namespace Bagatka.Foundation.Modules;

/// <summary>
/// The deployment's encryption key, from which each module that keeps secrets at rest derives its own
/// (<see cref="SecretBox"/>).
/// </summary>
public sealed record EncryptionSettings
{
    /// <summary>Creates the settings.</summary>
    /// <param name="key">
    /// At least 32 random characters. Changing it makes every stored secret unreadable: agent accounts'
    /// secrets, people's GitHub tokens, and secrets' values.
    /// </param>
    public EncryptionSettings(string key)
    {
        ArgumentNullException.ThrowIfNull(key);
        if (key.Length < 32)
        {
            throw new ArgumentException("The encryption key must be at least 32 characters.", nameof(key));
        }

        Key = key;
    }

    /// <summary>The deployment's encryption key.</summary>
    public string Key { get; }

    /// <inheritdoc />
    public override string ToString()
    {
        return "EncryptionSettings { Key = *** }";
    }
}
