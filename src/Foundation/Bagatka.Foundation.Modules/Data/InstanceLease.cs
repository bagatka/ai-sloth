using System;

namespace Bagatka.Foundation.Modules.Data;

// The one row naming the control-plane instance that does background work, until when.
internal sealed class InstanceLease
{
    public const int Only = 1;

    public int Id { get; init; }

    public Guid? Holder { get; init; }

    public DateTimeOffset ExpiresAt { get; init; }
}
