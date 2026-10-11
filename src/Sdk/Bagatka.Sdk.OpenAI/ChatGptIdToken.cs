using System;
using System.Buffers.Text;
using System.Text.Json;

namespace Bagatka.Sdk.OpenAI;

/// <summary>
/// The claims of an ID token from OpenAI's token endpoint (<see cref="Read"/>), such as Codex's sign-in's.
/// </summary>
/// <param name="Subject">The person's stable identifier at OpenAI.</param>
/// <param name="Email">The person's email address, if shared.</param>
/// <param name="AccountId">The ChatGPT account, personal or a workspace's, whose plan the sign-in uses, when it says.</param>
public sealed record ChatGptIdToken(string Subject, string? Email, string? AccountId)
{
    /// <summary>
    /// The claims of an ID token from the token endpoint, or <see langword="null"/> when it isn't one.
    /// The signature isn't checked: the token came straight from the token endpoint over TLS, which
    /// OpenID Connect accepts in place of it. Callers check what they rely on.
    /// </summary>
    public static ChatGptIdToken? Read(string idToken)
    {
        ArgumentNullException.ThrowIfNull(idToken);
        string[] parts = idToken.Split('.');
        if (parts.Length != 3)
        {
            return null;
        }

        OpenAIWire.IdTokenClaims? claims;
        try
        {
            byte[] payload = Base64Url.DecodeFromChars(parts[1]);
            claims = JsonSerializer.Deserialize(payload, OpenAIJsonContext.Default.IdTokenClaims);
        }
        catch (FormatException)
        {
            return null;
        }
        catch (JsonException)
        {
            return null;
        }

        return claims?.Subject is null ? null : new ChatGptIdToken(claims.Subject, claims.Email, claims.Auth?.ChatGptAccountId);
    }
}
