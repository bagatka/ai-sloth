using System;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace Bagatka.Foundation;

/// <summary>
/// Serializes a typed ID as its GUID string. Register it on each ID type with
/// <c>[JsonConverter(typeof(TypedIdJsonConverter&lt;TId&gt;))]</c>.
/// </summary>
/// <typeparam name="TId">The typed ID.</typeparam>
public sealed class TypedIdJsonConverter<TId> : JsonConverter<TId>
    where TId : struct, ITypedId<TId>
{
    /// <inheritdoc />
    public override TId Read(ref Utf8JsonReader reader, Type typeToConvert, JsonSerializerOptions options)
    {
        return TId.From(reader.GetGuid());
    }

    /// <inheritdoc />
    public override void Write(Utf8JsonWriter writer, TId value, JsonSerializerOptions options)
    {
        ArgumentNullException.ThrowIfNull(writer);
        writer.WriteStringValue(value.Value);
    }
}
