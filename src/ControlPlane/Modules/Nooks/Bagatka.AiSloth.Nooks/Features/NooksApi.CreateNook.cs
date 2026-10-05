using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Bagatka.AiSloth.Machines.Contracts;
using Bagatka.AiSloth.Nooks.Contracts;
using Bagatka.AiSloth.Nooks.Model;
using Bagatka.AiSloth.Sources.Contracts;
using Bagatka.AiSloth.Workspaces.Contracts;
using Bagatka.Foundation;
using Bagatka.Foundation.Modules;

namespace Bagatka.AiSloth.Nooks;

internal sealed partial class NooksApi
{
    private const int MaxRepositories = 20;

    public async Task<Result<NookSummary>> CreateAsync(Actor actor, CreateNook command, CancellationToken ct)
    {
        AccessLevel? access = await workspaces.GetAccessAsync(actor, Resource.Workspace(command.WorkspaceId), ct);
        if (access is null)
        {
            return new Result<NookSummary>(WorkspacesErrors.NotFound);
        }

        if (access < AccessLevel.Write)
        {
            return new Result<NookSummary>(Error.Forbidden);
        }

        ProviderId? provider = await FindProviderAsync(actor, command.WorkspaceId, command.Provider, ct);
        if (provider is null)
        {
            return new Result<NookSummary>(Error.Validation("provider", "The workspace has no provider with this ID."));
        }

        bool harnessOffered = command.Harness is null || settings.HarnessImages.ContainsKey(command.Harness);
        if (!harnessOffered)
        {
            return new Result<NookSummary>(Error.Validation("harness", "This deployment offers no harness with this ID."));
        }

        Result<IReadOnlyList<PlannedRepository>> planned = await PlanSourcesAsync(actor, command, ct);
        if (planned.Failed)
        {
            return new Result<NookSummary>(planned.Error);
        }

        UserId? createdBy = actor is UserActor user ? user.UserId : null;
        Nook nook = Nook.Create(command.WorkspaceId, provider.Value, command.Harness, createdBy, command.CopyOf, time);
        List<SourceCopy> copies = [.. planned.Output.Select(repository => SourceCopy.Planned(nook.Id, repository.Name, repository.Repository, repository.Branch))];

        // Recorded before the nook is saved: a record for a nook that never got saved stands alone harmlessly.
        AddResource inWorkspace = new AddResource(Resource.Nook(nook.Id.Value), Resource.Workspace(command.WorkspaceId));
        Result added = await workspaces.AddResourceAsync(actor, inWorkspace, ct);
        if (added.Failed)
        {
            return new Result<NookSummary>(added.Error);
        }

        db.Nooks.Add(nook);
        db.SourceCopies.AddRange(copies);
        Result saved = await db.SaveAsync(ct);
        if (saved.Failed)
        {
            return new Result<NookSummary>(saved.Error);
        }

        reconciler.Wake();
        return new Result<NookSummary>(nook.ToSummary([.. copies.Select(copy => copy.ToContract())]));
    }

    // The repositories the nook starts with, by copy name: the workspace's, each once. A copy of
    // another nook is of one the actor may see, in the same workspace, instead.
    private async Task<Result<IReadOnlyList<PlannedRepository>>> PlanSourcesAsync(Actor actor, CreateNook command, CancellationToken ct)
    {
        if (command.CopyOf is NookId copyOf)
        {
            Result<Nook> source = await FindNookAsync(actor, copyOf, AccessLevel.Read, ct);
            bool copyable = !source.Failed && source.Output.WorkspaceId == command.WorkspaceId && command.Repositories.Count == 0;
            return copyable
                ? new Result<IReadOnlyList<PlannedRepository>>([])
                : new Result<IReadOnlyList<PlannedRepository>>(Error.Validation("copyOf", "Must be another nook of the workspace, without repositories besides."));
        }

        if (command.Repositories.Count > MaxRepositories)
        {
            return new Result<IReadOnlyList<PlannedRepository>>(Error.Validation("repositories", "A nook starts with at most 20 repositories."));
        }

        List<PlannedRepository> planned = [];
        foreach (NookRepository wanted in command.Repositories)
        {
            Result<RepositorySummary> found = await sources.GetRepositoryAsync(actor, wanted.Repository, ct);
            RepositorySummary? repository = found.Failed ? null : found.Output;
            bool usable = repository is not null && repository.WorkspaceId == command.WorkspaceId && !planned.Exists(other => string.Equals(other.Name, repository.Name, StringComparison.Ordinal));
            if (!usable)
            {
                return new Result<IReadOnlyList<PlannedRepository>>(Error.Validation("repositories", "Each must be one of the workspace's repositories, once."));
            }

            planned.Add(new PlannedRepository(repository!.Name, wanted.Repository, wanted.Branch));
        }

        return new Result<IReadOnlyList<PlannedRepository>>(planned);
    }

    // A repository a new nook starts with, under its copy name.
    private sealed record PlannedRepository(string Name, RepositoryId Repository, string? Branch);

    // The provider, if the workspace has it. Nooks run only on their own workspace's machines.
    private async Task<ProviderId?> FindProviderAsync(Actor actor, WorkspaceId workspaceId, string id, CancellationToken ct)
    {
        ProviderId provider = ProviderId.Parse(id);
        if (!providers.Any(candidate => string.Equals(candidate.Name, provider.Name, StringComparison.Ordinal)))
        {
            return null;
        }

        if (!string.Equals(provider.Name, MachineProvider.Name, StringComparison.Ordinal))
        {
            return provider.Location is null ? provider : null;
        }

        MachineId? machineId = MachineProvider.ParseLocation(provider.Location);
        if (machineId is null)
        {
            return null;
        }

        Result<MachineSummary> machine = await machines.GetAsync(actor, machineId.Value, ct);
        bool workspaceMachine = !machine.Failed && machine.Output.WorkspaceId == workspaceId;
        return workspaceMachine ? provider with { Location = MachineProvider.LocationOf(machineId.Value) } : null;
    }
}
