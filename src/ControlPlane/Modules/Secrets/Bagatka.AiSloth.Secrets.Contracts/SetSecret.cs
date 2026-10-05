using Bagatka.AiSloth.Workspaces.Contracts;

namespace Bagatka.AiSloth.Secrets.Contracts;

/// <summary>Input to <see cref="ISecretsApi.SetAsync"/>.</summary>
/// <param name="WorkspaceId">The workspace whose nooks get it.</param>
/// <param name="Name">The environment variable's name, such as <c>GH_TOKEN</c>: letters, digits, and underscores, not starting with a digit, at most 128 characters.</param>
/// <param name="Value">Its value: 1 to 16,384 characters, without NUL.</param>
public sealed record SetSecret(WorkspaceId WorkspaceId, string Name, string Value);
