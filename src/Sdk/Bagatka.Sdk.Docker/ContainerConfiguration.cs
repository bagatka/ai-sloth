using System.Collections.Generic;
using System.Collections.ObjectModel;

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
/// <param name="Runtime">The OCI runtime to run it with, one the engine lists in <see cref="DockerClient.ListRuntimesAsync"/>.</param>
/// <param name="Network">The network it joins, by name, such as one <see cref="DockerClient.CreateNetworkAsync"/> made.</param>
public sealed record ContainerConfiguration(
    string Image,
    IReadOnlyList<string> Environment,
    IReadOnlyDictionary<string, string> Labels,
    long NanoCpus,
    long MemoryBytes,
    IReadOnlyList<string> ExtraHosts,
    string Runtime,
    string Network)
{
    /// <summary>The command it runs instead of the image's; empty runs the image's.</summary>
    public IReadOnlyList<string> Command { get; init; } = [];

    /// <summary>Kernel capabilities it gets beyond Docker's defaults, such as <c>NET_ADMIN</c>.</summary>
    public IReadOnlyList<string> Capabilities { get; init; } = [];

    /// <summary>Kernel parameters of its own namespaces, such as <c>net.ipv4.ip_forward</c>.</summary>
    public IReadOnlyDictionary<string, string> Sysctls { get; init; } = ReadOnlyDictionary<string, string>.Empty;

    /// <summary>Networks it joins besides <see cref="Network"/>, by name; its default route never goes through them.</summary>
    public IReadOnlyList<string> ExtraNetworks { get; init; } = [];
}
