namespace Bagatka.AiSloth.Users.Contracts;

/// <summary>
/// How a person proves who they are when they sign in: with an identity the host's sign-in provider
/// verified, with a code, or as a newcomer system code admits, such as someone with an invite.
/// </summary>
public union SignInProof(VerifiedIdentity, SignInCode, Newcomer);
