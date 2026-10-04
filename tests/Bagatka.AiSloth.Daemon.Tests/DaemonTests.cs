using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using Bagatka.AiSloth.DaemonProtocol.V1;
using Google.Protobuf;
using Xunit;

namespace Bagatka.AiSloth.Daemon.Tests;

/// <summary>
/// The daemon's guarantees from <c>daemon.proto</c>, through a real connection, real processes, and
/// real files.
/// </summary>
public sealed class DaemonTests
{
    private const int Timeout = 30_000;

    private static readonly OutputLimits SmallLimits = new OutputLimits(SegmentBytes: 256, RecentBytes: 1024, UndeliveredBytes: 1024, ChunkBytes: 64);

    // Writes 80 lines of 100 bytes: 8,000 bytes, far beyond the small limits.
    private const string EightThousandBytes = "i=0; while [ $i -lt 80 ]; do printf '%099d\\n' $i; i=$((i+1)); done";

    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    [Fact(Timeout = Timeout)]
    public async Task A_process_runs_and_its_output_and_exit_reach_the_control_plane()
    {
        CancellationToken ct = TestContext.Current.CancellationToken;
        await using FakeControlPlane controlPlane = await FakeControlPlane.StartAsync();
        await using DaemonUnderTest daemon = DaemonUnderTest.Start(controlPlane.Url);
        FakeControlPlane.Connection connection = await controlPlane.Endpoint.NextConnectionAsync(ct);
        string process = NewId();

        await connection.Instructions.Writer.WriteAsync(Start(process, "sh", "-c", "echo hello; echo oops >&2; exit 3"), ct);
        ProcessExited exited = await NextExitAsync(connection, process);
        string watch = NewId();
        await connection.Instructions.Writer.WriteAsync(Watch(watch, process, fromOffset: 0), ct);
        List<ProcessOutput> output = await controlPlane.Endpoint.Upload(watch).ReadAllAsync(ct).ToListAsync(ct);
        ProcessExited? uploadEnd = await controlPlane.Endpoint.UploadExitAsync(watch);

        Assert.Equal("Bearer " + DaemonUnderTest.Token, connection.Authorization);
        Assert.Equal(DaemonUnderTest.NookId.ToString("D", CultureInfo.InvariantCulture), connection.Hello.NookId);
        Assert.Equal(3, exited.ExitCode);
        Assert.Equal(3, uploadEnd?.ExitCode);
        Assert.Equal("hello\n", Text(output, OutputChannel.StandardOutput));
        Assert.Equal("oops\n", Text(output, OutputChannel.StandardError));
        Assert.Equal(0, output.Min(chunk => chunk.Offset));
    }

    [Fact(Timeout = Timeout)]
    public async Task A_process_gets_the_variables_it_was_started_with()
    {
        CancellationToken ct = TestContext.Current.CancellationToken;
        await using FakeControlPlane controlPlane = await FakeControlPlane.StartAsync();
        await using DaemonUnderTest daemon = DaemonUnderTest.Start(controlPlane.Url);
        FakeControlPlane.Connection connection = await controlPlane.Endpoint.NextConnectionAsync(ct);
        string process = NewId();
        DaemonInstruction start = Start(process, "sh", "-c", "printf '%s' \"$GREETING\"");
        start.StartProcess.Environment.Add("GREETING", "hello from the control plane");

        await connection.Instructions.Writer.WriteAsync(start, ct);
        await NextExitAsync(connection, process);
        List<ProcessOutput> output = await WatchAsync(controlPlane, connection, process, fromOffset: 0);

        Assert.Equal("hello from the control plane", Text(output, OutputChannel.StandardOutput));
    }

    [Fact(Timeout = Timeout)]
    public async Task A_process_keeps_running_when_the_connection_is_lost()
    {
        CancellationToken ct = TestContext.Current.CancellationToken;
        await using FakeControlPlane controlPlane = await FakeControlPlane.StartAsync();
        await using DaemonUnderTest daemon = DaemonUnderTest.Start(controlPlane.Url);
        FakeControlPlane.Connection first = await controlPlane.Endpoint.NextConnectionAsync(ct);
        string process = NewId();
        await first.Instructions.Writer.WriteAsync(Start(process, "cat"), ct);
        await first.Instructions.Writer.WriteAsync(Input(process, "before\n"), ct);
        string watch = NewId();
        await first.Instructions.Writer.WriteAsync(Watch(watch, process, 0), ct);
        await WaitForTextAsync(controlPlane, watch, "before\n");

        first.Drop();
        FakeControlPlane.Connection second = await controlPlane.Endpoint.NextConnectionAsync(ct);
        await second.Instructions.Writer.WriteAsync(Input(process, "after\n"), ct);
        string secondWatch = NewId();
        await second.Instructions.Writer.WriteAsync(Watch(secondWatch, process, 0), ct);
        await WaitForTextAsync(controlPlane, secondWatch, "before\nafter\n");
        await second.Instructions.Writer.WriteAsync(Stop(process), ct);
        ProcessExited exited = await NextExitAsync(second, process);
        List<ProcessOutput> output = await WatchAsync(controlPlane, second, process, fromOffset: 0);

        Assert.Contains(second.Hello.RunningProcesses, running => string.Equals(running.ProcessId, process, StringComparison.Ordinal));
        Assert.Equal("before\nafter\n", Text(output, OutputChannel.StandardOutput));
        Assert.Equal(128 + 15, exited.ExitCode);
    }

