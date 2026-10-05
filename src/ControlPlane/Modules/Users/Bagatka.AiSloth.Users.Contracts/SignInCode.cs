namespace Bagatka.AiSloth.Users.Contracts;

/// <summary>
/// A code the host issued: its setup code, which makes whoever uses it the host's first person while
/// nobody has signed up, or a link code, which signs its creator in on another device. Each works once.
/// </summary>
/// <param name="Code">The code, in any case and with stray spaces.</param>
/// <param name="Name">The first person's name, for the setup code: 1 to 100 characters. Ignored for a link code.</param>
public sealed record SignInCode(string Code, string? Name);
