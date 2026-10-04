using System;
using Microsoft.EntityFrameworkCore.Storage.ValueConversion;

namespace Bagatka.Foundation.Modules;

/// <summary>
/// Stores a typed ID as its GUID. Each module's DbContext registers it once per ID type
/// (PATTERNS.md, entry 8).
/// </summary>
/// <typeparam name="TId">The typed ID.</typeparam>
public sealed class TypedIdConverter<TId>() : ValueConverter<TId, Guid>(id => id.Value, value => FromStored(value))
    where TId : struct, ITypedId<TId>
{
    // Expression trees can't call static abstract members directly.
    private static TId FromStored(Guid value)
    {
        return TId.From(value);
    }
}