    [Fact(Timeout = Timeout)]
    public async Task A_watch_starts_at_any_offset()
    {
        CancellationToken ct = TestContext.Current.CancellationToken;
        await using FakeControlPlane controlPlane = await FakeControlPlane.StartAsync();
        await using DaemonUnderTest daemon = DaemonUnderTest.Start(controlPlane.Url);
        FakeControlPlane.Connection connection = await controlPlane.Endpoint.NextConnectionAsync(ct);
        string process = NewId();
        await connection.Instructions.Writer.WriteAsync(Start(process, "sh", "-c", "printf 'hello\\nworld\\n'"), ct);
        await NextExitAsync(connection, process);

        List<ProcessOutput> output = await WatchAsync(controlPlane, connection, process, fromOffset: 6);

        Assert.Equal("world\n", Text(output, OutputChannel.StandardOutput));
        Assert.Equal(6, output[0].Offset);
    }

    [Fact(Timeout = Timeout)]
    public async Task Reconnect_moves_the_daemon_to_a_new_connection_without_touching_processes()
    {
        CancellationToken ct = TestContext.Current.CancellationToken;
        await using FakeControlPlane controlPlane = await FakeControlPlane.StartAsync();
        await using DaemonUnderTest daemon = DaemonUnderTest.Start(controlPlane.Url);
        FakeControlPlane.Connection first = await controlPlane.Endpoint.NextConnectionAsync(ct);
        string process = NewId();
        await first.Instructions.Writer.WriteAsync(Start(process, "sleep", "30"), ct);

        await first.Instructions.Writer.WriteAsync(new DaemonInstruction { Reconnect = new Reconnect() }, ct);
        FakeControlPlane.Connection second = await controlPlane.Endpoint.NextConnectionAsync(ct);

        Assert.Contains(second.Hello.RunningProcesses, running => string.Equals(running.ProcessId, process, StringComparison.Ordinal));
    }

    [Fact(Timeout = Timeout)]
    public async Task A_watch_of_a_process_the_daemon_does_not_know_ends_with_exit_code_minus_one()
    {
        CancellationToken ct = TestContext.Current.CancellationToken;
        await using FakeControlPlane controlPlane = await FakeControlPlane.StartAsync();
        await using DaemonUnderTest daemon = DaemonUnderTest.Start(controlPlane.Url);
        FakeControlPlane.Connection connection = await controlPlane.Endpoint.NextConnectionAsync(ct);
        string watch = NewId();

        await connection.Instructions.Writer.WriteAsync(Watch(watch, NewId(), fromOffset: 0), ct);
        List<ProcessOutput> output = await controlPlane.Endpoint.Upload(watch).ReadAllAsync(ct).ToListAsync(ct);
        ProcessExited? uploadEnd = await controlPlane.Endpoint.UploadExitAsync(watch);

        Assert.Empty(output);
        Assert.Equal(-1, uploadEnd?.ExitCode);
    }

    [Fact(Timeout = Timeout)]
    public async Task A_program_that_cannot_start_exits_with_127_and_says_why()
    {
        CancellationToken ct = TestContext.Current.CancellationToken;
        await using FakeControlPlane controlPlane = await FakeControlPlane.StartAsync();
        await using DaemonUnderTest daemon = DaemonUnderTest.Start(controlPlane.Url);
        FakeControlPlane.Connection connection = await controlPlane.Endpoint.NextConnectionAsync(ct);
        string process = NewId();

        await connection.Instructions.Writer.WriteAsync(Start(process, "no-such-program-anywhere"), ct);
        ProcessExited exited = await NextExitAsync(connection, process);
        List<ProcessOutput> output = await WatchAsync(controlPlane, connection, process, fromOffset: 0);

        Assert.Equal(127, exited.ExitCode);
        Assert.Contains("cannot start 'no-such-program-anywhere'", Text(output, OutputChannel.StandardError), StringComparison.Ordinal);
    }

