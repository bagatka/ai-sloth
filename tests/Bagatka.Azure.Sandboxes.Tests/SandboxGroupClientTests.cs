using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Net;
using System.Net.Http;
using System.Text.Json;
using System.Threading.Tasks;
using Azure;
using Bagatka.Azure.Sandboxes.Models;
using Xunit;

namespace Bagatka.Azure.Sandboxes.Tests;

/// <summary>
/// The client against responses recorded from the real service (2026-09-01-preview): what it sends,
/// what it makes of the answers, how it waits, and how it fails.
/// </summary>
public sealed class SandboxGroupClientTests
{
    private const string SandboxId = "3e355780-b2d2-414e-865e-d6586e0ddd06";

    private static System.Threading.CancellationToken Ct => TestContext.Current.CancellationToken;

    [Fact]
    public async Task Creating_a_sandbox_sends_its_source_resources_labels_environment_and_lifecycle()
    {
        using RecordedService service = new RecordedService();
        service.Then(HttpStatusCode.Created, "sandbox-create");
        SandboxCreateOptions options = new SandboxCreateOptions(SandboxSource.FromDiskImage("cea1bc8d-edcd-42f3-8e98-e77e680a7677"), new SandboxResources("250m", "512Mi", "32Gi"))
        {
            AutoSuspend = SandboxAutoSuspend.AfterIdle(TimeSpan.FromMinutes(30), SandboxSuspendMode.Memory),
        };
        options.Labels["probe"] = "aisloth";
        options.Environment["HELLO"] = "world";
        options.Entrypoint.Add("/pause");

        Operation<Sandbox> created = await service.Client().CreateSandboxAsync(WaitUntil.Completed, options, Ct);

        (HttpMethod method, Uri uri, string? body, string? authorization) = Assert.Single(service.Requests);
        Assert.Equal(HttpMethod.Post, method);
        Assert.Equal(RecordedService.Endpoint.Host, uri.Host);
        Assert.Equal(RecordedService.GroupPath + "/sandboxes", uri.AbsolutePath);
        Assert.Equal("?api-version=2026-09-01-preview", uri.Query);
        Assert.Equal("Bearer test-token", authorization);
        JsonElement sent = JsonElement.Parse(body!);
        Assert.Equal("cea1bc8d-edcd-42f3-8e98-e77e680a7677", sent.GetProperty("sourcesRef").GetProperty("diskImage").GetProperty("id").GetString());
        Assert.Equal("32Gi", sent.GetProperty("resources").GetProperty("disk").GetString());
        Assert.Equal("world", sent.GetProperty("environment").GetProperty("HELLO").GetString());
        Assert.Equal(1800, sent.GetProperty("lifecycle").GetProperty("autoSuspendPolicy").GetProperty("interval").GetInt32());
        Assert.Equal("Memory", sent.GetProperty("lifecycle").GetProperty("autoSuspendPolicy").GetProperty("mode").GetString());
        Assert.False(sent.GetProperty("lifecycle").GetProperty("autoDeletePolicy").GetProperty("enabled").GetBoolean());
        Assert.True(created.HasCompleted);
        Assert.Equal(SandboxId, created.Value.Id);
        Assert.Equal(SandboxState.Running, created.Value.State);
        Assert.Equal("one", created.Value.Labels["key"]);
        Assert.Equal("cea1bc8d-edcd-42f3-8e98-e77e680a7677", created.Value.DiskImageId);
        Assert.Equal("eastus2", created.Value.Region);
    }

    [Fact]
    public async Task Stopping_waits_until_the_sandbox_is_stopped()
    {
        string stopping = RecordedService.Read("sandbox-get").Replace("\"Running\"", "\"Stopping\"", StringComparison.Ordinal);
        using RecordedService service = new RecordedService();
        service
            .Then(HttpStatusCode.OK, "sandbox-stop")
            .Then(HttpStatusCode.OK, stopping)
            .Then(HttpStatusCode.OK, "sandbox-stopped");

        Operation<Sandbox> stopped = await service.Client().StopSandboxAsync(WaitUntil.Completed, SandboxId, Ct);

        Assert.Equal(RecordedService.GroupPath + "/sandboxes/" + SandboxId + "/stop", service.Requests[0].Uri.AbsolutePath);
        Assert.Equal(SandboxState.Stopped, stopped.Value.State);
        Assert.Equal("bb923a91-1630-44f3-83e6-6bb709f7665b", stopped.Value.SnapshotId);
        Assert.Equal(3, service.Requests.Count);
    }

