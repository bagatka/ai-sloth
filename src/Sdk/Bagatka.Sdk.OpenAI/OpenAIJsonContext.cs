using System.Text.Json.Serialization;

namespace Bagatka.Sdk.OpenAI;

/// <summary>
/// Source-generated JSON for OpenAI's authorization server; the wire names are on the records.
/// </summary>
[JsonSerializable(typeof(OpenAIWire.ModelList))]
[JsonSerializable(typeof(OpenAIWire.TokenResponse))]
[JsonSerializable(typeof(OpenAIWire.ErrorResponse))]
[JsonSerializable(typeof(OpenAIWire.IdTokenClaims))]
[JsonSerializable(typeof(OpenAIWire.CodexUserCodeRequest))]
[JsonSerializable(typeof(OpenAIWire.CodexUserCode))]
[JsonSerializable(typeof(OpenAIWire.CodexPollRequest))]
[JsonSerializable(typeof(OpenAIWire.CodexApproval))]
[JsonSerializable(typeof(OpenAIWire.CodexRefreshRequest))]
[JsonSerializable(typeof(OpenAIWire.CodexTokens))]
internal sealed partial class OpenAIJsonContext : JsonSerializerContext;
