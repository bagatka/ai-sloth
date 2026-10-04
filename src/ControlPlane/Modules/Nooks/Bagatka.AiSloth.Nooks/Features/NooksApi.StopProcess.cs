using System.Threading;
using System.Threading.Tasks;
using Bagatka.AiSloth.Nooks.Contracts;
using Bagatka.AiSloth.Nooks.Daemons;
using Bagatka.AiSloth.Nooks.Model;
using Bagatka.Foundation;

namespace Bagatka.AiSloth.Nooks;

internal sealed partial class NooksApi
{
    public async Task<Result> StopProcessAsync(Actor actor, StopProcess command, CancellationToken ct)
    {
        if (!(await FindProcessAsync(actor, command.NookId, command.ProcessId, ct)).TryGetValue(out Process? process, out Error? missing))
        {
            return new Result(missing);
        }

        if (process.ExitCode is not null)
        {
            return new Result(new Success());
        }

        DaemonConnection? connection = await ConnectionAsync(command.NookId, ct);
        if (connection is null || !await connection.SendAsync(new DaemonInstruction(new StopProcessInstruction(process.Id)), ct))
        {
            return new Result(NooksErrors.NotReady);
        }

        return new Result(new Success());
    }
}
