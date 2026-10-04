using System;
using Bagatka.AiSloth.AgentAccounts.Model;
using Bagatka.Foundation;
using Microsoft.EntityFrameworkCore.Storage.ValueConversion;

namespace Bagatka.AiSloth.AgentAccounts.Data;

// Stored names go back through Parse, so invalid data fails loudly.
internal sealed class AgentAccountNameConverter() : ValueConverter<AgentAccountName, string>(
    name => name.Value,
    value => FromStored(value))
{
    private static AgentAccountName FromStored(string value)
    {
        Result<AgentAccountName> parsed = AgentAccountName.Parse(value);
        if (parsed.Failed)
        {
            throw new InvalidOperationException("A stored agent account name is invalid: " + parsed.Error.Message);
        }

        return parsed.Output;
    }
}
