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
        switch (await workspaces.GetRoleAsync(actor, command.WorkspaceId, ct))
        {
            case null:
                return new Result<MachineRegistration>(WorkspacesErrors.NotFound);
            case WorkspaceRole.Member:
                return new Result<MachineRegistration>(Error.Forbidden);
            case WorkspaceRole.Owner:
                break;
        }

        if (!MachineName.Parse(command.Name).TryGetValue(out MachineName? name, out Error? invalid))
        {
            return new Result<MachineRegistration>(invalid);
        }

        Machine machine = Machine.Add(command.WorkspaceId, name, time);
        string code = machine.IssueRegistrationCode(time);
        db.Machines.Add(machine);
        if ((await db.SaveAsync(ct)).IsError(out Error? failed))
        {
            return new Result<MachineRegistration>(failed);
        }

        return new Result<MachineRegistration>(new MachineRegistration(Summary(machine), code, machine.CodeExpiresAt!.Value));
    }
}
