using System;

namespace Bagatka.AiSloth.Nooks.Model;

// Where a nook runs, as callers name it: a provider, then the place within it for a provider with
// several, such as `docker` or `machine:<machine ID>`.
internal readonly record struct ProviderId(string Name, string? Location)
{
    public static ProviderId Parse(string id)
    {
        int colon = id.IndexOf(':', StringComparison.Ordinal);
        return colon < 0 ? new ProviderId(id, null) : new ProviderId(id[..colon], id[(colon + 1)..]);
    }

    public override string ToString()
    {
        return Location is null ? Name : Name + ":" + Location;
    }
}
