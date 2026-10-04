using System;
using Bagatka.AiSloth.Workspaces.Model;
using Bagatka.Foundation;
using Microsoft.EntityFrameworkCore.Storage.ValueConversion;

namespace Bagatka.AiSloth.Workspaces.Data;

// Stored names go back through Parse, so invalid data fails loudly.
internal sealed class WorkspaceNameConverter() : ValueConverter<WorkspaceName, string>(
    name => name.Value,
    value => FromStored(value))
{
    private static WorkspaceName FromStored(string value)
    {
        Result<WorkspaceName> parsed = WorkspaceName.Parse(value);
        if (parsed.Failed)
        {
            throw new InvalidOperationException("A stored workspace name is invalid: " + parsed.Error.Message);
        }

        return parsed.Output;
    }
}
