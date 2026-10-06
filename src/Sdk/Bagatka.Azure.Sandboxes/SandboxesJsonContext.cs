using System.Text.Json.Serialization;

namespace Bagatka.Azure.Sandboxes;

/// <summary>
/// Source-generated JSON for the service, which names properties in camelCase; trimming and Native
/// AOT keep working.
/// </summary>
[JsonSourceGenerationOptions(PropertyNamingPolicy = JsonKnownNamingPolicy.CamelCase, DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull)]
[JsonSerializable(typeof(Wire.Sandbox))]
[JsonSerializable(typeof(Wire.SandboxPage))]
[JsonSerializable(typeof(Wire.CreateSandbox))]
[JsonSerializable(typeof(Wire.DiskImage))]
[JsonSerializable(typeof(Wire.DiskImagePage))]
[JsonSerializable(typeof(Wire.CreateDiskImage))]
[JsonSerializable(typeof(Wire.Commit))]
[JsonSerializable(typeof(Wire.CommitResult))]
internal sealed partial class SandboxesJsonContext : JsonSerializerContext;
