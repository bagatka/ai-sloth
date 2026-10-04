using System;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace Bagatka.Foundation;

/// <summary>
/// The one JSON configuration every host and client uses (PATTERNS.md, entry 17): camelCase names,
/// enums as their names, and strict input that rejects what a careless client would get wrong.
/// </summary>
public static class FoundationJson
{
    /// <summary>The options for code that serializes on its own, such as API clients.</summary>
    public static JsonSerializerOptions Options { get; } = CreateOptions();

    /// <summary>Applies the configuration to options a framework owns, such as ASP.NET Core's.</summary>
    public static void Configure(JsonSerializerOptions options)
    {
        ArgumentNullException.ThrowIfNull(options);
        options.PropertyNamingPolicy = JsonNamingPolicy.CamelCase;
        options.PropertyNameCaseInsensitive = true;
        options.Converters.Add(new JsonStringEnumConverter());

        // Numbers are JSON numbers, never strings.
        options.NumberHandling = JsonNumberHandling.Strict;

        // A missing constructor argument or a null for a non-nullable one is a client error, not a default.
        options.RespectRequiredConstructorParameters = true;
        options.RespectNullableAnnotations = true;
    }

    private static JsonSerializerOptions CreateOptions()
    {
        JsonSerializerOptions options = new JsonSerializerOptions(JsonSerializerDefaults.Web);
        Configure(options);
        options.MakeReadOnly(populateMissingResolver: true);
        return options;
    }
}
