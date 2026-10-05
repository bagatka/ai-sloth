using System;
using Bagatka.Foundation;

namespace Bagatka.AiSloth.Users.Model;

// A code the host issued to sign someone in once, until it expires: the setup code or a link code
// (OneTimeCode: only its hash is kept).
internal sealed class IssuedCode
{
    private static readonly TimeSpan SetupLifetime = TimeSpan.FromDays(1);
    private static readonly TimeSpan LinkLifetime = TimeSpan.FromMinutes(10);

    // Used by the factories and by EF: parameter names match property names.
    private IssuedCode(Guid id, IssuedCodePurpose purpose, UserId? userId, byte[] codeHash, DateTimeOffset expiresAt)
    {
        Id = id;
        Purpose = purpose;
        UserId = userId;
        CodeHash = codeHash;
        ExpiresAt = expiresAt;
    }

    public Guid Id { get; private set; }

    public IssuedCodePurpose Purpose { get; private set; }

    // Whose device a link code signs in; none for the setup code.
    public UserId? UserId { get; private set; }

    public byte[] CodeHash { get; private set; }

    public DateTimeOffset ExpiresAt { get; private set; }

    public static (IssuedCode Code, string Text) Setup(TimeProvider time)
    {
        string text = OneTimeCode.Create();
        return (new IssuedCode(Guid.CreateVersion7(), IssuedCodePurpose.Setup, userId: null, OneTimeCode.Hash(text), time.GetUtcNow() + SetupLifetime), text);
    }

    public static (IssuedCode Code, string Text) Link(UserId userId, TimeProvider time)
    {
        string text = OneTimeCode.Create();
        return (new IssuedCode(Guid.CreateVersion7(), IssuedCodePurpose.Link, userId, OneTimeCode.Hash(text), time.GetUtcNow() + LinkLifetime), text);
    }

    public bool UsableAt(DateTimeOffset now)
    {
        return now < ExpiresAt;
    }
}
