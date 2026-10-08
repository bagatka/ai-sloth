using System;

namespace Bagatka.Foundation.Modules;

/// <summary>
/// The PostgreSQL database every module and the instance lease share, each in a schema of its own.
/// </summary>
public sealed record DatabaseSettings
{
    /// <summary>Creates the settings.</summary>
    /// <param name="connectionString">
    /// Npgsql's connection string. Its <c>Maximum Pool Size</c> (100 unless given) bounds the
    /// connections each instance opens; during a deploy two instances run at once.
    /// </param>
    public DatabaseSettings(string connectionString)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(connectionString);
        ConnectionString = connectionString;
    }

    /// <summary>Npgsql's connection string.</summary>
    public string ConnectionString { get; }

    /// <inheritdoc />
    public override string ToString()
    {
        return "DatabaseSettings { ConnectionString = *** }";
    }
}
