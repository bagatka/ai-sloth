using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Net;
using System.Net.Http;
using System.Net.Sockets;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using Bagatka.AiSloth.Nooks.Contracts;
using Bagatka.AiSloth.Secrets.Contracts;
using Bagatka.AiSloth.Workspaces.Contracts;
using Xunit;

namespace Bagatka.AiSloth.EndToEndTests;

/// <summary>
/// Journey: Alice runs processes in a nook of her own with her workspace's secrets. A secret's value is
/// never shown again, also when <c>sloth</c> reads it from standard input; every process gets the
/// secrets, writes output anyone can watch again from any offset, takes input, and stops when asked;
/// the nook reports what it uses; it is a computer of its own, out of other nooks' and the host's reach;
/// changed secrets reach new processes; only managers set them; and a deleted nook is gone.
/// </summary>
public sealed class SecretsAndProcessesJourney(ControlPlane app) : IDisposable
{
    private readonly string _person = "alice-" + Guid.CreateVersion7();
    private readonly HttpClient _bob = app.ClientFor("bob-" + Guid.CreateVersion7());
    private HttpClient? _alice;

    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    private HttpClient Alice => _alice!;

    [Fact]
    public async Task Processes_in_a_nook_get_the_workspaces_secrets_and_report_everything_they_do()
    {
        _alice = app.ClientFor(_person);
        await SlothReadsASecretFromStandardInputAndListsItByNameOnlyAsync();
        TestWorkspace acme = await TestWorkspace.CreateAsync(app, Alice);
        await ASecretsValueIsNeverShownAgainAsync(acme);
        NookSummary nook = await Api.ReadAsync<NookSummary>(Alice.SendPostAsync(acme.Path + "/nooks", new { provider = "docker" }), HttpStatusCode.Created);
        await ANewNooksProcessGetsTheSecretsAndItsOutputReplaysFromAnyOffsetAsync(acme, nook);
        await InputReachesARunningProcessAndStoppingEndsItPolitelyAsync(nook);
        await TheNookReportsWhatItUsesAsync(acme, nook);
        await ANookIsAComputerOfItsOwnAsync(acme, nook);
        await ChangedSecretsReachNewProcessesAsync(acme, nook);
        await OnlyManagersSetSecretsAsync(acme);
        await ADeletedNookIsGoneAsync(nook);
    }

    public void Dispose()
    {
        _alice?.Dispose();
        _bob.Dispose();
    }

    private async Task SlothReadsASecretFromStandardInputAndListsItByNameOnlyAsync()
    {
        await using SlothCli sloth = new SlothCli(_person);
        await sloth.RunAsync("host", "add", app.WebApiUrl.AbsoluteUri);

        int set = await sloth.RunWithInputAsync("ghp_from_stdin\n", "secret", "set", "GH_TOKEN");
        int listed = await sloth.RunAsync("secret", "list");

        Assert.Equal((0, 0), (set, listed));
        Assert.Matches("^GH_TOKEN +set just now", sloth.Output);
        Assert.DoesNotContain("ghp_from_stdin", sloth.Output, StringComparison.Ordinal);
    }

    private async Task ASecretsValueIsNeverShownAgainAsync(TestWorkspace acme)
    {
        HttpResponseMessage set = await Alice.SendPutAsync(acme.Path + "/secrets/GH_TOKEN", new { value = "ghp_example value" });
        string setBody = await set.Content.ReadAsStringAsync(Ct);
        SecretSummary secret = await Api.ReadAsync<SecretSummary>(set, HttpStatusCode.OK);
        HttpResponseMessage listed = await Alice.SendGetAsync(acme.Path + "/secrets");
        string listBody = await listed.Content.ReadAsStringAsync(Ct);
        IReadOnlyList<SecretSummary> secrets = await Api.ReadAsync<IReadOnlyList<SecretSummary>>(listed, HttpStatusCode.OK);

        Assert.Equal("GH_TOKEN", secret.Name);
        Assert.Equal(["GH_TOKEN"], secrets.Select(found => found.Name), StringComparer.Ordinal);
        Assert.DoesNotContain("ghp_example", setBody, StringComparison.Ordinal);
        Assert.DoesNotContain("ghp_example", listBody, StringComparison.Ordinal);
    }

