using System;

namespace Bagatka.AiSloth.Users;

/// <summary>
/// What the Users module needs from its host.
/// </summary>
public sealed record UsersSettings
{
    /// <summary>Creates the settings.</summary>
    /// <param name="connectionString">The PostgreSQL database that holds the <c>users</c> schema.</param>
    public UsersSettings(string connectionString)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(connectionString);
        ConnectionString = connectionString;
    }

    /// <summary>The PostgreSQL database that holds the <c>users</c> schema.</summary>
    public string ConnectionString { get; }
}
