using System;
using System.Collections.Generic;

namespace Bagatka.ModelAccess.Kinds;

internal static class ModelEndpoints
{
    // An endpoint whose calls carry one header that pays for them.
    public static ModelEndpoint Through(Uri url, string header, string value)
    {
        return new ModelEndpoint(url, new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase) { [header] = value });
    }
}