    // Offsets count a process's output in the order the daemon read it, which interleaves standard
    // output and standard error as they arrive, so the replay reads a process writing one of them.
    private async Task ANewNooksProcessGetsTheSecretsAndItsOutputReplaysFromAnyOffsetAsync(TestWorkspace acme, NookSummary nook)
    {
        ProcessRun run = await NookProcesses.RunAsync(Alice, nook.Id, "sh", "-c", "echo hello; echo oops >&2; exit 3");
        ProcessSummary secret = await NookProcesses.StartAsync(Alice, nook.Id, "sh", "-c", "printf %s \"$GH_TOKEN\"");
        ProcessRun read = await NookProcesses.OutputAsync(Alice, nook.Id, secret.Id, fromOffset: 0);
        ProcessRun replay = await NookProcesses.OutputAsync(Alice, nook.Id, secret.Id, fromOffset: 4);
        NookSummary after = await acme.NookAsync(nook.Id);

        Assert.Equal(NookStatus.Starting, nook.Status);
        Assert.Equal(new ProcessRun("hello\n", "oops\n", 3), run);
        Assert.Equal(new ProcessRun("ghp_example value", string.Empty, 0), read);
        Assert.Equal(new ProcessRun("example value", string.Empty, 0), replay);
        Assert.Equal(NookStatus.Ready, after.Status);
    }

    private async Task InputReachesARunningProcessAndStoppingEndsItPolitelyAsync(NookSummary nook)
    {
        ProcessSummary process = await NookProcesses.StartAsync(Alice, nook.Id, "cat");
        string path = string.Create(CultureInfo.InvariantCulture, $"{Paths.Nook(nook.Id)}/processes/{process.Id.Value}");
        await using IAsyncEnumerator<ProcessEvent> watching = NookProcesses.WatchAsync(Alice, nook.Id, process.Id, fromOffset: 0, Ct).GetAsyncEnumerator(Ct);

        await Api.ExpectAsync(Alice.SendPostAsync(path + "/input", new { data = Encoding.UTF8.GetBytes("ping\n") }), HttpStatusCode.NoContent);
        bool echoArrived = await watching.MoveNextAsync();
        ProcessEvent echo = watching.Current;
        await Api.ExpectAsync(Alice.SendPostAsync(path + "/stop", new { }), HttpStatusCode.NoContent);
        bool exitArrived = await watching.MoveNextAsync();

        Assert.True(echoArrived);
        Assert.Equal("ping\n", Encoding.UTF8.GetString(Assert.IsType<ProcessOutput>(echo.Value).Data.Span));
        Assert.True(exitArrived);
        Assert.Equal(143, Assert.IsType<ProcessExited>(watching.Current.Value).ExitCode);
    }

    private static async Task TheNookReportsWhatItUsesAsync(TestWorkspace acme, NookSummary nook)
    {
        NookUsage usage = await Api.EventuallyAsync(async () =>
        {
            NookSummary current = await acme.NookAsync(nook.Id);
            return current.Usage;
        });

        Assert.InRange(usage.DiskUsedBytes, 1, usage.DiskTotalBytes);
        Assert.InRange(usage.MemoryUsedBytes, 1, usage.MemoryTotalBytes);
        Assert.True(usage.CpuTotalMillicores > 0);
    }