    [Fact(Timeout = Timeout)]
    public async Task Stop_asks_the_process_politely_first()
    {
        CancellationToken ct = TestContext.Current.CancellationToken;
        await using FakeControlPlane controlPlane = await FakeControlPlane.StartAsync();
        await using DaemonUnderTest daemon = DaemonUnderTest.Start(controlPlane.Url);
        FakeControlPlane.Connection connection = await controlPlane.Endpoint.NextConnectionAsync(ct);
        string process = NewId();
        await connection.Instructions.Writer.WriteAsync(Start(process, "sh", "-c", "trap 'echo bye; exit 0' TERM; echo ready; while true; do sleep 0.1; done"), ct);
        string watch = NewId();
        await connection.Instructions.Writer.WriteAsync(Watch(watch, process, 0), ct);
        await WaitForTextAsync(controlPlane, watch, "ready\n");

        await connection.Instructions.Writer.WriteAsync(Stop(process), ct);
        ProcessExited exited = await NextExitAsync(connection, process);
        List<ProcessOutput> output = await WatchAsync(controlPlane, connection, process, fromOffset: 0);

        Assert.Equal(0, exited.ExitCode);
        Assert.Equal("ready\nbye\n", Text(output, OutputChannel.StandardOutput));
    }

    [Fact(Timeout = Timeout)]
    public async Task Recent_retention_keeps_the_latest_output_and_drops_the_oldest()
    {
        CancellationToken ct = TestContext.Current.CancellationToken;
        await using FakeControlPlane controlPlane = await FakeControlPlane.StartAsync();
        await using DaemonUnderTest daemon = DaemonUnderTest.Start(controlPlane.Url, SmallLimits);
        FakeControlPlane.Connection connection = await controlPlane.Endpoint.NextConnectionAsync(ct);
        string process = NewId();

        await connection.Instructions.Writer.WriteAsync(Start(process, OutputRetention.Recent, "sh", "-c", EightThousandBytes), ct);
        await NextExitAsync(connection, process);
        List<ProcessOutput> output = await WatchAsync(controlPlane, connection, process, fromOffset: 0);

        long first = output.Min(chunk => chunk.Offset);
        long end = output.Max(chunk => chunk.Offset + chunk.Data.Length);
        Assert.True(first > 0, "The oldest output should have been dropped.");
        Assert.Equal(8000, end);
        Assert.True(end - first >= SmallLimits.RecentBytes, "At least the latest output should be kept.");
        Assert.Equal(end - first, output.Sum(chunk => (long)chunk.Data.Length));
    }

    [Fact(Timeout = Timeout)]
    public async Task Complete_retention_loses_nothing_beyond_the_limits()
    {
        CancellationToken ct = TestContext.Current.CancellationToken;
        await using FakeControlPlane controlPlane = await FakeControlPlane.StartAsync();
        await using DaemonUnderTest daemon = DaemonUnderTest.Start(controlPlane.Url, SmallLimits);
        FakeControlPlane.Connection connection = await controlPlane.Endpoint.NextConnectionAsync(ct);
        string process = NewId();

        // Nothing watches yet, so the process blocks once 1 KiB waits instead of losing output.
        await connection.Instructions.Writer.WriteAsync(Start(process, OutputRetention.Complete, "sh", "-c", EightThousandBytes), ct);
        List<ProcessOutput> output = await WatchAsync(controlPlane, connection, process, fromOffset: 0);
        ProcessExited exited = await NextExitAsync(connection, process);

        Assert.Equal(0, exited.ExitCode);
        Assert.Equal(0, output.Min(chunk => chunk.Offset));
        Assert.Equal(8000, output.Sum(chunk => (long)chunk.Data.Length));
    }

    [Fact(Timeout = Timeout)]
    public async Task The_daemon_reports_how_full_the_disk_is_when_it_connects()
    {
        CancellationToken ct = TestContext.Current.CancellationToken;
        await using FakeControlPlane controlPlane = await FakeControlPlane.StartAsync();
        await using DaemonUnderTest daemon = DaemonUnderTest.Start(controlPlane.Url);
        FakeControlPlane.Connection connection = await controlPlane.Endpoint.NextConnectionAsync(ct);

        DiskUsage disk = await NextDiskUsageAsync(connection);

        Assert.True(disk.TotalBytes > 0);
        Assert.InRange(disk.AvailableBytes, 0, disk.TotalBytes);
    }

