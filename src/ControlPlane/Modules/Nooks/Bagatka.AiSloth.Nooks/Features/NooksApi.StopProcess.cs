using System.Threading.Tasks;
using System.Threading;
using Bagatka.AiSloth.Nooks.Contracts;
using Bagatka.AiSloth.Nooks.Daemons;
using Bagatka.AiSloth.Nooks.Model;
using Bagatka.AiSloth.Workspaces.Contracts;
using Bagatka.Foundation;

namespace Bagatka.AiSloth.Nooks;

internal sealed partial class NooksApi
{
    public async Task<Result> StopProcessAsync(Actor actor, StopProcess command, CancellationToken ct)
    {
        Result<Process> found = await FindProcessAsync(actor, command.NookId, command.ProcessId, AccessLevel.Write, ct);
        if (found.Failed)
        {
            return new Result(found.Error);
        }

        Process process = found.Output;

        if (process.ExitCode is not null)
        {
            return new Result(new Success());
        }

        DaemonConnection? connection = await ConnectionAsync(actor, command.NookId, ct);
        if (connection is null)
        {
            return new Result(NooksErrors.NotReady);
        }

        bool sent = await connection.SendAsync(new DaemonInstruction(new StopProcessInstruction(process.Id)), ct);
        if (!sent)
        {
            return new Result(NooksErrors.NotReady);
        }

        return new Result(new Success());
    }
}
