using Bagatka.AiSloth.Workspaces.Contracts;

namespace Bagatka.AiSloth.Sources.Contracts;

/// <summary>Input to <see cref="ISourcesApi.AddRepositoryAsync"/>.</summary>
/// <param name="WorkspaceId">The workspace whose nooks will use it.</param>
/// <param name="FullName">Its name on GitHub, such as <c>acme/api</c>, in any case.</param>
public sealed record AddRepository(WorkspaceId WorkspaceId, string FullName);
