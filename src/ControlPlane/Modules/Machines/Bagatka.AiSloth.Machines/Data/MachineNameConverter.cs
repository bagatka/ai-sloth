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
        Result<MachineName> parsed = MachineName.Parse(value);
        if (parsed.Failed)
        {
            throw new InvalidOperationException("A stored machine name is invalid: " + parsed.Error.Message);
        }

        return parsed.Output;
    }
}
