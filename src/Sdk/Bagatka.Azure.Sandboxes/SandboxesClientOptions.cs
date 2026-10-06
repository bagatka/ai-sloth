using System;
using System.Diagnostics.CodeAnalysis;
using Azure.Core;

namespace Bagatka.Azure.Sandboxes;

/// <summary>
/// Options for <see cref="SandboxGroupClient"/>: the service version, and the Azure.Core pipeline's
/// retries, logging, and transport.
/// </summary>
public sealed class SandboxesClientOptions : ClientOptions
{
    /// <summary>The newest service version this client knows.</summary>
    public const ServiceVersion LatestVersion = ServiceVersion.V2026_09_01_Preview;

    /// <summary>Creates the options.</summary>
    /// <param name="version">The service version to call.</param>
    public SandboxesClientOptions(ServiceVersion version = LatestVersion)
    {
        Version = version switch
        {
            ServiceVersion.V2026_09_01_Preview => "2026-09-01-preview",
            _ => throw new NotSupportedException("This client doesn't know service version " + version + "."),
        };
    }

    /// <summary>The versions of the service's data plane this client can call.</summary>
    [SuppressMessage("Naming", "CA1707:Identifiers should not contain underscores", Justification = "Azure SDKs name service versions V<yyyy>_<MM>_<dd>, which their users know.")]
    public enum ServiceVersion
    {
        /// <summary>2026-09-01-preview.</summary>
        V2026_09_01_Preview = 1,
    }

    /// <summary>
    /// The OAuth scope tokens are requested for: the service's own, unless a sovereign cloud needs
    /// another.
    /// </summary>
    public string Scope { get; set; } = "https://dynamicsessions.io/.default";

    internal string Version { get; }
}
