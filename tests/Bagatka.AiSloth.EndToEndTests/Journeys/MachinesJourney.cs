using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Net;
using System.Net.Http;
using System.Threading;
using System.Threading.Tasks;
using Bagatka.AiSloth.Cli;
using Bagatka.AiSloth.Machines.Contracts;
using Bagatka.AiSloth.Nooks.Contracts;
using Bagatka.AiSloth.Workspaces.Contracts;
using Grpc.Core;
using Xunit;
using MachineCredential = Bagatka.AiSloth.Cli.MachineCredential;

namespace Bagatka.AiSloth.EndToEndTests;

/// <summary>
/// Journey: Alice's workspace runs nooks on her own computer. She adds it, machine mode registers with
/// a code that works once and connects, and nooks run there and nowhere else; outsiders can't see it;
/// while it's away its nooks wait as unreachable and come back with it; and removing it stops its
/// machine mode. Machine mode runs in the test process against the local Docker Engine.
/// </summary>
public sealed class MachinesJourney(ControlPlane app) : IDisposable
{
    private readonly HttpClient _alice = app.ClientFor("alice-" + Guid.CreateVersion7());
    private readonly HttpClient _erin = app.ClientFor("erin-" + Guid.CreateVersion7());

    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    [Fact]
    public async Task A_workspace_runs_nooks_on_its_own_computer()
    {
        TestWorkspace acme = await TestWorkspace.CreateAsync(app, _alice);
        MachineRegistration added = await Api.ReadAsync<MachineRegistration>(_alice.SendPostAsync(acme.Path + "/machines", new { name = "laptop" }), HttpStatusCode.Created);
        MachineCredential credential = await ARegistrationCodeWorksOnceAsync(added);
        NookSummary nook;
        await using (RunningMachine machine = app.StartMachine(credential))
        {
            nook = await AConnectedMachineRunsItsWorkspacesNooksAsync(acme, added);
        }

        await NoOtherWorkspaceOrOutsiderUsesTheMachineAsync(acme, added);
        await ANookWhoseMachineIsAwayIsUnreachableAsync(acme, nook);
        await using RunningMachine back = app.StartMachine(credential);
        await TheNookComesBackWithItsMachineAndIsDeletedThereAsync(acme, nook);
        await RemovingTheMachineStopsItsMachineModeAsync(added, back);
    }

    public void Dispose()
    {
        _alice.Dispose();
        _erin.Dispose();
    }

    private async Task<MachineCredential> ARegistrationCodeWorksOnceAsync(MachineRegistration added)
    {
        MachineCredential credential = await MachineLink.RegisterAsync(app.MachinesUrl, " " + added.Code + "\n", Ct);
        RpcException again = await Assert.ThrowsAsync<RpcException>(() => MachineLink.RegisterAsync(app.MachinesUrl, added.Code, Ct));
        MachineSummary after = await Api.ReadAsync<MachineSummary>(_alice.SendGetAsync(PathOf(added.Machine)), HttpStatusCode.OK);

        Assert.Equal(added.Machine.Id.Value, credential.MachineId);
        Assert.Equal(MachineStatus.AwaitingRegistration, added.Machine.Status);
        Assert.Equal(StatusCode.Unauthenticated, again.StatusCode);
        Assert.Equal(MachineStatus.Offline, after.Status);
        return credential;
    }

    private async Task<NookSummary> AConnectedMachineRunsItsWorkspacesNooksAsync(TestWorkspace acme, MachineRegistration added)
    {
        ProviderSummary provider = await Api.EventuallyAsync(async () =>
        {
            IReadOnlyList<ProviderSummary> providers = await ProvidersAsync(acme.Path);
            return providers.SingleOrDefault(found => string.Equals(found.Name, "laptop", StringComparison.Ordinal) && found.Available);
        });
        NookSummary nook = await Api.ReadAsync<NookSummary>(_alice.SendPostAsync(acme.Path + "/nooks", new { provider = provider.Id }), HttpStatusCode.Created);
        int? exitCode = await acme.RunAsync(nook.Id, "exit 7");
        MachineSummary machine = await Api.ReadAsync<MachineSummary>(_alice.SendGetAsync(PathOf(added.Machine)), HttpStatusCode.OK);

        Assert.Equal(ProviderId(added), provider.Id);
        Assert.Equal(provider.Id, nook.Provider);
        Assert.Equal(7, exitCode);
        Assert.Equal(MachineStatus.Online, machine.Status);
        return nook;
    }

