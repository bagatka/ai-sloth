namespace Bagatka.Sandboxing;

/// <summary>
/// What a sandbox starts from: an OCI image, or a snapshot of another sandbox's files.
/// </summary>
public union SandboxSource(SandboxImage, SnapshotKey);
