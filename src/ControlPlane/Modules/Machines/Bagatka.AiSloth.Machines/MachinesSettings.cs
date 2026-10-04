using System;

namespace Bagatka.AiSloth.Machines;

/// <summary>
/// What the Machines module needs from its host.
/// </summary>
public sealed record MachinesSettings
{
    /// <summary>Creates the settings.</summary>
    /// <param name="connectionString">The PostgreSQL database that holds the <c>machines</c> schema.</param>
    public MachinesSettings(string connectionString)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(connectionString);
        ConnectionString = connectionString;
    }

    /// <summary>The PostgreSQL database that holds the <c>machines</c> schema.</summary>
    public string ConnectionString { get; }
}
