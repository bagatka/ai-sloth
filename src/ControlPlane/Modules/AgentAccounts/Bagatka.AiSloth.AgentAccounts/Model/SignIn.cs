using System;
using System.Buffers.Text;
using System.Collections.Generic;
using System.Diagnostics.CodeAnalysis;
using System.Linq;
using System.Security.Cryptography;
using System.Text;
using Bagatka.AiSloth.AgentAccounts.Contracts;
using Bagatka.Foundation;
using Bagatka.Sdk.OpenAI;

namespace Bagatka.AiSloth.AgentAccounts.Model;

// A sign-in at a vendor in progress, for ten minutes: the account it will add, where the person's
// browser returns, and the values that tie the answer to this attempt. The PKCE verifier is kept
// sealed, like a secret: with the code, it gets the tokens.
internal sealed class SignIn
{
    public const int MaxCallbackLength = 2048;
    public const int MaxValueLength = 64;

    private static readonly TimeSpan Lifetime = TimeSpan.FromMinutes(10);

    // Used by Start and by EF: parameter names match property names.
    private SignIn(SignInId id, UserId userId, AgentAccountKind kind, AgentAccountName name, Uri callback, string state, string nonce, DateTimeOffset expiresAt)
    {
        Id = id;
        UserId = userId;
        Kind = kind;
        Name = name;
        Callback = callback;
        State = state;
        Nonce = nonce;
        ExpiresAt = expiresAt;
    }

    public SignInId Id { get; private set; }

    public UserId UserId { get; private set; }

    public AgentAccountKind Kind { get; private set; }

    public AgentAccountName Name { get; private set; }

    public Uri Callback { get; private set; }

    public string State { get; private set; }

    public string Nonce { get; private set; }

    public DateTimeOffset ExpiresAt { get; private set; }

    public byte[] SealedVerifier { get; private set; } = [];

    public static SignIn Start(UserId userId, AgentAccountKind kind, AgentAccountName name, Uri callback, SecretBox box, TimeProvider time)
    {
        SignIn signIn = new SignIn(SignInId.New(), userId, kind, name, callback, RandomValue(), RandomValue(), time.GetUtcNow() + Lifetime);
        signIn.SealedVerifier = box.Seal(RandomValue(), signIn.Id.Value);
        return signIn;
    }

    public bool ExpiredAt(DateTimeOffset now)
    {
        return now >= ExpiresAt;
    }

    public string OpenVerifier(SecretBox box)
    {
        return box.Open(SealedVerifier, Id.Value);
    }

    public ChatGptAuthorization Authorization(string appName, string hostId, SecretBox box)
    {
        string challenge = ChatGptSignInClient.CodeChallenge(OpenVerifier(box));
        return new ChatGptAuthorization(ChatGptSignInClient.RegistrationClientId, appName, hostId, Callback, State, Nonce, challenge);
    }

    // The vendor's answer, if it answers this attempt at this callback: its state comes first, so an
    // answer to anything else is never read further.
    public Result<SignInAnswer> Read(Uri returnedTo)
    {
        bool sameCallback = returnedTo.IsAbsoluteUri
            && string.Equals(returnedTo.GetLeftPart(UriPartial.Path), Callback.GetLeftPart(UriPartial.Path), StringComparison.Ordinal);
        if (!sameCallback)
        {
            return Refused("This isn't the address the sign-in returns to.");
        }

        Dictionary<string, string> query = Query(returnedTo);
        string state = query.GetValueOrDefault("state") ?? string.Empty;
        bool sameAttempt = CryptographicOperations.FixedTimeEquals(Encoding.UTF8.GetBytes(state), Encoding.UTF8.GetBytes(State));
        if (!sameAttempt)
        {
            return Refused("This answer belongs to another sign-in.");
        }

        string? error = query.GetValueOrDefault("error");
        if (error is not null)
        {
            bool declined = string.Equals(error, "access_denied", StringComparison.Ordinal);
            return Refused(declined ? "The sign-in was declined." : "The vendor ended the sign-in: " + error + ".");
        }

        string? code = query.GetValueOrDefault("code");
        string? clientId = query.GetValueOrDefault("client_id");
        if (code is null || clientId is null)
        {
            return Refused("The vendor's answer lacks a code or a registered client; start again.");
        }

        return new Result<SignInAnswer>(new SignInAnswer(code, clientId));
    }

    // The ID token must be the one this attempt asked for, issued to the registered client and current.
    public bool IssuedFor([NotNullWhen(true)] ChatGptIdToken? identity, string clientId, Uri issuer, DateTimeOffset now)
    {
        return identity is not null
            && string.Equals(identity.Issuer, issuer.AbsoluteUri.TrimEnd('/'), StringComparison.Ordinal)
            && string.Equals(identity.Nonce, Nonce, StringComparison.Ordinal)
            && identity.Audiences.Contains(clientId, StringComparer.Ordinal)
            && identity.ExpiresAt > now;
    }

    private static Result<SignInAnswer> Refused(string message)
    {
        return new Result<SignInAnswer>(Error.Validation("returnedTo", message));
    }

    private static string RandomValue()
    {
        return Base64Url.EncodeToString(RandomNumberGenerator.GetBytes(32));
    }

    // The query's parameters; a repeated one keeps its first value.
    private static Dictionary<string, string> Query(Uri uri)
    {
        Dictionary<string, string> parameters = new Dictionary<string, string>(StringComparer.Ordinal);
        foreach (string pair in uri.Query.TrimStart('?').Split('&', StringSplitOptions.RemoveEmptyEntries))
        {
            string[] parts = pair.Split('=', 2);
            string key = Uri.UnescapeDataString(parts[0].Replace('+', ' '));
            string value = parts.Length == 2 ? Uri.UnescapeDataString(parts[1].Replace('+', ' ')) : string.Empty;
            parameters.TryAdd(key, value);
        }

        return parameters;
    }
}
