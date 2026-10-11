using System;
using System.Buffers.Text;
using System.Security.Cryptography;
using System.Text;
using Bagatka.AiSloth.AgentAccounts.Contracts;
using Bagatka.Foundation;
using Bagatka.Foundation.Modules;

namespace Bagatka.AiSloth.AgentAccounts.Model;

// A sign-in at a vendor's page in progress, for ten minutes: the account it will add, and the state that
// ties the code the page shows the person to this attempt. The PKCE verifier is kept sealed, like a
// secret: with the code, it gets the tokens. A Claude plan signs in this way, as Claude Code does.
internal sealed class SignIn
{
    public const int MaxValueLength = 64;
    public const int MaxCodeLength = 2048;

    private static readonly TimeSpan Lifetime = TimeSpan.FromMinutes(10);

    // Used by Start and by EF: parameter names match property names.
    private SignIn(SignInId id, UserId userId, AgentAccountKind kind, AgentAccountName name, string state, DateTimeOffset expiresAt)
    {
        Id = id;
        UserId = userId;
        Kind = kind;
        Name = name;
        State = state;
        ExpiresAt = expiresAt;
    }

    public SignInId Id { get; private set; }

    public UserId UserId { get; private set; }

    public AgentAccountKind Kind { get; private set; }

    public AgentAccountName Name { get; private set; }

    public string State { get; private set; }

    public DateTimeOffset ExpiresAt { get; private set; }

    public byte[] SealedVerifier { get; private set; } = [];

    public static SignIn Start(UserId userId, AgentAccountKind kind, AgentAccountName name, SecretBox box, TimeProvider time)
    {
        SignIn signIn = new SignIn(SignInId.New(), userId, kind, name, RandomValue(), time.GetUtcNow() + Lifetime);
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

    // The code the vendor's page showed, if it answers this attempt: Claude's carries the attempt's state
    // after a '#', which comes first, so a code for anything else is never used.
    public Result<string> Read(string? shown)
    {
        string pasted = (shown ?? string.Empty).Trim();
        int separator = pasted.LastIndexOf('#', StringComparison.Ordinal);
        if (pasted.Length is 0 or > MaxCodeLength || separator <= 0)
        {
            return new Result<string>(Error.Validation("code", "Paste the whole code Claude shows, with the part after its #."));
        }

        bool sameAttempt = CryptographicOperations.FixedTimeEquals(Encoding.UTF8.GetBytes(pasted[(separator + 1)..]), Encoding.UTF8.GetBytes(State));
        return sameAttempt
            ? new Result<string>(pasted[..separator])
            : new Result<string>(Error.Validation("code", "This code belongs to another sign-in; start again."));
    }

    private static string RandomValue()
    {
        return Base64Url.EncodeToString(RandomNumberGenerator.GetBytes(32));
    }
}
