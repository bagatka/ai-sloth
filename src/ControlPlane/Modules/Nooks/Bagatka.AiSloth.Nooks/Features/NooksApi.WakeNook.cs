using System;
using System.Threading;
using System.Threading.Tasks;
using Bagatka.AiSloth.Nooks.Contracts;
using Bagatka.AiSloth.Nooks.Model;
using Bagatka.AiSloth.Workspaces.Contracts;
using Bagatka.Foundation;

namespace Bagatka.AiSloth.Nooks;

internal sealed partial class NooksApi
{
    private static readonly TimeSpan LongestKeepAwake = TimeSpan.FromHours(1);

    public async Task<Result> WakeAsync(Actor actor, WakeNook command, CancellationToken ct)
    {
        NookId id = command.NookId;
        Result<Nook> nook = await FindNookAsync(actor, id, AccessLevel.Write, ct);
        if (nook.Failed)
        {
            return new Result(nook.Error);
        }

        if (command.KeepAwakeFor is TimeSpan span)
        {
            if (span <= TimeSpan.Zero || span > LongestKeepAwake)
            {
                return new Result(Error.Validation("keepAwakeFor", "Must be more than zero and at most an hour."));
            }

            activity.KeepAwake(id, span);
        }
        else
        {
            activity.Used(id);
        }
        if (!nook.Output.Asleep)
        {
            return new Result(new Success());
        }

        bool woke = await sleeper.WakeAsync(id, actor, ct);
        return woke ? new Result(new Success()) : new Result(NooksErrors.NotReady);
    }
}
