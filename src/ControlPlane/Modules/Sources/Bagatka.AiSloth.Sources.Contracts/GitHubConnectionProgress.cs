using System;

namespace Bagatka.AiSloth.Sources.Contracts;

/// <summary>Where a GitHub connection stands: connected, or still waiting for the person.</summary>
/// <param name="Account">The connected account; <see langword="null"/> while the person hasn't approved yet.</param>
/// <param name="RetryAfter">How long to wait before completing again, while waiting.</param>
public sealed record GitHubConnectionProgress(GitHubAccount? Account, TimeSpan RetryAfter);
