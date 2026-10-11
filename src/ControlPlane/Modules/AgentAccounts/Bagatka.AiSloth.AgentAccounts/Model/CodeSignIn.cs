using System;
using Bagatka.AiSloth.AgentAccounts.Contracts;
using Bagatka.Foundation;
using Bagatka.Foundation.Modules;

namespace Bagatka.AiSloth.AgentAccounts.Model;

// A sign-in with a code the person enters at the vendor (a device flow: GitHub's for Copilot, OpenAI's
// for Codex) waiting for them: the account it will add. The device code gets the token once they
// approve, so it is kept only sealed; the vendor is asked no more often than it allows.
internal sealed class CodeSignIn
{
    // Used by Start and by EF: parameter names match property names.
    private CodeSignIn(SignInId id, UserId userId, AgentAccountKind kind, AgentAccountName name, DateTimeOffset expiresAt, TimeSpan interval, DateTimeOffset nextPollAt)
    {
        Id = id;
        UserId = userId;
        Kind = kind;
        Name = name;
        ExpiresAt = expiresAt;
        Interval = interval;
        NextPollAt = nextPollAt;
    }

    public SignInId Id { get; private set; }

    public UserId UserId { get; private set; }

    public AgentAccountKind Kind { get; private set; }

    public AgentAccountName Name { get; private set; }

    public byte[] SealedDeviceCode { get; private set; } = [];

    // What the person enters, which OpenAI wants back when asked whether they approved.
    public string? UserCode { get; private set; }

    public DateTimeOffset ExpiresAt { get; private set; }

    public TimeSpan Interval { get; private set; }

    public DateTimeOffset NextPollAt { get; private set; }

    public static CodeSignIn Start(UserId userId, AgentAccountKind kind, AgentAccountName name, string deviceCode, string userCode, TimeSpan expiresIn, TimeSpan interval, SecretBox box, TimeProvider time)
    {
        DateTimeOffset now = time.GetUtcNow();
        CodeSignIn signIn = new CodeSignIn(SignInId.New(), userId, kind, name, now + expiresIn, interval, now + interval) { UserCode = userCode };
        signIn.SealedDeviceCode = box.Seal(deviceCode, signIn.Id.Value);
        return signIn;
    }

    public string DeviceCode(SecretBox box)
    {
        return box.Open(SealedDeviceCode, Id.Value);
    }

    public bool ExpiredAt(DateTimeOffset now)
    {
        return now >= ExpiresAt;
    }

    // How long until GitHub may be asked again.
    public TimeSpan WaitAt(DateTimeOffset now)
    {
        TimeSpan wait = NextPollAt - now;
        return wait > TimeSpan.Zero ? wait : TimeSpan.Zero;
    }

    // The vendor was asked; GitHub may have asked for a longer interval.
    public void Polled(TimeSpan? slowDown, DateTimeOffset now)
    {
        Interval = slowDown ?? Interval;
        NextPollAt = now + Interval;
    }

    public SignInStarted ToStarted(Uri verificationUrl)
    {
        return new SignInStarted(Id, verificationUrl, UserCode, ExpiresAt, Interval);
    }
}
