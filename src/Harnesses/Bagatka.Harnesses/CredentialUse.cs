namespace Bagatka.Harnesses;

/// <summary>
/// How a harness takes one kind of credential: in the environment variable <paramref name="Variable"/>.
/// With <paramref name="GatewayVariable"/> set, the secret never reaches the harness: it gets a model
/// gateway's URL in that variable and the gateway's token in <paramref name="Variable"/>, and the
/// gateway adds the secret to each model call.
/// </summary>
/// <param name="Kind">The kind of credential.</param>
/// <param name="Variable">The variable that carries the secret, or the gateway's token.</param>
/// <param name="GatewayVariable">The variable that carries the gateway's URL, or <see langword="null"/> when the secret goes to the harness.</param>
public sealed record CredentialUse(CredentialKind Kind, string Variable, string? GatewayVariable);
