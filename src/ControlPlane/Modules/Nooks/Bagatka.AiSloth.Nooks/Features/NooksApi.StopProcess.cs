using Bagatka.AiSloth.Nooks.Data;
using System.Threading.Tasks;
using System.Threading;
using Bagatka.AiSloth.Nooks.Contracts;
using Bagatka.AiSloth.Nooks.Daemons;
using Bagatka.AiSloth.Nooks.Model;
using Bagatka.Foundation;

namespace Bagatka.AiSloth.Nooks;

internal sealed partial class NooksApi
{
    public async Task<Result> StopProcessAsync(Actor actor, StopProcess command, CancellationToken ct)
    {
        await using NooksDbContext db = await databases.CreateDbContextAsync(ct);

        Result<Process> found = await FindProcessToOperateAsync(db, actor, command.NookId, command.ProcessId, ct);
        if (found.Failed)
        {
            return new Result(found.Error);
        }

        Process process = found.Output;

        if (process.ExitCode is not null)
        {
            return new Result(new Success());
        }

        Result<DaemonConnection> connected = await lifecycle.ConnectAsync(db, actor, command.NookId, ct);
        if (connected.Failed)
        {
            return new Result(connected.Error);
        }

        DaemonConnection connection = connected.Output;

        bool sent = await connection.SendAsync(new DaemonInstruction(new StopProcessInstruction(process.Id)), ct);
        if (!sent)
        {
            return new Result(NooksErrors.NotReady);
        }

        return new Result(new Success());
    }
}
