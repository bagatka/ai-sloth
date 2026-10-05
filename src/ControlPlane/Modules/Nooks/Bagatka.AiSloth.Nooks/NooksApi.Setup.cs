using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using Bagatka.AiSloth.Nooks.Contracts;
using Bagatka.AiSloth.Nooks.Daemons;
using Bagatka.AiSloth.Nooks.Model;
using Bagatka.Foundation;

namespace Bagatka.AiSloth.Nooks;

internal sealed partial class NooksApi
{
    // The setup scripts in the nook's files, relative to /work, in the order they run: every setup, then every
    // resume; /work's own before each folder's directly in it, by name.
    private const string FindSetupScript = """
        export LC_ALL=C
        cd /work 2>/dev/null || exit 0
        for kind in setup resume; do
            for script in .agents/$kind */.agents/$kind; do
                if [ -f "$script" ]; then printf '%s\n' "$script"; fi
            done
        done
        """;

    // Runs the setup scripts given, each in its own folder with its time limit, all of them even after
    // one fails, and ends with the first failure's exit code. Scripts write to the log, which this
    // process's output follows, so a service a script leaves running holds the log open, never the
    // output; and nothing reads their input.
    private const string RunSetupScript = """
        set -u
        log=/var/log/aisloth/setup.log
        mkdir -p /var/log/aisloth
        : > "$log"
        run() {
            status=0
            for script in "$@"; do
                case "$script" in
                    */resume) limit=5m ;;
                    *) limit=30m ;;
                esac
                echo "==> $script"
                if [ -x "/work/$script" ]; then
                    (cd "/work/${script%.agents/*}" && timeout "$limit" "/work/$script")
                else
                    (cd "/work/${script%.agents/*}" && timeout "$limit" bash "/work/$script")
                fi
                code=$?
                if [ "$code" -eq 124 ]; then
                    echo "==> $script ran longer than $limit and was stopped"
                elif [ "$code" -ne 0 ]; then
                    echo "==> $script failed with exit code $code"
                fi
                if [ "$code" -ne 0 ] && [ "$status" -eq 0 ]; then
                    status=$code
                fi
            done
            return "$status"
        }
        run "$@" < /dev/null >> "$log" 2>&1 &
        worker=$!
        tail -n +1 -f --sleep-interval=0.2 --pid="$worker" "$log"
        wait "$worker"
        """;

    // Finds the setup scripts in the nook's files, just put in place, and starts them with
    // the workspace's secrets, without waiting for them; only the resume scripts for a nook that woke.
    // Returns the scripts, and the process running them; none when there are no scripts.
    private async Task<Result<SetupStart>> StartSetupAsync(Nook nook, DaemonConnection connection, bool resumeOnly, CancellationToken ct)
    {
        using MemoryStream found = new MemoryStream();
        ProcessRun listed = await RunAsync(connection, FindSetupScript, [], NoVariables, input: null, found, ct);
        if (!listed.Succeeded)
        {
            return new Result<SetupStart>(SourcesFailed("Looking for the nook's setup failed: " + listed.Errors));
        }

        // Not handled: folders whose names hold a line break, which checkpoints refuse too.
        List<string> scripts = [.. Encoding.UTF8.GetString(found.ToArray()).Split('\n', StringSplitOptions.RemoveEmptyEntries)
            .Where(script => !resumeOnly || script.EndsWith("/resume", StringComparison.Ordinal))];
        if (scripts.Count == 0)
        {
            return new Result<SetupStart>(new SetupStart(scripts, Process: null));
        }

        Result<IReadOnlyDictionary<string, string>> resolved = await secrets.ResolveAsync(SystemActors.Processes, nook.WorkspaceId, ct);
        if (resolved.Failed)
        {
            throw new InvalidOperationException("Resolving the secrets of nook " + nook.Id.Value + "'s workspace failed: " + resolved.Error.Message);
        }

        StartProcess command = new StartProcess(nook.Id, "/bin/bash", ["-c", RunSetupScript, "setup", .. scripts], "/work", OutputRetention.Recent, resolved.Output);
        Process process = await RecordAsync(command, ct);
        bool sent = await connection.SendAsync(new DaemonInstruction(process.ToInstruction(resolved.Output, inputStreamed: false)), ct);
        if (!sent)
        {
            return new Result<SetupStart>(SourcesFailed("The nook's daemon disconnected before its setup could start; try again."));
        }

        return new Result<SetupStart>(new SetupStart(scripts, process.Id));
    }

    private sealed record SetupStart(List<string> Scripts, ProcessId? Process);
}
