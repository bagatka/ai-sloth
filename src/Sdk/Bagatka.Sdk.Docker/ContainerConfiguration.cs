using System.Collections.Generic;

namespace Bagatka.Sdk.Docker;

/// <summary>
/// What to create a container from.
/// </summary>
/// <param name="Image">The image reference or ID; it must already be present.</param>
/// <param name="Environment">Environment variables as <c>NAME=value</c>.</param>
/// <param name="Labels">Labels to attach.</param>
/// <param name="NanoCpus">CPU quota in billionths of a CPU.</param>
/// <param name="MemoryBytes">Memory limit in bytes.</param>
/// <param name="ExtraHosts">Extra <c>/etc/hosts</c> entries as <c>name:address</c>.</param>
public sealed record ContainerConfiguration(
    string Image,
    IReadOnlyList<string> Environment,
    IReadOnlyDictionary<string, string> Labels,
    long NanoCpus,
    long MemoryBytes,
    IReadOnlyList<string> ExtraHosts);
