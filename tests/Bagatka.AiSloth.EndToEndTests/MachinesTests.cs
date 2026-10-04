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
using Bagatka.Foundation;
using Grpc.Core;
using Xunit;
using MachineCredential = Bagatka.AiSloth.Cli.MachineCredential;

namespace Bagatka.AiSloth.EndToEndTests;

/// <summary>
/// A workspace's own computers as providers: an owner adds one, machine mode registers and connects,
/// and nooks run there. Machine mode runs in the test process against the local Docker Engine.
/// </summary>
public sealed class MachinesTests(ControlPlane controlPlane) : IDisposable
{
    private static readonly string[] ExitSeven = ["-c", "exit 7"];

    private readonly HttpClient _alice = controlPlane.ClientFor("alice-" + Guid.CreateVersion7());

    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    [Fact]
    public async Task A_connected_machine_runs_its_workspace_nooks()
    {
        WorkspaceSummary workspace = await CreateWorkspaceAsync();
        MachineRegistration added = await AddMachineAsync(workspace, "hetzner-1");
        MachineCredential credential = await MachineLink.RegisterAsync(controlPlane.MachinesUrl, added.Code, Ct);
        await using RunningMachine machine = controlPlane.StartMachine(credential);

        ProviderSummary provider = await Api.EventuallyAsync(async () =>
        {
            IReadOnlyList<ProviderSummary> providers = await ProvidersAsync(workspace);
            return providers.SingleOrDefault(found => string.Equals(found.Name, "hetzner-1", StringComparison.Ordinal) && found.Available);
        });
        NookSummary nook = await Api.ReadAsync<NookSummary>(_alice.SendPostAsync(WorkspaceNooksPath(workspace), new { provider = provider.Id }), HttpStatusCode.Created);
        ProcessSummary process = await Api.ReadAsync<ProcessSummary>(_alice.SendPostAsync(PathOf(nook) + "/processes", new { command = "sh", arguments = ExitSeven }), HttpStatusCode.OK);
        ProcessSummary exited = await Api.EventuallyAsync(async () =>
        {
            Page<ProcessSummary> processes = await ProcessesAsync(nook);
            return processes.Items.SingleOrDefault(found => found.Id == process.Id && found.ExitCode is not null);
        });
        await Api.ExpectAsync(_alice.DeleteAsync(new Uri(PathOf(nook), UriKind.Relative), Ct), HttpStatusCode.NoContent);
        await Api.EventuallyAsync(async () =>
        {
            HttpResponseMessage found = await _alice.SendGetAsync(PathOf(nook));
            return found.StatusCode == HttpStatusCode.NotFound ? "gone" : null;
        });
        MachineSummary after = await GetMachineAsync(added.Machine);

        Assert.Equal(added.Machine.Id.Value, credential.MachineId);
        Assert.Equal("machine:" + added.Machine.Id.Value.ToString("D", CultureInfo.InvariantCulture), provider.Id);
        Assert.Equal(provider.Id, nook.Provider);
        Assert.Equal(7, exited.ExitCode);
        Assert.Equal(MachineStatus.Online, after.Status);
    }

    [Fact]
    public async Task A_registration_code_works_once()
    {
        WorkspaceSummary workspace = await CreateWorkspaceAsync();
        MachineRegistration added = await AddMachineAsync(workspace, "mac-mini");

        await MachineLink.RegisterAsync(controlPlane.MachinesUrl, " " + added.Code + "\n", Ct);
        RpcException again = await Assert.ThrowsAsync<RpcException>(() => MachineLink.RegisterAsync(controlPlane.MachinesUrl, added.Code, Ct));
        MachineSummary after = await GetMachineAsync(added.Machine);

        Assert.Equal(MachineStatus.AwaitingRegistration, added.Machine.Status);
        Assert.Equal(StatusCode.Unauthenticated, again.StatusCode);
        Assert.Equal(MachineStatus.Offline, after.Status);
    }

    [Fact]
    public async Task Removing_a_machine_stops_its_machine_mode()
    {
        WorkspaceSummary workspace = await CreateWorkspaceAsync();
        MachineRegistration added = await AddMachineAsync(workspace, "vps");
        MachineCredential credential = await MachineLink.RegisterAsync(controlPlane.MachinesUrl, added.Code, Ct);
        await using RunningMachine machine = controlPlane.StartMachine(credential);
        await Api.EventuallyAsync(async () =>
        {
            MachineSummary current = await GetMachineAsync(added.Machine);
            return current.Status == MachineStatus.Online ? "online" : null;
        });

        await Api.ExpectAsync(_alice.DeleteAsync(new Uri(PathOf(added.Machine), UriKind.Relative), Ct), HttpStatusCode.NoContent);

        await machine.Running.WaitAsync(TimeSpan.FromSeconds(60), Ct);
        await Api.ProblemAsync(_alice.SendGetAsync(PathOf(added.Machine)), HttpStatusCode.NotFound);
    }

