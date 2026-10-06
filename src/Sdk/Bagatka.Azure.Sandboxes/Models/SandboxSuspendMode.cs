using System;

namespace Bagatka.Azure.Sandboxes.Models;

/// <summary>
/// What a suspended sandbox keeps. The service may add values; one this version doesn't know is kept as it came.
/// </summary>
public readonly struct SandboxSuspendMode : IEquatable<SandboxSuspendMode>
{
    private const string MemoryValue = "Memory";
    private const string DiskValue = "Disk";
    private const string NoneValue = "None";

    private readonly string? _value;

    /// <summary>Creates the value from its name at the service, such as <c>Memory</c>.</summary>
    /// <param name="value">The service's name for it.</param>
    public SandboxSuspendMode(string value)
    {
        ArgumentNullException.ThrowIfNull(value);
        _value = value;
    }

    /// <summary>Its memory and disk: it resumes with its processes running.</summary>
    public static SandboxSuspendMode Memory { get; } = new SandboxSuspendMode(MemoryValue);

    /// <summary>Its disk: it resumes with its processes started again.</summary>
    public static SandboxSuspendMode Disk { get; } = new SandboxSuspendMode(DiskValue);

    /// <summary>Nothing.</summary>
    public static SandboxSuspendMode None { get; } = new SandboxSuspendMode(NoneValue);

    /// <summary>Whether two values are the same, ignoring case.</summary>
    public static bool operator ==(SandboxSuspendMode left, SandboxSuspendMode right)
    {
        return left.Equals(right);
    }

    /// <summary>Whether two values differ, ignoring case.</summary>
    public static bool operator !=(SandboxSuspendMode left, SandboxSuspendMode right)
    {
        return !left.Equals(right);
    }

    /// <inheritdoc />
    public bool Equals(SandboxSuspendMode other)
    {
        return string.Equals(_value, other._value, StringComparison.OrdinalIgnoreCase);
    }

    /// <inheritdoc />
    public override bool Equals(object? obj)
    {
        return obj is SandboxSuspendMode other && Equals(other);
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
