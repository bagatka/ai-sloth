using System.Collections.Generic;
using System.Text.Json.Serialization;

namespace Bagatka.Sdk.GitHub;

// Source-generated JSON for GitHub's answers and requests, in its snake_case.
[JsonSourceGenerationOptions(PropertyNamingPolicy = JsonKnownNamingPolicy.SnakeCaseLower)]
[JsonSerializable(typeof(GitHubWire.DeviceCodeResponse))]
[JsonSerializable(typeof(GitHubWire.TokenResponse))]
[JsonSerializable(typeof(GitHubWire.User))]
[JsonSerializable(typeof(GitHubWire.InstallationPage))]
[JsonSerializable(typeof(GitHubWire.Repository))]
[JsonSerializable(typeof(GitHubWire.RepositoryPage))]
[JsonSerializable(typeof(GitHubWire.PullRequest))]
[JsonSerializable(typeof(IReadOnlyList<GitHubWire.PullRequest>))]
[JsonSerializable(typeof(GitHubWire.NewPullRequest))]
[JsonSerializable(typeof(GitHubWire.Problem))]
internal sealed partial class GitHubJsonContext : JsonSerializerContext;
