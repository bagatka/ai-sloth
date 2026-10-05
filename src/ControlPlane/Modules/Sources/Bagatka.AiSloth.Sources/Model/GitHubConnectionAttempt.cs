using System;
using Bagatka.AiSloth.Sources.Contracts;
using Bagatka.Foundation;
using Bagatka.Foundation.Modules;
using Bagatka.Sdk.GitHub;

namespace Bagatka.AiSloth.Sources.Model;

// A device flow sign-in at GitHub waiting for the person. The device code gets the tokens once they
// approve, so it is kept only sealed; GitHub is asked no more often than it allows.
internal sealed class GitHubConnectionAttempt
{
    // Used by Start and by EF: parameter names match property names.
    private GitHubConnectionAttempt(GitHubConnectionAttemptId id, UserId userId, DateTimeOffset expiresAt, TimeSpan interval, DateTimeOffset nextPollAt)
    {
        Id = id;
        UserId = userId;
        ExpiresAt = expiresAt;
        Interval = interval;
        NextPollAt = nextPollAt;
    }

    public GitHubConnectionAttemptId Id { get; private set; }

    public UserId UserId { get; private set; }

    public byte[] SealedDeviceCode { get; private set; } = [];

    public DateTimeOffset ExpiresAt { get; private set; }

    public TimeSpan Interval { get; private set; }

    public DateTimeOffset NextPollAt { get; private set; }

    public static GitHubConnectionAttempt Start(UserId userId, GitHubDeviceCode code, SecretBox box, TimeProvider time)
    {
        DateTimeOffset now = time.GetUtcNow();
        GitHubConnectionAttempt attempt = new GitHubConnectionAttempt(GitHubConnectionAttemptId.New(), userId, now + code.ExpiresIn, code.Interval, now + code.Interval);
        attempt.SealedDeviceCode = box.Seal(code.DeviceCode, attempt.Id.Value);
        return attempt;
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

    // GitHub was asked; it may have asked for a longer interval.
    public void Polled(TimeSpan? slowDown, DateTimeOffset now)
    {
        Interval = slowDown ?? Interval;
        NextPollAt = now + Interval;
    }

    public GitHubConnectionStarted ToStarted(GitHubDeviceCode code)
    {
        return new GitHubConnectionStarted(Id, code.UserCode, code.VerificationUri, ExpiresAt, Interval);
    }
}
