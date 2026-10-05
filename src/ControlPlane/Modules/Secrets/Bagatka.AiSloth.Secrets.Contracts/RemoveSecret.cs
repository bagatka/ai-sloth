using Bagatka.AiSloth.Workspaces.Contracts;

namespace Bagatka.AiSloth.Secrets.Contracts;

/// <summary>Input to <see cref="ISecretsApi.RemoveAsync"/>.</summary>
/// <param name="WorkspaceId">The workspace.</param>
/// <param name="Name">The secret's name.</param>
public sealed record RemoveSecret(WorkspaceId WorkspaceId, string Name);
