using System.Threading;
using System.Threading.Tasks;
using Bagatka.AiSloth.Machines.Contracts;
using Bagatka.AiSloth.Machines.Model;
using Bagatka.Foundation;
using Bagatka.Foundation.Modules;
using Microsoft.EntityFrameworkCore;

namespace Bagatka.AiSloth.Machines;

internal sealed partial class MachinesApi
{
    public async Task<Result<MachineCredential>> RegisterAsync(Actor actor, RegisterMachine command, CancellationToken ct)
    {
        // The code is the credential; the actor is always anonymous.
        byte[] codeHash = Machine.HashCode(command.Code);
        Machine? machine = await db.Machines.SingleOrDefaultAsync(found => found.CodeHash == codeHash, ct);
        string? token = machine?.Register(time);
        if (machine is null || token is null)
        {
            return new Result<MachineCredential>(Error.Unauthorized);
        }

        // Two registrations racing with one code: the other one won.
        if ((await db.SaveAsync(ct)).IsError(out _))
        {
            return new Result<MachineCredential>(Error.Unauthorized);
        }

        return new Result<MachineCredential>(new MachineCredential(machine.Id, token));
    }
}