    private async Task NoOtherWorkspaceOrOutsiderUsesTheMachineAsync(TestWorkspace acme, MachineRegistration added)
    {
        WorkspaceSummary other = await Api.ReadAsync<WorkspaceSummary>(_alice.SendPostAsync("/workspaces", new { name = "Other" }), HttpStatusCode.Created);

        Problem elsewhere = await Api.ProblemAsync(_alice.SendPostAsync(Paths.Workspace(other.Id) + "/nooks", new { provider = ProviderId(added) }), HttpStatusCode.BadRequest);
        IReadOnlyList<ProviderSummary> othersProviders = await ProvidersAsync(Paths.Workspace(other.Id));
        IReadOnlyList<ProviderSummary> acmeProviders = await ProvidersAsync(acme.Path);
        Problem seen = await Api.ProblemAsync(_erin.SendGetAsync(PathOf(added.Machine)), HttpStatusCode.NotFound);
        Problem removed = await Api.ProblemAsync(_erin.DeleteAsync(new Uri(PathOf(added.Machine), UriKind.Relative), Ct), HttpStatusCode.NotFound);

        Assert.True(elsewhere.Errors?.ContainsKey("provider"));
        Assert.DoesNotContain(othersProviders, found => string.Equals(found.Id, ProviderId(added), StringComparison.Ordinal));
        Assert.Contains(acmeProviders, found => string.Equals(found.Id, ProviderId(added), StringComparison.Ordinal));
        Assert.Equal((MachinesErrors.NotFound.Code, MachinesErrors.NotFound.Code), (seen.Code, removed.Code));
    }

    // The machine is away, and its nook's daemon with it: nothing can say whether the nook is gone.
    private async Task ANookWhoseMachineIsAwayIsUnreachableAsync(TestWorkspace acme, NookSummary nook)
    {
        await app.LoseSandboxAsync(nook.Id.Value);

        NookStatus away = await acme.StatusAsync(nook.Id, status => status is NookStatus.Unreachable, TimeSpan.FromSeconds(60));

        Assert.Equal(NookStatus.Unreachable, away);
    }

    private async Task TheNookComesBackWithItsMachineAndIsDeletedThereAsync(TestWorkspace acme, NookSummary nook)
    {
        NookStatus back = await acme.StatusAsync(nook.Id, status => status is NookStatus.Running, TimeSpan.FromSeconds(60));
        await Api.ExpectAsync(_alice.DeleteAsync(new Uri(Paths.Nook(nook.Id), UriKind.Relative), Ct), HttpStatusCode.NoContent);
        bool gone = await Api.GoneOrDeletingAsync(_alice, nook.Id);

        Assert.Equal(NookStatus.Running, back);
        Assert.True(gone);
    }

    private async Task RemovingTheMachineStopsItsMachineModeAsync(MachineRegistration added, RunningMachine machine)
    {
        await Api.ExpectAsync(_alice.DeleteAsync(new Uri(PathOf(added.Machine), UriKind.Relative), Ct), HttpStatusCode.NoContent);

        await machine.Running.WaitAsync(TimeSpan.FromSeconds(60), Ct);
        await Api.ProblemAsync(_alice.SendGetAsync(PathOf(added.Machine)), HttpStatusCode.NotFound);
    }

    private static string PathOf(MachineSummary machine)
    {
        return string.Create(CultureInfo.InvariantCulture, $"/machines/{machine.Id.Value}");
    }

    private static string ProviderId(MachineRegistration added)
    {
        return "machine:" + added.Machine.Id.Value.ToString("D", CultureInfo.InvariantCulture);
    }

    private async Task<IReadOnlyList<ProviderSummary>> ProvidersAsync(string workspacePath)
    {
        return await Api.ReadAsync<IReadOnlyList<ProviderSummary>>(_alice.SendGetAsync(workspacePath + "/providers"), HttpStatusCode.OK);
    }
}
