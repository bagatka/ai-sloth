using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Net;
using System.Net.Http;
using System.Threading;
using System.Threading.Tasks;
using Bagatka.AiSloth.Nooks.Contracts;
using Bagatka.AiSloth.Secrets.Contracts;
using Bagatka.AiSloth.Workspaces.Contracts;
using Xunit;

namespace Bagatka.AiSloth.EndToEndTests;

/// <summary>
/// A workspace's secrets: environment variables every process in its nooks gets, agents included.
/// Processes are real, in real nooks.
/// </summary>
public sealed class SecretsTests(ControlPlane controlPlane) : IDisposable
{
    private readonly HttpClient _alice = controlPlane.ClientFor("alice-" + Guid.CreateVersion7());

    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    [Fact]
    public async Task A_workspaces_secret_is_in_every_process_of_its_nooks_and_its_value_is_never_shown()
    {
        WorkspaceSummary workspace = await CreateWorkspaceAsync();

        HttpResponseMessage set = await _alice.SendPutAsync(SecretPath(workspace, "GH_TOKEN"), new { value = "ghp_example value" });
        string setBody = await set.Content.ReadAsStringAsync(Ct);
        SecretSummary secret = await Api.ReadAsync<SecretSummary>(set, HttpStatusCode.OK);
        HttpResponseMessage listed = await _alice.SendGetAsync(SecretsPath(workspace));
        string listBody = await listed.Content.ReadAsStringAsync(Ct);
        IReadOnlyList<SecretSummary> secrets = await Api.ReadAsync<IReadOnlyList<SecretSummary>>(listed, HttpStatusCode.OK);
        NookSummary nook = await CreateNookAsync(workspace);
        int? matches = await NookProcesses.ExitCodeAsync(_alice, nook.Id, "sh", "-c", "test \"$GH_TOKEN\" = 'ghp_example value'");

        Assert.Equal("GH_TOKEN", secret.Name);
        Assert.Equal(["GH_TOKEN"], secrets.Select(found => found.Name), StringComparer.Ordinal);
        Assert.DoesNotContain("ghp_example", setBody, StringComparison.Ordinal);
        Assert.DoesNotContain("ghp_example", listBody, StringComparison.Ordinal);
        Assert.Equal(0, matches);
    }

    [Fact]
    public async Task Replacing_or_removing_a_secret_changes_what_new_processes_get()
    {
        WorkspaceSummary workspace = await CreateWorkspaceAsync();
        await Api.ExpectAsync(_alice.SendPutAsync(SecretPath(workspace, "DEPLOY_KEY"), new { value = "one" }), HttpStatusCode.OK);
        NookSummary nook = await CreateNookAsync(workspace);

        await Api.ExpectAsync(_alice.SendPutAsync(SecretPath(workspace, "DEPLOY_KEY"), new { value = "two" }), HttpStatusCode.OK);
        int? replaced = await NookProcesses.ExitCodeAsync(_alice, nook.Id, "sh", "-c", "test \"$DEPLOY_KEY\" = two");
        await Api.ExpectAsync(_alice.DeleteAsync(new Uri(SecretPath(workspace, "DEPLOY_KEY"), UriKind.Relative), Ct), HttpStatusCode.NoContent);
        int? removed = await NookProcesses.ExitCodeAsync(_alice, nook.Id, "sh", "-c", "test -z \"${DEPLOY_KEY+set}\"");
        Problem again = await Api.ProblemAsync(_alice.DeleteAsync(new Uri(SecretPath(workspace, "DEPLOY_KEY"), UriKind.Relative), Ct), HttpStatusCode.NotFound);
        IReadOnlyList<SecretSummary> secrets = await Api.ReadAsync<IReadOnlyList<SecretSummary>>(_alice.SendGetAsync(SecretsPath(workspace)), HttpStatusCode.OK);

        Assert.Equal(0, replaced);
        Assert.Equal(0, removed);
        Assert.Equal(SecretsErrors.NotFound.Code, again.Code);
        Assert.Empty(secrets);
    }

    [Fact]
    public async Task Only_managers_set_secrets_and_names_must_be_an_environments_own()
    {
        WorkspaceSummary workspace = await CreateWorkspaceAsync();
        await Api.ExpectAsync(_alice.SendPutAsync(SecretPath(workspace, "NPM_TOKEN"), new { value = "npm_x" }), HttpStatusCode.OK);
        using HttpClient bob = controlPlane.ClientFor("bob-" + Guid.CreateVersion7());
        using HttpClient carol = controlPlane.ClientFor("carol-" + Guid.CreateVersion7());
        string invites = string.Create(CultureInfo.InvariantCulture, $"/workspaces/{workspace.Id.Value}/invites");
        Invite invite = await Api.ReadAsync<Invite>(_alice.SendPostAsync(invites, new { access = "Write" }), HttpStatusCode.OK);
        await Api.ExpectAsync(bob.SendPostAsync("/invites/accept", new { code = invite.Code }), HttpStatusCode.OK);

        IReadOnlyList<SecretSummary> bobSees = await Api.ReadAsync<IReadOnlyList<SecretSummary>>(bob.SendGetAsync(SecretsPath(workspace)), HttpStatusCode.OK);
        await Api.ExpectAsync(bob.SendPutAsync(SecretPath(workspace, "NPM_TOKEN"), new { value = "mine" }), HttpStatusCode.Forbidden);
        await Api.ExpectAsync(carol.SendGetAsync(SecretsPath(workspace)), HttpStatusCode.NotFound);
        Problem digitFirst = await Api.ProblemAsync(_alice.SendPutAsync(SecretPath(workspace, "1TOKEN"), new { value = "x" }), HttpStatusCode.BadRequest);
        Problem systems = await Api.ProblemAsync(_alice.SendPutAsync(SecretPath(workspace, "PATH"), new { value = "/tmp" }), HttpStatusCode.BadRequest);
        Problem empty = await Api.ProblemAsync(_alice.SendPutAsync(SecretPath(workspace, "EMPTY"), new { value = string.Empty }), HttpStatusCode.BadRequest);

        Assert.Equal(["NPM_TOKEN"], bobSees.Select(found => found.Name), StringComparer.Ordinal);
        Assert.True(digitFirst.Errors?.ContainsKey("name"));
        Assert.True(systems.Errors?.ContainsKey("name"));
        Assert.True(empty.Errors?.ContainsKey("value"));
    }

    public void Dispose()
    {
        _alice.Dispose();
    }

    private static string SecretsPath(WorkspaceSummary workspace)
    {
        return string.Create(CultureInfo.InvariantCulture, $"/workspaces/{workspace.Id.Value}/secrets");
    }

    private static string SecretPath(WorkspaceSummary workspace, string name)
    {
        return SecretsPath(workspace) + "/" + name;
    }

    private async Task<WorkspaceSummary> CreateWorkspaceAsync()
    {
        return await Api.ReadAsync<WorkspaceSummary>(_alice.SendPostAsync("/workspaces", new { name = "Acme" }), HttpStatusCode.Created);
    }

    private async Task<NookSummary> CreateNookAsync(WorkspaceSummary workspace)
    {
        string nooks = string.Create(CultureInfo.InvariantCulture, $"/workspaces/{workspace.Id.Value}/nooks");
        return await Api.ReadAsync<NookSummary>(_alice.SendPostAsync(nooks, new { provider = "docker" }), HttpStatusCode.Created);
    }
}
