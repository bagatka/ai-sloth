using System;
using System.Text.Json.Serialization;
using Bagatka.Foundation;

namespace Bagatka.AiSloth.Workspaces.Contracts;

/// <summary>
/// Identifies a workspace.
/// </summary>
[JsonConverter(typeof(TypedIdJsonConverter<WorkspaceId>))]
public readonly record struct WorkspaceId : ITypedId<WorkspaceId>
{
    private WorkspaceId(Guid value)
    {
        Value = value;
    }

    /// <inheritdoc />
    public Guid Value { get; }

    /// <summary>Creates a new, time-ordered (UUIDv7) ID.</summary>
    public static WorkspaceId New()
    {
        return new WorkspaceId(Guid.CreateVersion7());
    }

    /// <inheritdoc />
    public static WorkspaceId From(Guid value)
    {
        return new WorkspaceId(value);
    }
}
