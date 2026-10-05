using System;
using System.Globalization;
using Bagatka.AiSloth.Nooks.Contracts;

namespace Bagatka.AiSloth.Nooks.Model;

// A nook's files at one moment: one part for each place it keeps (CheckpointPart), whose bundles are
// in object storage under the nook's prefix. Kept until the nook is deleted.
internal sealed class Checkpoint
{
    public const int MaxNoteLength = 200;

    // Used by Take and by EF: parameter names match property names.
    private Checkpoint(CheckpointId id, NookId nookId, int number, DateTimeOffset createdAt, string note)
    {
        Id = id;
        NookId = nookId;
        Number = number;
        CreatedAt = createdAt;
        Note = note;
    }

    public CheckpointId Id { get; private set; }

    public NookId NookId { get; private set; }

    // Counts from 1 in each nook; one checkpoint is taken at a time per nook (FileLocks).
    public int Number { get; private set; }

    public DateTimeOffset CreatedAt { get; private set; }

    public string Note { get; private set; }

    public static Checkpoint Take(NookId nookId, int number, string note, TimeProvider time)
    {
        return new Checkpoint(CheckpointId.New(), nookId, number, time.GetUtcNow(), note);
    }

    // Where the bundles of a nook's checkpoint are stored; everything under the nook's prefix goes with it.
    public static string ObjectKey(NookId nookId, int number, string bundle)
    {
        return Prefix(nookId) + "/checkpoints/" + number.ToString(CultureInfo.InvariantCulture) + "/" + bundle;
    }

    public static string Prefix(NookId nookId)
    {
        return "nooks/" + nookId.Value.ToString("D", CultureInfo.InvariantCulture);
    }

    public CheckpointSummary ToSummary()
    {
        return new CheckpointSummary(NookId, Number, CreatedAt, Note);
    }
}
