using System.Text.Json.Serialization;

namespace Bagatka.Sdk.OpenAI;

/// <summary>
/// Source-generated JSON for OpenAI's authorization server; the wire names are on the records.
/// </summary>
[JsonSerializable(typeof(OpenAIWire.TokenResponse))]
[JsonSerializable(typeof(OpenAIWire.ErrorResponse))]
[JsonSerializable(typeof(OpenAIWire.IdTokenClaims))]
internal sealed partial class OpenAIJsonContext : JsonSerializerContext;
