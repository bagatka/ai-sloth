using System;
using Bagatka.AiSloth.AgentAccounts.Contracts;
using Bagatka.Foundation;
using Bagatka.Foundation.Modules;

namespace Bagatka.AiSloth.AgentAccounts.Model;

// A sign-in at a plan's vendor in progress, until it expires: the plan it will add, the code the person
// enters at the vendor if any, and the secret it continues with, such as a device code or a PKCE
// verifier, kept sealed. The vendor is asked no more often than it allows.
internal sealed class SignIn
{
    public const int MaxUserCodeLength = 64;

    // Used by Start and by EF: parameter names match property names.
    private SignIn(SignInId id, UserId userId, AgentAccountKind kind, AgentAccountName name, string? userCode, DateTimeOffset expiresAt, TimeSpan interval, DateTimeOffset nextPollAt)
    {
        Id = id;
        UserId = userId;
        Kind = kind;
        Name = name;
        UserCode = userCode;
        ExpiresAt = expiresAt;
        Interval = interval;
        NextPollAt = nextPollAt;
    }

    public SignInId Id { get; private set; }

    public UserId UserId { get; private set; }

    public AgentAccountKind Kind { get; private set; }

    public AgentAccountName Name { get; private set; }

    public string? UserCode { get; private set; }

    public DateTimeOffset ExpiresAt { get; private set; }

    public TimeSpan Interval { get; private set; }

    public DateTimeOffset NextPollAt { get; private set; }

    public byte[] SealedSecret { get; private set; } = [];

    public static SignIn Start(SignInId id, UserId userId, AgentAccountKind kind, AgentAccountName name, string? userCode, TimeSpan expiresIn, TimeSpan interval, string secret, SecretBox box, TimeProvider time)
    {
        DateTimeOffset now = time.GetUtcNow();
        SignIn signIn = new SignIn(id, userId, kind, name, userCode, now + expiresIn, interval, now + interval);
        signIn.SealedSecret = box.Seal(secret, id.Value);
        return signIn;
    }

    public string Secret(SecretBox box)
    {
        return box.Open(SealedSecret, Id.Value);
    }

    public bool ExpiredAt(DateTimeOffset now)
    {
        return now >= ExpiresAt;
    }

    // How long until the vendor may be asked again.
    public TimeSpan WaitAt(DateTimeOffset now)
    {
        TimeSpan wait = NextPollAt - now;
        return wait > TimeSpan.Zero ? wait : TimeSpan.Zero;
    }

    // The vendor was asked, and may have asked to be asked less often.
    public void Polled(TimeSpan? interval, DateTimeOffset now)
    {
        Interval = interval ?? Interval;
        NextPollAt = now + Interval;
    }
}
