using System;
using Bagatka.Foundation;

namespace Bagatka.AiSloth.Nooks.Model;

// Identifies a checkpoint inside this module, for keyset pages; callers know it by its nook and number.
internal readonly record struct CheckpointId : ITypedId<CheckpointId>
{
    private CheckpointId(Guid value)
    {
        Value = value;
    }

    public Guid Value { get; }

    public static CheckpointId New()
    {
        return new CheckpointId(Guid.CreateVersion7());
    }

    public static CheckpointId From(Guid value)
    {
        return new CheckpointId(value);
    }
}
