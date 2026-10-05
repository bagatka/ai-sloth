using System;
using Bagatka.AiSloth.Users.Model;
using Bagatka.Foundation;
using Microsoft.EntityFrameworkCore.Storage.ValueConversion;

namespace Bagatka.AiSloth.Users.Data;

// Stored names go back through Parse, so invalid data fails loudly.
internal sealed class BoundedNameConverter() : ValueConverter<BoundedName, string>(
    name => name.Value,
    value => FromStored(value))
{
    private static BoundedName FromStored(string value)
    {
        Result<BoundedName> parsed = BoundedName.Parse(value, "name");
        if (parsed.Failed)
        {
            throw new InvalidOperationException("A stored name is invalid: " + parsed.Error.Message);
        }

        return parsed.Output;
    }
}
