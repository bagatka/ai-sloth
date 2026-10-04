using Bagatka.Foundation;

namespace Bagatka.AiSloth.WebApi.Endpoints;

// The page the public API returns when a client doesn't say (PATTERNS.md, entry 18).
internal static class Paging
{
    public const int DefaultLimit = 50;

    public static PageRequest Request(string? cursor, int? limit)
    {
        return new PageRequest(cursor, limit ?? DefaultLimit);
    }
}
