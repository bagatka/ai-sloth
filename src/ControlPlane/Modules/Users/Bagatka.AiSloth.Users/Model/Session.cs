using System;
using System.Buffers.Text;
using System.Security.Cryptography;
using System.Text;
using Bagatka.AiSloth.Users.Contracts;
using Bagatka.Foundation;

namespace Bagatka.AiSloth.Users.Model;

// A signed-in device. Only the token's hash is kept. It lasts until it is ended, or until it goes
// unused for 90 days; its last use is recorded to the day, so most calls write nothing.
internal sealed class Session
{
    private static readonly TimeSpan IdleLimit = TimeSpan.FromDays(90);
    private static readonly TimeSpan UseResolution = TimeSpan.FromDays(1);

    // Used by Start and by EF: parameter names match property names.
    private Session(SessionId id, UserId userId, BoundedName device, byte[] tokenHash, DateTimeOffset startedAt)
    {
        Id = id;
        UserId = userId;
        Device = device;
        TokenHash = tokenHash;
        StartedAt = startedAt;
        LastUsedAt = startedAt;
    }

    public SessionId Id { get; private set; }

    public UserId UserId { get; private set; }

    public BoundedName Device { get; private set; }

    public byte[] TokenHash { get; private set; }

    public DateTimeOffset StartedAt { get; private set; }

    public DateTimeOffset LastUsedAt { get; private set; }

    // The token is shown once, to the device, and prefixed so secret scanners can find a leaked one.
    public static (Session Session, string Token) Start(UserId userId, BoundedName device, TimeProvider time)
    {
        string token = "aisloth_" + Base64Url.EncodeToString(RandomNumberGenerator.GetBytes(32));
        return (new Session(SessionId.New(), userId, device, HashToken(token), time.GetUtcNow()), token);
    }

    public static byte[] HashToken(string token)
    {
        return SHA256.HashData(Encoding.UTF8.GetBytes(token));
    }

    public bool ExpiredAt(DateTimeOffset now)
    {
        return now - LastUsedAt > IdleLimit;
    }

    // Records a use; returns whether that changed anything to save.
    public bool Use(DateTimeOffset now)
    {
        if (now - LastUsedAt < UseResolution)
        {
            return false;
        }

        LastUsedAt = now;
        return true;
    }

    public SessionSummary ToSummary()
    {
        return new SessionSummary(Id, Device.Value, StartedAt, LastUsedAt);
    }
}
