using System;
using System.Collections.Generic;

namespace Bagatka.Sdk.OpenAI;

/// <summary>
/// The claims of an ID token from OpenAI's token endpoint (<see cref="ChatGptSignInClient.ReadIdToken"/>).
/// </summary>
/// <param name="Issuer">Who issued it: <c>https://auth.openai.com</c>.</param>
/// <param name="Subject">The person's stable identifier at OpenAI.</param>
/// <param name="Audiences">Whom it was issued to: the client ID.</param>
/// <param name="ExpiresAt">When it expires.</param>
/// <param name="Nonce">The nonce of the authorization it answers.</param>
/// <param name="Email">The person's email address, if shared.</param>
public sealed record ChatGptIdToken(
    string Issuer,
    string Subject,
    IReadOnlyList<string> Audiences,
    DateTimeOffset ExpiresAt,
    string? Nonce,
    string? Email);
