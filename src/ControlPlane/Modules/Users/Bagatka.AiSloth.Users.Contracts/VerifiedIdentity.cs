namespace Bagatka.AiSloth.Users.Contracts;

/// <summary>
/// An identity a provider verified, as <see cref="IUsersApi.SignInAsync"/> takes it.
/// </summary>
/// <param name="Issuer">The provider's issuer, exactly as its tokens carry it.</param>
/// <param name="Subject">The provider's stable identifier for the person.</param>
public sealed record VerifiedIdentity(string Issuer, string Subject);
