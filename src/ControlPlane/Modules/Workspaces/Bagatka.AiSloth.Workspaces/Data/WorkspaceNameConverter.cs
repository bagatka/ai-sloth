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
        return WorkspaceName.Parse(value).TryGetValue(out WorkspaceName? name, out Error? invalid)
            ? name
            : throw new InvalidOperationException("A stored workspace name is invalid: " + invalid.Message);
    }
}
