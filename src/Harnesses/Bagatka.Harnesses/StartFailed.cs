namespace Bagatka.Harnesses;

/// <summary>The agent refused to start or to start a session, so it can't be used, such as for <c>Authentication required</c>.</summary>
/// <param name="Error">Why, in the agent's words.</param>
public sealed record StartFailed(string Error);
