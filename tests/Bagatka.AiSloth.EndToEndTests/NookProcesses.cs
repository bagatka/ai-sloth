using System.Globalization;
using System.Net;
using System.Net.Http;
using System.Linq;
using System.Threading.Tasks;
using Bagatka.AiSloth.Nooks.Contracts;
using Bagatka.Foundation;

namespace Bagatka.AiSloth.EndToEndTests;

/// <summary>Running a command in a nook through the public API, as people and agents do.</summary>
internal static class NookProcesses
{
    /// <summary>Runs the command in the nook and waits for its exit code.</summary>
    public static async Task<int?> ExitCodeAsync(HttpClient client, NookId nook, string command, params string[] arguments)
    {
        string processes = string.Create(CultureInfo.InvariantCulture, $"/nooks/{nook.Value}/processes");
        ProcessSummary process = await Api.ReadAsync<ProcessSummary>(client.SendPostAsync(processes, new { command, arguments }), HttpStatusCode.OK);
        ProcessSummary exited = await Api.EventuallyAsync(async () =>
        {
            Page<ProcessSummary> listed = await Api.ReadAsync<Page<ProcessSummary>>(client.SendGetAsync(processes), HttpStatusCode.OK);
            return listed.Items.SingleOrDefault(found => found.Id == process.Id && found.ExitCode is not null);
        });
        return exited.ExitCode;
    }
}
