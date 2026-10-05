namespace Bagatka.AiSloth.Users.Contracts;

/// <summary>Input to <see cref="IUsersApi.SignInAsync"/>.</summary>
/// <param name="Proof">Who the person is.</param>
/// <param name="Device">What the device is to them, such as <c>sloth on alex-laptop</c>: 1 to 100 characters.</param>
public sealed record SignIn(SignInProof Proof, string Device);
