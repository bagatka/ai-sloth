using System;

namespace Bagatka.Foundation;

/// <summary>
/// A strongly typed identifier that wraps a <see cref="Guid"/>. The interface exists so one
/// generic converter per technology (JSON, EF Core) works for every ID type, checked by the
/// compiler instead of discovered by reflection.
/// </summary>
/// <typeparam name="TSelf">The implementing ID type.</typeparam>
public interface ITypedId<TSelf>
    where TSelf : struct, ITypedId<TSelf>
{
    /// <summary>The raw value. Use it wherever a raw value is needed; never rely on <c>ToString()</c>.</summary>
    public Guid Value { get; }

    /// <summary>Rebuilds an ID from storage or a route value. New IDs come from the type's <c>New()</c>.</summary>
    public static abstract TSelf From(Guid value);
}
