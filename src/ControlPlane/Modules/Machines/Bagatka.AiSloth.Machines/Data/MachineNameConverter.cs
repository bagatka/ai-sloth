using System;
using Bagatka.AiSloth.Machines.Model;
using Bagatka.Foundation;
using Microsoft.EntityFrameworkCore.Storage.ValueConversion;

namespace Bagatka.AiSloth.Machines.Data;

// Stored names go back through Parse, so invalid data fails loudly.
internal sealed class MachineNameConverter() : ValueConverter<MachineName, string>(
    name => name.Value,
    value => FromStored(value))
{
    private static MachineName FromStored(string value)
    {
        return MachineName.Parse(value).TryGetValue(out MachineName? name, out Error? invalid)
            ? name
            : throw new InvalidOperationException("A stored machine name is invalid: " + invalid.Message);
    }
}