    [Fact]
    public async Task Only_members_see_a_workspace_machines()
    {
        WorkspaceSummary workspace = await CreateWorkspaceAsync();
        MachineRegistration added = await AddMachineAsync(workspace, "vps");
        using HttpClient bob = controlPlane.ClientFor("bob-" + Guid.CreateVersion7());

        Problem get = await Api.ProblemAsync(bob.SendGetAsync(PathOf(added.Machine)), HttpStatusCode.NotFound);
        Problem list = await Api.ProblemAsync(bob.SendGetAsync(WorkspaceMachinesPath(workspace)), HttpStatusCode.NotFound);
        Problem add = await Api.ProblemAsync(bob.SendPostAsync(WorkspaceMachinesPath(workspace), new { name = "mine" }), HttpStatusCode.NotFound);
        Problem remove = await Api.ProblemAsync(bob.DeleteAsync(new Uri(PathOf(added.Machine), UriKind.Relative), Ct), HttpStatusCode.NotFound);

        Assert.Equal(MachinesErrors.NotFound.Code, get.Code);
        Assert.Equal(WorkspacesErrors.NotFound.Code, list.Code);
        Assert.Equal(WorkspacesErrors.NotFound.Code, add.Code);
        Assert.Equal(MachinesErrors.NotFound.Code, remove.Code);
    }

    [Fact]
    public async Task Nooks_run_only_on_their_own_workspace_machines()
    {
        WorkspaceSummary acme = await CreateWorkspaceAsync();
        WorkspaceSummary other = await CreateWorkspaceAsync();
        MachineRegistration added = await AddMachineAsync(acme, "vps");
        string provider = "machine:" + added.Machine.Id.Value.ToString("D", CultureInfo.InvariantCulture);

        Problem problem = await Api.ProblemAsync(_alice.SendPostAsync(WorkspaceNooksPath(other), new { provider }), HttpStatusCode.BadRequest);

        IReadOnlyList<ProviderSummary> othersProviders = await ProvidersAsync(other);
        IReadOnlyList<ProviderSummary> acmeProviders = await ProvidersAsync(acme);

        Assert.True(problem.Errors?.ContainsKey("provider"));
        Assert.DoesNotContain(othersProviders, found => string.Equals(found.Id, provider, StringComparison.Ordinal));
        Assert.Contains(acmeProviders, found => string.Equals(found.Id, provider, StringComparison.Ordinal) && !found.Available);
    }

    public void Dispose()
    {
        _alice.Dispose();
    }

    private static string PathOf(MachineSummary machine)
    {
        return string.Create(CultureInfo.InvariantCulture, $"/machines/{machine.Id.Value}");
    }

    private static string PathOf(NookSummary nook)
    {
        return string.Create(CultureInfo.InvariantCulture, $"/nooks/{nook.Id.Value}");
    }

    private static string WorkspaceMachinesPath(WorkspaceSummary workspace)
    {
        return string.Create(CultureInfo.InvariantCulture, $"/workspaces/{workspace.Id.Value}/machines");
    }

    private static string WorkspaceNooksPath(WorkspaceSummary workspace)
    {
        return string.Create(CultureInfo.InvariantCulture, $"/workspaces/{workspace.Id.Value}/nooks");
    }

    private async Task<WorkspaceSummary> CreateWorkspaceAsync()
    {
        return await Api.ReadAsync<WorkspaceSummary>(_alice.SendPostAsync("/workspaces", new { name = "Acme" }), HttpStatusCode.Created);
    }

    private async Task<MachineRegistration> AddMachineAsync(WorkspaceSummary workspace, string name)
    {
        return await Api.ReadAsync<MachineRegistration>(_alice.SendPostAsync(WorkspaceMachinesPath(workspace), new { name }), HttpStatusCode.Created);
    }

    private async Task<MachineSummary> GetMachineAsync(MachineSummary machine)
    {
        return await Api.ReadAsync<MachineSummary>(_alice.SendGetAsync(PathOf(machine)), HttpStatusCode.OK);
    }

    private async Task<IReadOnlyList<ProviderSummary>> ProvidersAsync(WorkspaceSummary workspace)
    {
        string path = string.Create(CultureInfo.InvariantCulture, $"/workspaces/{workspace.Id.Value}/providers");
        return await Api.ReadAsync<IReadOnlyList<ProviderSummary>>(_alice.SendGetAsync(path), HttpStatusCode.OK);
    }

    private async Task<Page<ProcessSummary>> ProcessesAsync(NookSummary nook)
    {
        return await Api.ReadAsync<Page<ProcessSummary>>(_alice.SendGetAsync(PathOf(nook) + "/processes"), HttpStatusCode.OK);
    }
}
