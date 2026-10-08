using Bagatka.AiSloth.Workspaces.Contracts;

namespace Bagatka.AiSloth.Chats.Contracts;

/// <summary>Input to <see cref="IChatsApi.ForgetHarnessStateAsync"/>.</summary>
/// <param name="WorkspaceId">The workspace.</param>
/// <param name="Harness">The harness, such as <c>claude-code</c>.</param>
/// <param name="Shared">Whether to forget the workspace's state, rather than the actor's own.</param>
public sealed record ForgetHarnessState(WorkspaceId WorkspaceId, string Harness, bool Shared);
