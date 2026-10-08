using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using Bagatka.Foundation;
using Google.Protobuf.WellKnownTypes;
using Wire = Bagatka.Sandboxing.Remote.V1;

namespace Bagatka.Sandboxing.Remote;

// Conversions between the provider contract and its messages, the one place that knows both.
internal static class SandboxWire
{
    public static string Key(Guid value)
    {
        return value.ToString("D", CultureInfo.InvariantCulture);
    }

    public static Guid Key(string value)
    {
        return Guid.Parse(value, CultureInfo.InvariantCulture);
    }

    public static Wire.SandboxSpec ToWire(SandboxSpec spec)
    {
        Wire.SandboxSpec wire = new Wire.SandboxSpec
        {
            Key = Key(spec.Key.Value),
            CpuMillicores = spec.Resources.CpuMillicores,
            MemoryMebibytes = spec.Resources.MemoryMebibytes,
        };
        foreach (KeyValuePair<string, string> variable in spec.Environment)
        {
            wire.Environment.Add(variable.Key, variable.Value);
        }

        switch (spec.Source.Value)
        {
            case SandboxImage image:
                wire.Image = image.Reference;
                break;
            case SnapshotKey snapshot:
                wire.Snapshot = Key(snapshot.Value);
                break;
            default:
                throw new InvalidOperationException("The sandbox source is default.");
        }

        if (spec.Location is not null)
        {
            wire.Location = spec.Location;
        }

        return wire;
    }

    public static SandboxSpec FromWire(Wire.SandboxSpec wire)
    {
        SandboxSource source = wire.SourceCase switch
        {
            Wire.SandboxSpec.SourceOneofCase.Image => new SandboxSource(new SandboxImage(wire.Image)),
            Wire.SandboxSpec.SourceOneofCase.Snapshot => new SandboxSource(SnapshotKey.From(Key(wire.Snapshot))),
            Wire.SandboxSpec.SourceOneofCase.None => throw new InvalidOperationException("The sandbox spec has no source."),
        };
        return new SandboxSpec(
            SandboxKey.From(Key(wire.Key)),
            source,
            new SandboxResources(wire.CpuMillicores, wire.MemoryMebibytes),
            new Dictionary<string, string>(wire.Environment, StringComparer.Ordinal),
            wire.HasLocation ? wire.Location : null);
    }

    public static Wire.Sandbox ToWire(SandboxObservation sandbox)
    {
        Wire.Sandbox wire = new Wire.Sandbox
        {
            Key = Key(sandbox.Key.Value),
            State = sandbox.State switch
            {
                SandboxState.Starting => Wire.SandboxState.Starting,
                SandboxState.Running => Wire.SandboxState.Running,
                SandboxState.Suspending => Wire.SandboxState.Suspending,
                SandboxState.Paused => Wire.SandboxState.Paused,
                SandboxState.Stopped => Wire.SandboxState.Stopped,
                SandboxState.Failed => Wire.SandboxState.Failed,
                SandboxState.Deleting => Wire.SandboxState.Deleting,
            },
            CreatedAt = Timestamp.FromDateTimeOffset(sandbox.CreatedAt),
        };
        if (sandbox.Reason is not null)
        {
            wire.Reason = sandbox.Reason;
        }

        return wire;
    }

    public static SandboxObservation FromWire(Wire.Sandbox wire)
    {
        SandboxState state = wire.State switch
        {
            Wire.SandboxState.Starting => SandboxState.Starting,
            Wire.SandboxState.Running => SandboxState.Running,
            Wire.SandboxState.Suspending => SandboxState.Suspending,
            Wire.SandboxState.Paused => SandboxState.Paused,
            Wire.SandboxState.Stopped => SandboxState.Stopped,
            Wire.SandboxState.Failed => SandboxState.Failed,
            Wire.SandboxState.Deleting => SandboxState.Deleting,
            Wire.SandboxState.Unspecified => throw new InvalidOperationException("The remote provider sent a sandbox without a state."),
        };
        return new SandboxObservation(SandboxKey.From(Key(wire.Key)), state, wire.CreatedAt.ToDateTimeOffset(), wire.HasReason ? wire.Reason : null);
    }

    public static Wire.Snapshot ToWire(SnapshotObservation snapshot)
    {
        return new Wire.Snapshot
        {
            Key = Key(snapshot.Key.Value),
            Source = Key(snapshot.Source.Value),
            CreatedAt = Timestamp.FromDateTimeOffset(snapshot.CreatedAt),
        };
    }

    public static SnapshotObservation FromWire(Wire.Snapshot wire)
    {
        return new SnapshotObservation(SnapshotKey.From(Key(wire.Key)), SandboxKey.From(Key(wire.Source)), wire.CreatedAt.ToDateTimeOffset());
    }

    public static Wire.Error ToWire(Error error)
    {
        return new Wire.Error
        {
            Kind = error.Kind switch
            {
                ErrorKind.Validation => Wire.ErrorKind.Validation,
                ErrorKind.Unauthorized => Wire.ErrorKind.Unauthorized,
                ErrorKind.Forbidden => Wire.ErrorKind.Forbidden,
                ErrorKind.NotFound => Wire.ErrorKind.NotFound,
                ErrorKind.Conflict => Wire.ErrorKind.Conflict,
            },
            Code = error.Code,
            Message = error.Message,
            Fields = { error.Fields.Select(field => new Wire.FieldError { Field = field.Field, Message = field.Message }) },
        };
    }

    // Not handled: a validation error with several fields keeps its first; providers reject one
    // field at a time. Keeping all would need an Error factory that takes several fields.
    public static Error FromWire(Wire.Error wire)
    {
        return wire.Kind switch
        {
            Wire.ErrorKind.Validation => Error.Validation(wire.Fields.FirstOrDefault()?.Field ?? "spec", wire.Fields.FirstOrDefault()?.Message ?? wire.Message),
            Wire.ErrorKind.Unauthorized => Error.Unauthorized,
            Wire.ErrorKind.Forbidden => Error.Forbidden,
            Wire.ErrorKind.NotFound => Error.NotFound(wire.Code, wire.Message),
            Wire.ErrorKind.Conflict => Error.Conflict(wire.Code, wire.Message),
            Wire.ErrorKind.Unspecified => throw new InvalidOperationException("The remote provider sent an error without a kind."),
        };
    }
}