    // A nook is a computer of its own on the internet, with Docker inside: a server it runs answers in it
    // and nowhere else, even in another nook on the same engine; and of the host, only the control
    // plane answers a nook, also when its root switches IPv6 on to look for the host next door.
    private async Task ANookIsAComputerOfItsOwnAsync(TestWorkspace acme, NookSummary nook)
    {
        NookSummary other = await Api.ReadAsync<NookSummary>(Alice.SendPostAsync(acme.Path + "/nooks", new { provider = "docker" }), HttpStatusCode.Created);
        ProcessRun served = await NookProcesses.RunAsync(Alice, nook.Id, "bash", "-c", """
            docker run -d --rm -p 8000:8000 busybox nc -lk -p 8000 -e echo hi >/dev/null
            for attempt in $(seq 1 60); do timeout 2 bash -c '</dev/tcp/127.0.0.1/8000' 2>/dev/null && break; sleep 1; done
            hostname -I | cut -d' ' -f1
            """);
        string address = served.StandardOutput.Trim();
        using TcpListener onTheHost = new TcpListener(IPAddress.Any, 0);
        onTheHost.Start();
        int hostPort = ((IPEndPoint)onTheHost.LocalEndpoint).Port;

        int? here = await NookProcesses.ExitCodeAsync(Alice, nook.Id, "bash", "-c", "timeout 5 bash -c '</dev/tcp/127.0.0.1/8000'");
        int? fromTheOther = await NookProcesses.ExitCodeAsync(Alice, other.Id, "bash", "-c", "timeout 5 bash -c '</dev/tcp/" + address + "/8000'");
        int? hostService = await NookProcesses.ExitCodeAsync(Alice, other.Id, "bash", "-c", string.Create(CultureInfo.InvariantCulture, $"timeout 5 bash -c '</dev/tcp/host.docker.internal/{hostPort}'"));
        ProcessRun neighbors = await NookProcesses.RunAsync(Alice, other.Id, "bash", "-c", """
            docker run --rm --privileged --network host busybox sh -c '
                ip link set eth0 mtu 1500
                sysctl -qw net.ipv6.conf.eth0.disable_ipv6=0
                until ip -6 address show dev eth0 | grep "scope link" | grep -qv tentative; do sleep 0.2; done
                ping -6 -c 2 -W 2 ff02::1%eth0 | awk "/bytes from/ { print \$4 }" | sort -u'
            """);

        Assert.Equal(0, here);
        Assert.NotEqual(0, fromTheOther);
        Assert.NotEqual(0, hostService);
        Assert.Single(neighbors.StandardOutput.Split('\n', StringSplitOptions.RemoveEmptyEntries));
    }

    private async Task ChangedSecretsReachNewProcessesAsync(TestWorkspace acme, NookSummary nook)
    {
        string secret = acme.Path + "/secrets/GH_TOKEN";

        await Api.ExpectAsync(Alice.SendPutAsync(secret, new { value = "two" }), HttpStatusCode.OK);
        int? replaced = await acme.RunAsync(nook.Id, "test \"$GH_TOKEN\" = two");
        await Api.ExpectAsync(Alice.DeleteAsync(new Uri(secret, UriKind.Relative), Ct), HttpStatusCode.NoContent);
        int? removed = await acme.RunAsync(nook.Id, "test -z \"${GH_TOKEN+set}\"");
        Problem again = await Api.ProblemAsync(Alice.DeleteAsync(new Uri(secret, UriKind.Relative), Ct), HttpStatusCode.NotFound);

        Assert.Equal((0, 0), (replaced, removed));
        Assert.Equal(SecretsErrors.NotFound.Code, again.Code);
    }

    private async Task OnlyManagersSetSecretsAsync(TestWorkspace acme)
    {
        await Api.ExpectAsync(Alice.SendPutAsync(acme.Path + "/secrets/NPM_TOKEN", new { value = "npm_x" }), HttpStatusCode.OK);
        Invite invite = await Api.ReadAsync<Invite>(Alice.SendPostAsync(acme.Path + "/invites", new { access = "Write" }), HttpStatusCode.OK);
        await Api.ExpectAsync(_bob.SendPostAsync("/invites/accept", new { code = invite.Code }), HttpStatusCode.OK);

        IReadOnlyList<SecretSummary> bobSees = await Api.ReadAsync<IReadOnlyList<SecretSummary>>(_bob.SendGetAsync(acme.Path + "/secrets"), HttpStatusCode.OK);
        await Api.ExpectAsync(_bob.SendPutAsync(acme.Path + "/secrets/NPM_TOKEN", new { value = "mine" }), HttpStatusCode.Forbidden);

        Assert.Equal(["NPM_TOKEN"], bobSees.Select(found => found.Name), StringComparer.Ordinal);
    }

    private async Task ADeletedNookIsGoneAsync(NookSummary nook)
    {
        await Api.ExpectAsync(Alice.DeleteAsync(new Uri(Paths.Nook(nook.Id), UriKind.Relative), Ct), HttpStatusCode.NoContent);

        await Api.EventuallyAsync(async () =>
        {
            using HttpResponseMessage found = await Alice.SendGetAsync(Paths.Nook(nook.Id));
            return found.StatusCode == HttpStatusCode.NotFound ? "gone" : null;
        });
    }
}
