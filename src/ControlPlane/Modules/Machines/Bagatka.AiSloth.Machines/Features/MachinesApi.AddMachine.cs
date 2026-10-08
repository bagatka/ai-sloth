using Bagatka.AiSloth.Machines.Data;
using System.Threading;
using System.Threading.Tasks;
using Bagatka.AiSloth.Machines.Contracts;
using Bagatka.AiSloth.Machines.Model;
using Bagatka.AiSloth.Workspaces.Contracts;
using Bagatka.Foundation;
using Bagatka.Foundation.Modules;

namespace Bagatka.AiSloth.Machines;

internal sealed partial class MachinesApi
{
    public async Task<Result<MachineRegistration>> AddAsync(Actor actor, AddMachine command, CancellationToken ct)
    {
        await using MachinesDbContext db = await databases.CreateDbContextAsync(ct);

        AccessLevel? access = await workspaces.GetAccessAsync(actor, Resource.Workspace(command.WorkspaceId), ct);
        if (access is null)
        {
            return new Result<MachineRegistration>(WorkspacesErrors.NotFound);
        }

        if (access < AccessLevel.Manage)
        {
            return new Result<MachineRegistration>(Error.Forbidden);
        }

        Result<MachineName> name = MachineName.Parse(command.Name);
        if (name.Failed)
        {
            return new Result<MachineRegistration>(name.Error);
        }

        Machine machine = Machine.Add(command.WorkspaceId, name.Output, time);
        string code = machine.IssueRegistrationCode(time);
        db.Machines.Add(machine);
        Result saved = await db.SaveAsync(ct);
        if (saved.Failed)
        {
            return new Result<MachineRegistration>(saved.Error);
        }

        return new Result<MachineRegistration>(new MachineRegistration(Summary(machine), code, machine.CodeExpiresAt!.Value));
    }
}
