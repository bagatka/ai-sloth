namespace Bagatka.Foundation.Modules;

/// <summary>
/// The order of a keyset-paginated list. Typed IDs are UUIDv7, so ID order is creation order.
/// </summary>
public enum KeysetOrder
{
    /// <summary>Ascending IDs.</summary>
    OldestFirst = 1,

    /// <summary>Descending IDs.</summary>
    NewestFirst = 2,
}
