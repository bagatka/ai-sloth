using System.Text.Json.Serialization;

namespace Bagatka.Sdk.Anthropic;

// Source-generated JSON for Anthropic's answers, in its snake_case.
[JsonSourceGenerationOptions(PropertyNamingPolicy = JsonKnownNamingPolicy.SnakeCaseLower)]
[JsonSerializable(typeof(AnthropicWire.ModelList))]
[JsonSerializable(typeof(AnthropicWire.CodeExchange))]
[JsonSerializable(typeof(AnthropicWire.Refresh))]
[JsonSerializable(typeof(AnthropicWire.TokenAnswer))]
[JsonSerializable(typeof(AnthropicWire.ErrorAnswer))]
internal sealed partial class AnthropicJsonContext : JsonSerializerContext;
