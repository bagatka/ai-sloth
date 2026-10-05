using System.Text.Json.Serialization;

namespace Bagatka.AiSloth.Cli;

// Source-generated JSON in GitHub's snake_case, as Native AOT requires.
[JsonSourceGenerationOptions(PropertyNamingPolicy = JsonKnownNamingPolicy.SnakeCaseLower)]
[JsonSerializable(typeof(GitHubAppManifest.Manifest))]
[JsonSerializable(typeof(GitHubAppManifest.Conversion))]
internal sealed partial class GitHubJsonContext : JsonSerializerContext;
