using System;

namespace Bagatka.Azure.Sandboxes.Models;

/// <summary>
/// Where a sandbox is in its lifecycle. The service may add values; one this version doesn't know is kept as it came.
/// </summary>
public readonly struct SandboxState : IEquatable<SandboxState>
{
    private const string CreatingValue = "Creating";
    private const string RunningValue = "Running";
    private const string StoppingValue = "Stopping";
    private const string StoppedValue = "Stopped";
    private const string IdleValue = "Idle";
    private const string StopFailedValue = "StopFailed";

    private readonly string? _value;

    /// <summary>Creates the value from its name at the service, such as <c>Running</c>.</summary>
    /// <param name="value">The service's name for it.</param>
    public SandboxState(string value)
    {
        ArgumentNullException.ThrowIfNull(value);
        _value = value;
    }

    /// <summary>Being created.</summary>
    public static SandboxState Creating { get; } = new SandboxState(CreatingValue);

    /// <summary>Running.</summary>
    public static SandboxState Running { get; } = new SandboxState(RunningValue);

    /// <summary>Being stopped.</summary>
    public static SandboxState Stopping { get; } = new SandboxState(StoppingValue);

    /// <summary>Stopped, by a call or after it was idle, with what its suspend mode keeps.</summary>
    public static SandboxState Stopped { get; } = new SandboxState(StoppedValue);

    /// <summary>Running, but idle.</summary>
    public static SandboxState Idle { get; } = new SandboxState(IdleValue);

    /// <summary>Stopping it failed.</summary>
    public static SandboxState StopFailed { get; } = new SandboxState(StopFailedValue);

    /// <summary>Whether two values are the same, ignoring case.</summary>
    public static bool operator ==(SandboxState left, SandboxState right)
    {
        return left.Equals(right);
    }

    /// <summary>Whether two values differ, ignoring case.</summary>
    public static bool operator !=(SandboxState left, SandboxState right)
    {
        return !left.Equals(right);
    }

    /// <inheritdoc />
    public bool Equals(SandboxState other)
    {
        return string.Equals(_value, other._value, StringComparison.OrdinalIgnoreCase);
    }

    /// <inheritdoc />
    public override bool Equals(object? obj)
    {
        return obj is SandboxState other && Equals(other);
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
