using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Net;
using System.Net.Http;
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
/// the nook reports its disk; changed secrets reach new processes; only managers set them; and a
/// deleted nook is gone.
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
        await TheNookReportsHowFullItsDiskIsAsync(acme, nook);
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

    private async Task ANewNooksProcessGetsTheSecretsAndItsOutputReplaysFromAnyOffsetAsync(TestWorkspace acme, NookSummary nook)
    {
        ProcessSummary process = await NookProcesses.StartAsync(Alice, nook.Id, "sh", "-c", "printf %s \"$GH_TOKEN\"; echo oops >&2; exit 3");
        ProcessRun run = await NookProcesses.OutputAsync(Alice, nook.Id, process.Id, fromOffset: 0);
        ProcessRun replay = await NookProcesses.OutputAsync(Alice, nook.Id, process.Id, fromOffset: 4);
        NookSummary after = await acme.NookAsync(nook.Id);

        Assert.Equal(NookStatus.Creating, nook.Status);
        Assert.Equal(new ProcessRun("ghp_example value", "oops\n", 3), run);
        Assert.Equal(("example value", 3), (replay.StandardOutput, replay.ExitCode));
        Assert.Equal(NookStatus.Running, after.Status);
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

    private static async Task TheNookReportsHowFullItsDiskIsAsync(TestWorkspace acme, NookSummary nook)
    {
        DiskUsage disk = await Api.EventuallyAsync(async () =>
        {
            NookSummary current = await acme.NookAsync(nook.Id);
            return current.Disk;
        });

        Assert.InRange(disk.AvailableBytes, 0, disk.TotalBytes);
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
