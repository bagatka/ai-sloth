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
using Grpc.Net.Client;
using Bagatka.Sandboxing.Remote.V1;
using Bagatka.AiSloth.MachineProtocol.V1;
using Xunit;
using MachineCredential = Bagatka.AiSloth.Cli.MachineCredential;
using MachinesClient = Bagatka.AiSloth.MachineProtocol.V1.Machines.MachinesClient;

namespace Bagatka.AiSloth.EndToEndTests;

/// <summary>
/// Journey: Alice's workspace runs nooks on her own computer. She adds it, machine mode registers with
/// a code that works once and connects, and nooks run there and nowhere else; outsiders can't see it;
/// while it's away its nooks wait as unreachable and come back with it; a computer that stops answering
/// without closing its connection, as one gone to sleep, goes offline; and removing it while machine mode
/// is stopped fails its nooks at once and cuts off their daemons, still running there, and machine mode,
/// run again, deletes them from the computer and stops. Machine mode runs in the test process against
/// the local Docker Engine.
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
        await AMachineThatStopsAnsweringGoesOfflineAsync(acme);
        await ANookWhoseMachineIsAwayIsUnreachableAsync(acme, nook);
        NookSummary left;
        await using (RunningMachine back = app.StartMachine(credential))
        {
            await TheNookComesBackWithItsMachineAndIsDeletedThereAsync(acme, nook);
            left = await Api.ReadAsync<NookSummary>(_alice.SendPostAsync(acme.Path + "/nooks", new { provider = ProviderId(added) }), HttpStatusCode.Created);
            await acme.RunAsync(left.Id, "true");
        }

        await RemovingTheMachineFailsItsNooksAtOnceAsync(acme, added, left);
        await MachineModeOfARemovedMachineDeletesItsNooksAndStopsAsync(acme, credential, left);
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

    // A computer gone to sleep keeps its connection open and says nothing: it is asked every 15 seconds
    // whether it is there and has 15 to answer, so it goes offline within half a minute.
    private async Task AMachineThatStopsAnsweringGoesOfflineAsync(TestWorkspace acme)
    {
        MachineRegistration added = await Api.ReadAsync<MachineRegistration>(_alice.SendPostAsync(acme.Path + "/machines", new { name = "asleep" }), HttpStatusCode.Created);
        MachineCredential credential = await MachineLink.RegisterAsync(app.MachinesUrl, added.Code, Ct);
        using GrpcChannel channel = GrpcChannel.ForAddress(app.MachinesUrl);
        Metadata authorization = new Metadata { { "authorization", "Bearer " + credential.Token } };
        using AsyncDuplexStreamingCall<MachineMessage, SandboxCall> silent = new MachinesClient(channel).Connect(authorization, cancellationToken: Ct);
        Hello hello = new Hello { MachineId = credential.MachineId.ToString("D", CultureInfo.InvariantCulture), SlothVersion = "silent" };
        await silent.RequestStream.WriteAsync(new MachineMessage { Hello = hello }, Ct);

        MachineSummary online = await Api.EventuallyAsync(async () =>
        {
            MachineSummary machine = await Api.ReadAsync<MachineSummary>(_alice.SendGetAsync(PathOf(added.Machine)), HttpStatusCode.OK);
            return machine.Status == MachineStatus.Online ? machine : null;
        });
        MachineSummary offline = await Api.EventuallyAsync(async () =>
        {
            MachineSummary machine = await Api.ReadAsync<MachineSummary>(_alice.SendGetAsync(PathOf(added.Machine)), HttpStatusCode.OK);
            return machine.Status == MachineStatus.Offline ? machine : null;
        });

        Assert.Equal((MachineStatus.Online, MachineStatus.Offline), (online.Status, offline.Status));
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

    // Machine mode is stopped, but the nook's container runs on, its daemon connected: removing the
    // machine fails the nook at once and cuts the daemon off for good.
    private async Task RemovingTheMachineFailsItsNooksAtOnceAsync(TestWorkspace acme, MachineRegistration added, NookSummary nook)
    {
        await Api.ExpectAsync(_alice.DeleteAsync(new Uri(PathOf(added.Machine), UriKind.Relative), Ct), HttpStatusCode.NoContent);

        NookStatus failed = await acme.StatusAsync(nook.Id, status => status is NookStatus.Failed, TimeSpan.FromSeconds(60));
        bool stillThere = await app.SandboxExistsAsync(nook.Id.Value);
        await Api.ProblemAsync(_alice.SendGetAsync(PathOf(added.Machine)), HttpStatusCode.NotFound);

        Assert.Equal(NookStatus.Failed, failed);
        Assert.True(stillThere);
    }

    // The computer runs machine mode again: it is refused, deletes its nooks, and stops. The nook stays
    // failed, as its daemon can't bring it back, and its record is deleted like any other.
    private async Task MachineModeOfARemovedMachineDeletesItsNooksAndStopsAsync(TestWorkspace acme, MachineCredential credential, NookSummary nook)
    {
        await using RunningMachine again = app.StartMachine(credential);

        int deleted = await again.Running.WaitAsync(TimeSpan.FromSeconds(60), Ct);
        bool stillThere = await app.SandboxExistsAsync(nook.Id.Value);
        NookSummary afterwards = await acme.NookAsync(nook.Id);
        await Api.ExpectAsync(_alice.DeleteAsync(new Uri(Paths.Nook(nook.Id), UriKind.Relative), Ct), HttpStatusCode.NoContent);
        bool gone = await Api.GoneOrDeletingAsync(_alice, nook.Id);

        Assert.True(deleted >= 1, "Machine mode deleted none of its nooks.");
        Assert.False(stillThere, "The nook's container is still on the computer.");
        Assert.Equal(NookStatus.Failed, afterwards.Status);
        Assert.True(gone);
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
