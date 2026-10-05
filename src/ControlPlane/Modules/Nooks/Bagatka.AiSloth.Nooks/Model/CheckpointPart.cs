namespace Bagatka.AiSloth.Nooks.Model;

// One place a checkpoint keeps, as a snapshot commit (NooksApi.Checkpoints.cs): a git repository
// directly in /work, by its folder; /work itself, without those repositories; or `/`, holding the
// nook's kept paths. The commit's objects are in the bundle of the checkpoint that first kept it,
// which needs the bundle of its previous commit, and so on back to a bundle that needs none.
internal sealed class CheckpointPart
{
    public const int MaxPathLength = 300;

    // Used by the constructor and by EF: parameter names match property names.
    public CheckpointPart(CheckpointId checkpointId, string path, string commit, string? previous, string? objectKey)
    {
        CheckpointId = checkpointId;
        Path = path;
        Commit = commit;
        Previous = previous;
        ObjectKey = objectKey;
    }

    public CheckpointId CheckpointId { get; private set; }

    public string Path { get; private set; }

    public string Commit { get; private set; }

    // The commit the bundle builds on, which must be fetched first; null for a bundle that needs none.
    public string? Previous { get; private set; }

    // The bundle that brought the commit; null when an earlier checkpoint's did, because nothing
    // changed here since.
    public string? ObjectKey { get; private set; }
}
