namespace Bagatka.Harnesses;

/// <summary>
/// The agent couldn't load the earlier session (<see cref="Acp.LoadSession"/>), such as when its files
/// are gone; a new session can still start.
/// </summary>
/// <param name="Error">What the agent said, in words for people.</param>
public sealed record AcpLoadFailed(string Error);
