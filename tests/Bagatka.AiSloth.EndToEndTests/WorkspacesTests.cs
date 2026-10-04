using System;
using System.Globalization;
using System.Linq;
using System.Net;
using System.Net.Http;
using System.Threading.Tasks;
using Bagatka.AiSloth.Workspaces.Contracts;
using Bagatka.Foundation;
using Xunit;

namespace Bagatka.AiSloth.EndToEndTests;

public sealed class WorkspacesTests(ControlPlane controlPlane)
{
    [Fact]
    public async Task Creating_a_workspace_makes_the_creator_its_owner()
    {
        using HttpClient alice = controlPlane.ClientFor("alice-" + Guid.CreateVersion7());

        HttpResponseMessage response = await alice.SendPostAsync("/workspaces", new { name = "  Acme  " });
        WorkspaceSummary created = await Api.ReadAsync<WorkspaceSummary>(response, HttpStatusCode.Created);
        WorkspaceSummary fetched = await Api.ReadAsync<WorkspaceSummary>(alice.SendGetAsync(PathOf(created)), HttpStatusCode.OK);

        Assert.Equal("Acme", created.Name);
        Assert.Equal(WorkspaceRole.Owner, created.Role);
        Assert.Equal(PathOf(created), response.Headers.Location?.OriginalString);
        Assert.Equal(created, fetched);
    }

    [Fact]
    public async Task A_workspace_needs_a_name()
    {
        using HttpClient alice = controlPlane.ClientFor("alice-" + Guid.CreateVersion7());

        Problem problem = await Api.ProblemAsync(alice.SendPostAsync("/workspaces", new { name = "   " }), HttpStatusCode.BadRequest);

        Assert.True(problem.Errors?.ContainsKey("name"));
    }

    [Fact]
    public async Task Non_members_cannot_learn_that_a_workspace_exists()
    {
        using HttpClient alice = controlPlane.ClientFor("alice-" + Guid.CreateVersion7());
        using HttpClient bob = controlPlane.ClientFor("bob-" + Guid.CreateVersion7());
        WorkspaceSummary created = await Api.ReadAsync<WorkspaceSummary>(alice.SendPostAsync("/workspaces", new { name = "Acme" }), HttpStatusCode.Created);

        Problem problem = await Api.ProblemAsync(bob.SendGetAsync(PathOf(created)), HttpStatusCode.NotFound);
        Page<WorkspaceSummary> bobs = await Api.ReadAsync<Page<WorkspaceSummary>>(bob.SendGetAsync("/workspaces"), HttpStatusCode.OK);

        Assert.Equal(WorkspacesErrors.NotFound.Code, problem.Code);
        Assert.Empty(bobs.Items);
    }

    [Fact]
    public async Task A_users_workspaces_are_listed_oldest_first_page_by_page()
    {
        using HttpClient alice = controlPlane.ClientFor("alice-" + Guid.CreateVersion7());
        string[] names = ["First", "Second", "Third"];
        foreach (string name in names)
        {
            await Api.ExpectAsync(alice.SendPostAsync("/workspaces", new { name }), HttpStatusCode.Created);
        }

        Page<WorkspaceSummary> first = await Api.ReadAsync<Page<WorkspaceSummary>>(alice.SendGetAsync("/workspaces?limit=2"), HttpStatusCode.OK);
        Page<WorkspaceSummary> second = await Api.ReadAsync<Page<WorkspaceSummary>>(alice.SendGetAsync("/workspaces?limit=2&cursor=" + first.NextCursor), HttpStatusCode.OK);

        Assert.Equal(names, first.Items.Concat(second.Items).Select(workspace => workspace.Name), StringComparer.Ordinal);
        Assert.Null(second.NextCursor);
    }

    [Fact]
    public async Task A_cursor_must_come_from_a_previous_page()
    {
        using HttpClient alice = controlPlane.ClientFor("alice-" + Guid.CreateVersion7());

        Problem problem = await Api.ProblemAsync(alice.SendGetAsync("/workspaces?cursor=not-a-cursor"), HttpStatusCode.BadRequest);

        Assert.True(problem.Errors?.ContainsKey("cursor"));
    }

    private static string PathOf(WorkspaceSummary workspace)
    {
        return string.Create(CultureInfo.InvariantCulture, $"/workspaces/{workspace.Id.Value}");
    }
}
