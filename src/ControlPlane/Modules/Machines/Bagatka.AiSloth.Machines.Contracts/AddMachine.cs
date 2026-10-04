using Bagatka.AiSloth.Workspaces.Contracts;

namespace Bagatka.AiSloth.Machines.Contracts;

/// <summary>
/// Input to <see cref="IMachinesApi.AddAsync"/>.
/// </summary>
/// <param name="WorkspaceId">The workspace whose nooks the machine will run.</param>
/// <param name="Name">The display name, as the owner typed it; the module validates it.</param>
public sealed record AddMachine(WorkspaceId WorkspaceId, string Name);
