using System.Collections.Generic;
using System.Text.Json.Serialization;

namespace Bagatka.Sdk.Docker;

/// <summary>
/// Source-generated JSON for the Engine API, which uses the C# property names as they are.
/// </summary>
[JsonSourceGenerationOptions(DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull)]
[JsonSerializable(typeof(DockerWire.ContainerCreate))]
[JsonSerializable(typeof(DockerWire.NetworkCreate))]
[JsonSerializable(typeof(DockerWire.IdResponse))]
[JsonSerializable(typeof(DockerWire.WaitResponse))]
[JsonSerializable(typeof(DockerWire.ContainerInspect))]
[JsonSerializable(typeof(IReadOnlyList<DockerWire.ContainerSummary>))]
[JsonSerializable(typeof(DockerWire.ImageInspect))]
[JsonSerializable(typeof(IReadOnlyList<DockerWire.ImageSummary>))]
[JsonSerializable(typeof(DockerWire.Commit))]
[JsonSerializable(typeof(DockerWire.SystemInfo))]
[JsonSerializable(typeof(DockerWire.ErrorResponse))]
[JsonSerializable(typeof(DockerWire.PullProgress))]
[JsonSerializable(typeof(Dictionary<string, IReadOnlyList<string>>))]
internal sealed partial class DockerJsonContext : JsonSerializerContext;
