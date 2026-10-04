namespace Bagatka.AiSloth.Nooks.Contracts;

/// <summary>
/// A provider a workspace's nooks can run on: one the deployment runs for every workspace, such as a
/// Docker Engine or a cloud, or one of the workspace's own machines.
/// </summary>
/// <param name="Id">What <see cref="CreateNook.Provider"/> takes; callers don't parse it.</param>
/// <param name="Name">Its display name, such as <c>docker</c> or a machine's name.</param>
/// <param name="Available">Whether it can create nooks right now; a machine that is offline can't.</param>
public sealed record ProviderSummary(string Id, string Name, bool Available);