    [Fact(Timeout = Timeout)]
    public async Task A_full_disk_releases_the_reserve_and_no_output_is_lost()
    {
        CancellationToken ct = TestContext.Current.CancellationToken;
        await using SmallDisk smallDisk = await SmallDisk.MountAsync(mebibytes: 4);
        await using FakeControlPlane controlPlane = await FakeControlPlane.StartAsync();
        await using DaemonUnderTest daemon = DaemonUnderTest.Start(controlPlane.Url, SmallLimits, smallDisk.Path, diskReserveBytes: 1 << 20);
        FakeControlPlane.Connection connection = await controlPlane.Endpoint.NextConnectionAsync(ct);
        DiskUsage before = await NextDiskUsageAsync(connection);
        await smallDisk.FillAsync();
        string process = NewId();

        await connection.Instructions.Writer.WriteAsync(Start(process, OutputRetention.Complete, "sh", "-c", EightThousandBytes), ct);
        List<ProcessOutput> output = await WatchAsync(controlPlane, connection, process, fromOffset: 0);
        DiskUsage full = await NextDiskUsageAsync(connection);

        string expected = string.Concat(Enumerable.Range(0, 80).Select(line => line.ToString("D99", CultureInfo.InvariantCulture) + "\n"));
        Assert.Equal(expected, Text(output, OutputChannel.StandardOutput));
        Assert.True(full.AvailableBytes < before.AvailableBytes);
    }

    private static string NewId()
    {
        return Guid.CreateVersion7().ToString("D", CultureInfo.InvariantCulture);
    }

    private static DaemonInstruction Start(string process, string command, params string[] arguments)
    {
        return Start(process, OutputRetention.Recent, command, arguments);
    }

    private static DaemonInstruction Start(string process, OutputRetention retention, string command, params string[] arguments)
    {
        StartProcess start = new StartProcess { ProcessId = process, Command = command, Retention = retention };
        start.Arguments.AddRange(arguments);
        return new DaemonInstruction { StartProcess = start };
    }

    private static DaemonInstruction Input(string process, string text)
    {
        return new DaemonInstruction { SendInput = new SendInput { ProcessId = process, Data = ByteString.CopyFromUtf8(text) } };
    }

    private static DaemonInstruction Stop(string process)
    {
        return new DaemonInstruction { StopProcess = new StopProcess { ProcessId = process } };
    }

    private static DaemonInstruction Watch(string watch, string process, long fromOffset)
    {
        return new DaemonInstruction { WatchOutput = new WatchOutput { WatchId = watch, ProcessId = process, FromOffset = fromOffset } };
    }

    private static async Task<ProcessExited> NextExitAsync(FakeControlPlane.Connection connection, string process)
    {
        await foreach (DaemonEvent daemonEvent in connection.Events.Reader.ReadAllAsync(Ct))
        {
            if (daemonEvent.ProcessExited is { } exited && string.Equals(exited.ProcessId, process, StringComparison.Ordinal))
            {
                return exited;
            }
        }

        throw new InvalidOperationException("The connection ended before the process exited.");
    }

    private static async Task<DiskUsage> NextDiskUsageAsync(FakeControlPlane.Connection connection)
    {
        await foreach (DaemonEvent daemonEvent in connection.Events.Reader.ReadAllAsync(Ct))
        {
            if (daemonEvent.DiskUsage is { } disk)
            {
                return disk;
            }
        }

        throw new InvalidOperationException("The connection ended before the daemon reported its disk.");
    }

    // Watches from an offset and returns everything uploaded until the upload ends.
    private static async Task<List<ProcessOutput>> WatchAsync(FakeControlPlane controlPlane, FakeControlPlane.Connection connection, string process, long fromOffset)
    {
        string watch = NewId();
        await connection.Instructions.Writer.WriteAsync(Watch(watch, process, fromOffset), Ct);
        return await controlPlane.Endpoint.Upload(watch).ReadAllAsync(Ct).ToListAsync(Ct);
    }

    private static async Task WaitForTextAsync(FakeControlPlane controlPlane, string watch, string text)
    {
        StringBuilder received = new StringBuilder();
        await foreach (ProcessOutput chunk in controlPlane.Endpoint.Upload(watch).ReadAllAsync(Ct))
        {
            received.Append(chunk.Data.ToStringUtf8());
            if (received.ToString().Contains(text, StringComparison.Ordinal))
            {
                return;
            }
        }
    }

    private static string Text(List<ProcessOutput> output, OutputChannel channel)
    {
        return string.Concat(output.Where(chunk => chunk.Channel == channel).OrderBy(chunk => chunk.Offset).Select(chunk => chunk.Data.ToStringUtf8()));
    }
}
