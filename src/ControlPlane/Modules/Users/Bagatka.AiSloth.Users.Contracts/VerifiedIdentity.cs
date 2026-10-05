namespace Bagatka.AiSloth.Users.Contracts;

/// <summary>
/// An identity the host's sign-in provider verified. Only system code that validated the provider's
/// token may present it.
/// </summary>
/// <param name="Issuer">The provider's issuer, exactly as its tokens carry it.</param>
/// <param name="Subject">The provider's stable identifier for the person.</param>
/// <param name="Name">The person's name as the provider gave it, or their email address; recorded on first sign-in.</param>
public sealed record VerifiedIdentity(string Issuer, string Subject, string? Name);
