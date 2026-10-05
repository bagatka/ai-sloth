using System;
using System.Globalization;
using Bagatka.AiSloth.Users.Contracts;
using Bagatka.Foundation;

namespace Bagatka.AiSloth.Users.Model;

// A person who uses the host: known by an identity provider's issuer and subject, or, without a
// provider, by the codes and sessions that carry them.
internal sealed class User
{
    // OpenID Connect limits subjects to 255 ASCII characters; issuers are URLs.
    public const int MaxSubjectLength = 255;
    public const int MaxIssuerLength = 2048;

    // Used by the factories and by EF: parameter names match property names.
    private User(UserId id, BoundedName name, string? issuer, string? subject, DateTimeOffset createdAt)
    {
        Id = id;
        Name = name;
        Issuer = issuer;
        Subject = subject;
        CreatedAt = createdAt;
    }

    public UserId Id { get; private set; }

    public BoundedName Name { get; private set; }

    // Both set, or neither for a person without a provider's identity.
    public string? Issuer { get; private set; }

    public string? Subject { get; private set; }

    public DateTimeOffset CreatedAt { get; private set; }

    // Not handled: renaming. A provider that gives no usable name or email leaves "New person" until
    // renaming is built.
    public static Result<User> FromProvider(VerifiedIdentity identity, TimeProvider time)
    {
        if (identity.Issuer.Length is 0 or > MaxIssuerLength)
        {
            return new Result<User>(Error.Validation("issuer", string.Create(CultureInfo.InvariantCulture, $"Must be 1 to {MaxIssuerLength} characters.")));
        }

        if (identity.Subject.Length is 0 or > MaxSubjectLength)
        {
            return new Result<User>(Error.Validation("subject", string.Create(CultureInfo.InvariantCulture, $"Must be 1 to {MaxSubjectLength} characters.")));
        }

        Result<BoundedName> given = BoundedName.Parse(identity.Name, "name");
        BoundedName name = given.Failed ? BoundedName.NewPerson : given.Output;
        return new Result<User>(new User(UserId.New(), name, identity.Issuer, identity.Subject, time.GetUtcNow()));
    }

    // A person known by name only, such as the host's first person or a newcomer with an invite.
    public static User Named(BoundedName name, TimeProvider time)
    {
        return new User(UserId.New(), name, issuer: null, subject: null, time.GetUtcNow());
    }

    public UserSummary ToSummary()
    {
        return new UserSummary(Id, Name.Value);
    }
}
