using System;
using System.Globalization;
using Bagatka.AiSloth.Users.Contracts;
using Bagatka.Foundation;

namespace Bagatka.AiSloth.Users.Model;

// A person who signed in, known by the identity provider's issuer and subject.
internal sealed class User
{
    // OpenID Connect limits subjects to 255 ASCII characters; issuers are URLs.
    public const int MaxSubjectLength = 255;
    public const int MaxIssuerLength = 2048;

    // Used by Register and by EF: parameter names match property names.
    private User(UserId id, string issuer, string subject, DateTimeOffset createdAt)
    {
        Id = id;
        Issuer = issuer;
        Subject = subject;
        CreatedAt = createdAt;
    }

    public UserId Id { get; private set; }

    public string Issuer { get; private set; }

    public string Subject { get; private set; }

    public DateTimeOffset CreatedAt { get; private set; }

    public static Result<User> Register(SignIn identity, TimeProvider time)
    {
        if (identity.Issuer.Length is 0 or > MaxIssuerLength)
        {
            return new Result<User>(Error.Validation("issuer", string.Create(CultureInfo.InvariantCulture, $"Must be 1 to {MaxIssuerLength} characters.")));
        }

        if (identity.Subject.Length is 0 or > MaxSubjectLength)
        {
            return new Result<User>(Error.Validation("subject", string.Create(CultureInfo.InvariantCulture, $"Must be 1 to {MaxSubjectLength} characters.")));
        }

        return new Result<User>(new User(UserId.New(), identity.Issuer, identity.Subject, time.GetUtcNow()));
    }
}
