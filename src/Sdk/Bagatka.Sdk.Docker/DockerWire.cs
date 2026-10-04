using System.Collections.Generic;
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
        HostConfig HostConfig);

    internal sealed record HostConfig(long NanoCpus, long Memory, IReadOnlyList<string> ExtraHosts);

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
