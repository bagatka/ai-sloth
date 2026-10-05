namespace Bagatka.AiSloth.Sources.Contracts;

/// <summary>Who a commit names, as git writes it: <c>Name &lt;email&gt;</c>.</summary>
/// <param name="Name">The name: 1 to 100 characters, without <c>&lt;</c>, <c>&gt;</c>, or line breaks.</param>
/// <param name="Email">The email address, such as GitHub's no-reply address for the account.</param>
public sealed record GitIdentity(string Name, string Email);