    [Fact]
    public async Task Stopping_a_sandbox_that_isnt_running_fails_with_the_services_code()
    {
        using RecordedService service = new RecordedService();
        service.Then(HttpStatusCode.Conflict, "sandbox-not-running");

        RequestFailedException failed = await Assert.ThrowsAsync<RequestFailedException>(async () => await service.Client().StopSandboxAsync(WaitUntil.Completed, SandboxId, Ct));

        Assert.Equal(409, failed.Status);
        Assert.Equal("SandboxNotRunning", failed.ErrorCode);
    }

    [Fact]
    public async Task A_missing_sandbox_is_absent_when_asked_if_it_exists_and_a_failure_otherwise()
    {
        using RecordedService service = new RecordedService();
        service
            .Then(HttpStatusCode.NotFound, "sandbox-not-found")
            .Then(HttpStatusCode.NotFound, "sandbox-not-found");
        SandboxGroupClient client = service.Client();

        NullableResponse<Sandbox> maybe = await client.GetSandboxIfExistsAsync(SandboxId, Ct);
        RequestFailedException failed = await Assert.ThrowsAsync<RequestFailedException>(async () => await client.GetSandboxAsync(SandboxId, Ct));

        Assert.False(maybe.HasValue);
        Assert.Equal(404, maybe.GetRawResponse().Status);
        Assert.Equal("SandboxNotFound", failed.ErrorCode);
    }

    [Fact]
    public async Task Listing_selects_by_labels_and_follows_the_next_link()
    {
        string firstPage = RecordedService.Read("sandbox-list").Replace("\"value\"", "\"nextLink\": \"" + RecordedService.Endpoint + "next-page?api-version=2026-09-01-preview&skipToken=abc\", \"value\"", StringComparison.Ordinal);
        using RecordedService service = new RecordedService();
        service
            .Then(HttpStatusCode.OK, firstPage)
            .Then(HttpStatusCode.OK, "sandbox-list");
        Dictionary<string, string> labels = new Dictionary<string, string>(StringComparer.Ordinal) { ["probe"] = "aisloth", ["key"] = "one" };

        List<Sandbox> sandboxes = [];
        await foreach (Sandbox sandbox in service.Client().GetSandboxesAsync(labels, Ct))
        {
            sandboxes.Add(sandbox);
        }

        Assert.Equal("?api-version=2026-09-01-preview&labels=probe%3Daisloth%2Ckey%3Done", service.Requests[0].Uri.Query);
        Assert.Equal("/next-page", service.Requests[1].Uri.AbsolutePath);
        Assert.Equal([SandboxId, SandboxId], sandboxes.Select(sandbox => sandbox.Id), StringComparer.Ordinal);
    }

    [Fact]
    public async Task A_disk_image_from_a_container_image_is_waited_for_until_ready()
    {
        string pending = RecordedService.Read("diskimage-create").Replace("\"Ready\"", "\"Pending\"", StringComparison.Ordinal);
        using RecordedService service = new RecordedService();
        service
            .Then(HttpStatusCode.Created, pending)
            .Then(HttpStatusCode.OK, "diskimage-get");

        Operation<DiskImage> created = await service.Client().CreateDiskImageAsync(WaitUntil.Completed, new DiskImageCreateOptions("registry.k8s.io/pause:3.10") { Name = "probe-pause" }, Ct);

        JsonElement sent = JsonElement.Parse(service.Requests[0].Body!);
        Assert.Equal("registry", sent.GetProperty("source").GetProperty("kind").GetString());
        Assert.Equal("registry.k8s.io/pause:3.10", sent.GetProperty("source").GetProperty("imageUrl").GetString());
        bool signedIn = sent.GetProperty("source").TryGetProperty("authentication", out _);
        Assert.False(signedIn);
        Assert.Equal(DiskImageState.Ready, created.Value.State);
        Assert.Equal("registry.k8s.io/pause:3.10", created.Value.BaseImage);
        Assert.Equal(2, service.Requests.Count);
    }

