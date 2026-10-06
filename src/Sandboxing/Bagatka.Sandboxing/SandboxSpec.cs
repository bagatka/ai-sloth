using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Security.Cryptography;
using System.Text;

namespace Bagatka.Sandboxing;

/// <summary>
/// What to create.
/// </summary>
/// <param name="Key">The caller's identifier for the sandbox.</param>
/// <param name="Source">What the sandbox starts from: an image, or a snapshot of another sandbox's files.</param>
/// <param name="Resources">The compute the sandbox gets.</param>
/// <param name="Environment">
/// Environment variables for the entry point. Names are valid variable names (non-empty, no
/// <c>=</c>); callers validate names people enter. Values may hold secrets, so providers never log
/// them.
/// </param>
/// <param name="Location">
/// Where within the backend the sandbox runs, such as a region or one of the backend's machines;
/// <see langword="null"/> for a backend with one place. A provider rejects a location it doesn't
/// have with a validation error. Operations by key find the sandbox wherever it was created.
/// </param>
public sealed record SandboxSpec(
    SandboxKey Key,
    SandboxSource Source,
    SandboxResources Resources,
    IReadOnlyDictionary<string, string> Environment,
    string? Location)
{
    /// <summary>
    /// What makes a repeated create the same sandbox: the source, resources, and environment, as 64
    /// lowercase hex characters. A provider stores it with the sandbox and compares it when a create
    /// is repeated. It is derived from the environment's values but never reveals them.
    /// </summary>
    public string Fingerprint()
    {
        StringBuilder canonical = new StringBuilder();
        canonical.Append(Source.Value switch
        {
            SandboxImage image => "image:" + image.Reference,
            SnapshotKey snapshot => "snapshot:" + snapshot.Value.ToString("N", CultureInfo.InvariantCulture),
            _ => throw new InvalidOperationException("The sandbox source is default."),
        });
        canonical.Append('\n').Append(Resources.CpuMillicores.ToString(CultureInfo.InvariantCulture));
        canonical.Append('\n').Append(Resources.MemoryMebibytes.ToString(CultureInfo.InvariantCulture));
        foreach (KeyValuePair<string, string> variable in Environment.OrderBy(variable => variable.Key, StringComparer.Ordinal))
        {
            canonical.Append('\n').Append(variable.Key).Append('=').Append(variable.Value);
        }

        return Convert.ToHexStringLower(SHA256.HashData(Encoding.UTF8.GetBytes(canonical.ToString())));
    }
}
