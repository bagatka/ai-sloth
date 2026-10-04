using System;
using System.Globalization;

namespace Bagatka.AiSloth.Machines.Contracts;

/// <summary>
/// Machines run nooks as one sandbox provider; a sandbox's location is the ID of the machine it runs on.
/// </summary>
public static class MachineProvider
{
    /// <summary>The provider's name.</summary>
    public const string Name = "machine";

    /// <summary>The location of sandboxes on the machine.</summary>
    public static string LocationOf(MachineId machine)
    {
        return machine.Value.ToString("D", CultureInfo.InvariantCulture);
    }

    /// <summary>The machine a location names, or <see langword="null"/> when it names none.</summary>
    public static MachineId? ParseLocation(string? location)
    {
        bool parsed = Guid.TryParseExact(location, "D", out Guid id);
        return parsed ? MachineId.From(id) : null;
    }
}
