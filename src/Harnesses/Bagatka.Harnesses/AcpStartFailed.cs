namespace Bagatka.Harnesses;

/// <summary>The agent refused <see cref="Acp.Initialize"/> or <see cref="Acp.NewSession"/>, so it can't be used.</summary>
/// <param name="Error">What went wrong, in words for people, such as <c>Authentication required</c>.</param>
public sealed record AcpStartFailed(string Error);
