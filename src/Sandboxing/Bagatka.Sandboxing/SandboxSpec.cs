using System.Collections.Generic;

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
    string? Location);
