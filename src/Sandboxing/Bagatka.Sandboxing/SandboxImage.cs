namespace Bagatka.Sandboxing;

/// <summary>
/// An OCI image. Its entry point is the caller's process.
/// </summary>
/// <param name="Reference">The image reference, such as <c>ghcr.io/bagatka/aisloth-nook:1.4</c>.</param>
public sealed record SandboxImage(string Reference);
