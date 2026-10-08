using System.Collections.Generic;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace Bagatka.Sdk.Docker;

/// <summary>
/// The Engine API's JSON shapes, only as much as this client reads and writes.
/// </summary>
internal static class DockerWire
{
    internal sealed record ContainerCreate(
        string Image,
        IReadOnlyList<string> Env,
        IReadOnlyDictionary<string, string> Labels,
        IReadOnlyList<string>? Cmd,
        HostConfig HostConfig,
        NetworkingConfig NetworkingConfig);

    internal sealed record HostConfig(
        long NanoCpus,
        long Memory,
        IReadOnlyList<string> ExtraHosts,
        string Runtime,
        string NetworkMode,
        IReadOnlyList<string> CapAdd,
        IReadOnlyDictionary<string, string> Sysctls);

    internal sealed record NetworkingConfig(IReadOnlyDictionary<string, EndpointSettings> EndpointsConfig);

    internal sealed record EndpointSettings(int GwPriority);

    internal sealed record NetworkCreate(
        string Name,
        string Driver,
        IReadOnlyDictionary<string, string> Options,
        IReadOnlyDictionary<string, string> Labels,
        [property: JsonPropertyName("IPAM")] Ipam Ipam);

    internal sealed record Ipam(IReadOnlyList<IpamConfig> Config);

    internal sealed record IpamConfig(string Subnet);

    internal sealed record WaitResponse(int StatusCode);

    internal sealed record SystemInfo(IReadOnlyDictionary<string, JsonElement>? Runtimes);

    internal sealed record IdResponse(string Id);

    internal sealed record ContainerInspect(string Id, string Name, string Created, string Image, ContainerState State, Config? Config);

    internal sealed record ContainerState(string Status, int ExitCode, string? Error);

    internal sealed record Config(IReadOnlyList<string>? Env, IReadOnlyDictionary<string, string>? Labels);

    internal sealed record ContainerSummary(
        string Id,
        IReadOnlyDictionary<string, string>? Labels,
        string State,
        string Status,
        long Created);

    internal sealed record ImageInspect(string Id, IReadOnlyList<string>? RepoTags, Config? Config);

    internal sealed record ImageSummary(
        string Id,
        IReadOnlyList<string>? RepoTags,
        IReadOnlyDictionary<string, string>? Labels,
        long Created);

    internal sealed record Commit(IReadOnlyList<string> Env, IReadOnlyDictionary<string, string> Labels);

    internal sealed record ErrorResponse([property: JsonPropertyName("message")] string? Message);

    internal sealed record PullProgress([property: JsonPropertyName("error")] string? Error);
}
