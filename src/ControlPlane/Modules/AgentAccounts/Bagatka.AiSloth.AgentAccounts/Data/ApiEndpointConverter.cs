using System;
using Bagatka.AiSloth.AgentAccounts.Model;
using Bagatka.Foundation;
using Microsoft.EntityFrameworkCore.Storage.ValueConversion;

namespace Bagatka.AiSloth.AgentAccounts.Data;

// Stored endpoints go back through Parse, so invalid data fails loudly.
internal sealed class ApiEndpointConverter() : ValueConverter<ApiEndpoint, string>(
    endpoint => endpoint.Value.AbsoluteUri,
    value => FromStored(value))
{
    private static ApiEndpoint FromStored(string value)
    {
        Result<ApiEndpoint> parsed = ApiEndpoint.Parse(new Uri(value, UriKind.Absolute));
        if (parsed.Failed)
        {
            throw new InvalidOperationException("A stored API endpoint is invalid: " + parsed.Error.Message);
        }

        return parsed.Output;
    }
}
