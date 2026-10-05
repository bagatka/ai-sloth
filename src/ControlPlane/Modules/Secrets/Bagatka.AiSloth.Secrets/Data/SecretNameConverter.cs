using System;
using Bagatka.AiSloth.Secrets.Model;
using Bagatka.Foundation;
using Microsoft.EntityFrameworkCore.Storage.ValueConversion;

namespace Bagatka.AiSloth.Secrets.Data;

// Stored names go back through Parse, so invalid data fails loudly.
internal sealed class SecretNameConverter() : ValueConverter<SecretName, string>(
    name => name.Value,
    value => FromStored(value))
{
    private static SecretName FromStored(string value)
    {
        Result<SecretName> parsed = SecretName.Parse(value);
        if (parsed.Failed)
        {
            throw new InvalidOperationException("A stored secret name is invalid: " + parsed.Error.Message);
        }

        return parsed.Output;
    }
}
