namespace Bagatka.AiSloth.Users.Contracts;

/// <summary>
/// Input to <see cref="IUsersApi.SignInAsync"/>: an identity a provider vouched for.
/// </summary>
/// <param name="Issuer">The provider's issuer, exactly as its tokens carry it.</param>
/// <param name="Subject">The provider's stable identifier for the person.</param>
public sealed record SignIn(string Issuer, string Subject);