    [Fact]
    public async Task A_disk_image_from_a_private_registry_signs_in_with_credentials_that_never_show()
    {
        using RecordedService service = new RecordedService();
        service.Then(HttpStatusCode.Created, "diskimage-create");
        RegistryCredentials credentials = new RegistryCredentials("00000000-0000-0000-0000-000000000000", "refresh-token");

        await service.Client().CreateDiskImageAsync(WaitUntil.Started, new DiskImageCreateOptions("example.azurecr.io/pause:3.10") { RegistryCredentials = credentials }, Ct);

        JsonElement sent = JsonElement.Parse(service.Requests[0].Body!).GetProperty("source").GetProperty("authentication").GetProperty("registryCredentials");
        Assert.Equal("00000000-0000-0000-0000-000000000000", sent.GetProperty("username").GetString());
        Assert.Equal("refresh-token", sent.GetProperty("token").GetString());
        Assert.DoesNotContain("refresh-token", credentials.ToString(), StringComparison.Ordinal);
    }

    [Fact]
    public async Task Committing_a_sandbox_makes_a_disk_image_with_its_labels()
    {
        using RecordedService service = new RecordedService();
        service.Then(HttpStatusCode.OK, "sandbox-commit");
        Dictionary<string, string> labels = new Dictionary<string, string>(StringComparer.Ordinal) { ["snapshot"] = "s1" };

        Operation<DiskImage> committed = await service.Client().CommitSandboxAsync(WaitUntil.Completed, SandboxId, labels, Ct);

        Assert.Equal("s1", JsonElement.Parse(service.Requests[0].Body!).GetProperty("labels").GetProperty("snapshot").GetString());
        Assert.Equal("fde3dcc5-6ec7-4185-bc8a-2907efa9a9c3", committed.Value.Id);
        Assert.Equal("s1", committed.Value.Labels["snapshot"]);
    }

    [Fact]
    public async Task Throttling_and_unavailability_are_retried()
    {
        using RecordedService service = new RecordedService();
        service
            .Then(HttpStatusCode.TooManyRequests)
            .Then(HttpStatusCode.ServiceUnavailable)
            .Then(HttpStatusCode.OK, "sandbox-get");

        Response<Sandbox> sandbox = await service.Client().GetSandboxAsync(SandboxId, Ct);

        Assert.Equal(SandboxState.Running, sandbox.Value.State);
        Assert.Equal(3, service.Requests.Count);
    }

    [Fact]
    public async Task Each_call_is_an_activity_that_names_its_failure()
    {
        List<Activity> stopped = [];
        using ActivityListener listener = new ActivityListener
        {
            ShouldListenTo = source => string.Equals(source.Name, SandboxesDiagnostics.Name, StringComparison.Ordinal),
            Sample = (ref ActivityCreationOptions<ActivityContext> _) => ActivitySamplingResult.AllDataAndRecorded,
            ActivityStopped = stopped.Add,
        };
        ActivitySource.AddActivityListener(listener);
        using RecordedService service = new RecordedService();
        service.Then(HttpStatusCode.NotFound, "sandbox-not-found");

        await Assert.ThrowsAsync<RequestFailedException>(async () => await service.Client().GetSandboxAsync(SandboxId, Ct));

        Activity activity = Assert.Single(stopped, activity => activity.OperationName is "SandboxGroupClient.GetSandbox");
        Assert.Equal(ActivityStatusCode.Error, activity.Status);
        Assert.Equal("SandboxNotFound", activity.GetTagItem("error.type"));
        Assert.Equal("Microsoft.App", activity.GetTagItem("az.namespace"));
    }
}
