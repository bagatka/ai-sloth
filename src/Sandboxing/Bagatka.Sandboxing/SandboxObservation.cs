using System;

namespace Bagatka.Sandboxing;

/// <summary>
/// A sandbox as its backend reports it at one moment.
/// </summary>
/// <param name="Key">The caller's identifier for the sandbox.</param>
/// <param name="State">The backend's view of the sandbox.</param>
/// <param name="CreatedAt">
/// When the backend created it. Reconcilers use it to leave sandboxes that are still being created alone.
/// </param>
/// <param name="Reason">Why the sandbox <see cref="SandboxState.Failed"/>, in the backend's words; otherwise <see langword="null"/>.</param>
public sealed record SandboxObservation(
    SandboxKey Key,
    SandboxState State,
    DateTimeOffset CreatedAt,
    string? Reason);
