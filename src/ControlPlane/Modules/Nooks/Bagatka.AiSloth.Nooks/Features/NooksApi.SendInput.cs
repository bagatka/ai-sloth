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
    private const int MaxInputBytes = 64 * 1024;

    public async Task<Result> SendInputAsync(Actor actor, SendInput command, CancellationToken ct)
    {
        Result<Process> found = await FindProcessAsync(actor, command.NookId, command.ProcessId, AccessLevel.Write, ct);
        if (found.Failed)
        {
            return new Result(found.Error);
        }

        Process process = found.Output;

        if (command.Data.Length > MaxInputBytes)
        {
            return new Result(Error.Validation("data", "Send at most 64 KiB at once."));
        }

        DaemonConnection? connection = await ConnectionAsync(command.NookId, ct);
        if (connection is null)
        {
            return new Result(NooksErrors.NotReady);
        }

        bool sent = await connection.SendAsync(new DaemonInstruction(new SendInputInstruction(process.Id, command.Data)), ct);
        if (!sent)
        {
            return new Result(NooksErrors.NotReady);
        }

        return new Result(new Success());
    }
}
