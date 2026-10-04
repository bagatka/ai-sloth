using System.Collections.Generic;

namespace Bagatka.Foundation;

/// <summary>
/// One page of a keyset-paginated list.
/// </summary>
/// <typeparam name="T">The item type.</typeparam>
/// <param name="Items">The items, in the list's stable order.</param>
/// <param name="NextCursor">Pass it in the next <see cref="PageRequest"/>; <see langword="null"/> on the last page.</param>
public sealed record Page<T>(IReadOnlyList<T> Items, string? NextCursor);
