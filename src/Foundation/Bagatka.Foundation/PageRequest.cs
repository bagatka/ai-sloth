using System;

namespace Bagatka.Foundation;

/// <summary>
/// Asks for one page of a keyset-paginated list.
/// </summary>
public sealed record PageRequest
{
    /// <summary>The largest page any list returns.</summary>
    public const int MaxLimit = 200;

    /// <summary>Creates a page request. <paramref name="limit"/> is clamped to 1 through <see cref="MaxLimit"/>.</summary>
    /// <param name="cursor">The <see cref="Page{T}.NextCursor"/> of the previous page, or <see langword="null"/> for the first page.</param>
    /// <param name="limit">The number of items wanted.</param>
    public PageRequest(string? cursor, int limit)
    {
        Cursor = cursor;
        Limit = Math.Clamp(limit, 1, MaxLimit);
    }

    /// <summary>Where the page starts; opaque to callers.</summary>
    public string? Cursor { get; }

    /// <summary>The most items the page may hold, between 1 and <see cref="MaxLimit"/>.</summary>
    public int Limit { get; }
}
