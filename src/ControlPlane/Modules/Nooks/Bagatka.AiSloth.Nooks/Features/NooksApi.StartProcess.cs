using System.Threading.Tasks;
using System.Threading;
using System;
using System.Collections.Generic;
using Bagatka.AiSloth.Nooks.Contracts;
using Bagatka.AiSloth.Nooks.Daemons;
using Bagatka.AiSloth.Nooks.Model;
using Bagatka.AiSloth.Workspaces.Contracts;
using Bagatka.Foundation.Modules;
using Bagatka.Foundation;

namespace Bagatka.AiSloth.Nooks;

internal sealed partial class NooksApi
{
    public async Task<Result<ProcessSummary>> StartProcessAsync(Actor actor, StartProcess command, CancellationToken ct)
    {
        Result<Nook> nook = await FindNookAsync(actor, command.NookId, AccessLevel.Write, ct);
        if (nook.Failed)
        {
            return new Result<ProcessSummary>(nook.Error);
        }

        Result<Process> started = Process.Start(command, time);
        if (started.Failed)
        {
            return new Result<ProcessSummary>(started.Error);
        }

        Process process = started.Output;

        // Every process gets the workspace's secrets, as they are now; its own variables win.
        Result<IReadOnlyDictionary<string, string>> resolved = await secrets.ResolveAsync(SystemActors.Processes, nook.Output.WorkspaceId, ct);
        if (resolved.Failed)
        {
            throw new InvalidOperationException("Resolving the secrets of nook " + command.NookId.Value + "'s workspace failed: " + resolved.Error.Message);
        }

        Dictionary<string, string> environment = new Dictionary<string, string>(resolved.Output, StringComparer.Ordinal);
        foreach (KeyValuePair<string, string> variable in command.Environment)
        {
            environment[variable.Key] = variable.Value;
        }

        DaemonConnection? connection = await ConnectionAsync(command.NookId, ct);
        if (connection is null)
        {
            return new Result<ProcessSummary>(NooksErrors.NotReady);
        }

        // Nothing runs before the nook's sources are in place, agents included.
        Result prepared = await PrepareSourcesAsync(nook.Output, connection, ct);
        if (prepared.Failed)
        {
            return new Result<ProcessSummary>(prepared.Error);
        }

        db.Processes.Add(process);
        Result saved = await db.SaveAsync(ct);
        if (saved.Failed)
        {
            return new Result<ProcessSummary>(saved.Error);
        }

        // Not handled: the connection ending between the commit and the send, which leaves the
        // process recorded as running. Handling it would mean comparing recorded processes with the
        // daemon's hello when it reconnects.
        bool sent = await connection.SendAsync(new DaemonInstruction(process.ToInstruction(environment, inputStreamed: false)), ct);
        if (!sent)
        {
            throw new InvalidOperationException("Nook " + command.NookId.Value + "'s daemon disconnected before process " + process.Id.Value + " was sent.");
        }

        return new Result<ProcessSummary>(process.ToSummary());
    }
}
