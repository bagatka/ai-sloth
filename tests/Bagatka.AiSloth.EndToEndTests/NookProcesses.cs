using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Net;
using System.Net.Http;
using System.Net.ServerSentEvents;
using System.Runtime.CompilerServices;
using System.Text;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using Bagatka.AiSloth.Nooks.Contracts;
using Bagatka.Foundation;
using Xunit;

namespace Bagatka.AiSloth.EndToEndTests;

/// <summary>Running a command in a nook through the public API, as people and agents do.</summary>
internal static class NookProcesses
{
    /// <summary>Starts the command in the nook, waiting for its daemon on the nook's first.</summary>
    public static async Task<ProcessSummary> StartAsync(HttpClient client, NookId nook, string command, params string[] arguments)
    {
        return await Api.ReadAsync<ProcessSummary>(client.SendPostAsync(Paths.Nook(nook) + "/processes", new { command, arguments }), HttpStatusCode.OK);
    }

    /// <summary>Runs the command in the nook and waits for its exit code.</summary>
    public static async Task<int?> ExitCodeAsync(HttpClient client, NookId nook, string command, params string[] arguments)
    {
        string processes = Paths.Nook(nook) + "/processes";
        ProcessSummary process = await StartAsync(client, nook, command, arguments);
        ProcessSummary exited = await Api.EventuallyAsync(async () =>
        {
            Page<ProcessSummary> listed = await Api.ReadAsync<Page<ProcessSummary>>(client.SendGetAsync(processes), HttpStatusCode.OK);
            return listed.Items.SingleOrDefault(found => found.Id == process.Id && found.ExitCode is not null);
        });
        return exited.ExitCode;
    }

    /// <summary>Runs the command in the nook and reads what it wrote until it exits.</summary>
    public static async Task<ProcessRun> RunAsync(HttpClient client, NookId nook, string command, params string[] arguments)
    {
        ProcessSummary process = await StartAsync(client, nook, command, arguments);
        return await OutputAsync(client, nook, process.Id, fromOffset: 0);
    }

    /// <summary>What the process wrote from <paramref name="fromOffset"/> on, and its exit code.</summary>
    public static async Task<ProcessRun> OutputAsync(HttpClient client, NookId nook, ProcessId process, long fromOffset)
    {
        StringBuilder standardOutput = new StringBuilder();
        StringBuilder standardError = new StringBuilder();
        await foreach (ProcessEvent processEvent in WatchAsync(client, nook, process, fromOffset, TestContext.Current.CancellationToken))
        {
            switch (processEvent)
            {
                case ProcessOutput output:
                    (output.Channel == OutputChannel.StandardOutput ? standardOutput : standardError).Append(Encoding.UTF8.GetString(output.Data.Span));
                    break;
                case ProcessExited exited:
                    return new ProcessRun(standardOutput.ToString(), standardError.ToString(), exited.ExitCode);
            }
        }

        throw new InvalidOperationException("The watch ended without the process's exit.");
    }

    /// <summary>The process's events, read from its server-sent events.</summary>
    public static async IAsyncEnumerable<ProcessEvent> WatchAsync(HttpClient client, NookId nook, ProcessId process, long fromOffset, [EnumeratorCancellation] CancellationToken ct)
    {
        Uri output = new Uri(string.Create(CultureInfo.InvariantCulture, $"{Paths.Nook(nook)}/processes/{process.Value}/output?fromOffset={fromOffset}"), UriKind.Relative);
        using HttpResponseMessage response = await client.GetAsync(output, HttpCompletionOption.ResponseHeadersRead, ct);
        await Api.ExpectAsync(response, HttpStatusCode.OK);
        await using Stream stream = await response.Content.ReadAsStreamAsync(ct);
        await foreach (SseItem<string> item in SseParser.Create(stream).EnumerateAsync(ct))
        {
            yield return item.EventType switch
            {
                nameof(ProcessOutput) => new ProcessEvent(JsonSerializer.Deserialize<ProcessOutput>(item.Data, FoundationJson.Options)!),
                nameof(ProcessExited) => new ProcessEvent(JsonSerializer.Deserialize<ProcessExited>(item.Data, FoundationJson.Options)!),
                _ => throw new InvalidOperationException("Unexpected event " + item.EventType),
            };
        }
    }
}
