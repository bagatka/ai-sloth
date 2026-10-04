using System;

namespace Bagatka.AiSloth.Workspaces;

/// <summary>
/// What the Workspaces module needs from its host.
/// </summary>
public sealed record WorkspacesSettings
{
    /// <summary>Creates the settings.</summary>
    /// <param name="connectionString">The PostgreSQL database that holds the <c>workspaces</c> schema.</param>
    public WorkspacesSettings(string connectionString)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(connectionString);
        ConnectionString = connectionString;
    }

    /// <summary>The PostgreSQL database that holds the <c>workspaces</c> schema.</summary>
    public string ConnectionString { get; }
}
