using System;

namespace Bagatka.Azure.Sandboxes.Models;

/// <summary>
/// Whether a disk image can start sandboxes yet. The service may add values; one this version doesn't know is kept as it came.
/// </summary>
public readonly struct DiskImageState : IEquatable<DiskImageState>
{
    private const string ReadyValue = "Ready";
    private const string FailedValue = "Failed";

    private readonly string? _value;

    /// <summary>Creates the value from its name at the service, such as <c>Ready</c>.</summary>
    /// <param name="value">The service's name for it.</param>
    public DiskImageState(string value)
    {
        ArgumentNullException.ThrowIfNull(value);
        _value = value;
    }

    /// <summary>Ready to start sandboxes.</summary>
    public static DiskImageState Ready { get; } = new DiskImageState(ReadyValue);

    /// <summary>Making it failed; its error message says why.</summary>
    public static DiskImageState Failed { get; } = new DiskImageState(FailedValue);

    /// <summary>Whether two values are the same, ignoring case.</summary>
    public static bool operator ==(DiskImageState left, DiskImageState right)
    {
        return left.Equals(right);
    }

    /// <summary>Whether two values differ, ignoring case.</summary>
    public static bool operator !=(DiskImageState left, DiskImageState right)
    {
        return !left.Equals(right);
    }

    /// <inheritdoc />
    public bool Equals(DiskImageState other)
    {
        return string.Equals(_value, other._value, StringComparison.OrdinalIgnoreCase);
    }

    /// <inheritdoc />
    public override bool Equals(object? obj)
    {
        return obj is DiskImageState other && Equals(other);
    }

    /// <inheritdoc />
    public override int GetHashCode()
    {
        return _value is null ? 0 : StringComparer.OrdinalIgnoreCase.GetHashCode(_value);
    }

    /// <summary>The service's name for the value.</summary>
    public override string ToString()
    {
        return _value ?? string.Empty;
    }
}
