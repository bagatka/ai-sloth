using System;
using System.Threading.Tasks;
using Azure;
using Azure.Identity;
using Bagatka.Azure.Sandboxes.Models;
using Xunit;

namespace Bagatka.Azure.Sandboxes.Tests;

/// <summary>
/// The client against the real service, run only when BAGATKA_AZURE_SANDBOXES_GROUP names a sandbox
/// group as <c>subscription/resource-group/group/region</c>, signed in with <c>az login</c> as someone
/// with the Container Apps SandboxGroup Data Owner role. It deletes what it creates; a few cents.
/// </summary>
public sealed class LiveTests
{
    private static System.Threading.CancellationToken Ct => TestContext.Current.CancellationToken;

    [Fact]
    public async Task A_sandbox_runs_stops_with_its_memory_resumes_and_commits_on_the_real_service()
    {
        string? group = Environment.GetEnvironmentVariable("BAGATKA_AZURE_SANDBOXES_GROUP");
        Assert.SkipWhen(string.IsNullOrEmpty(group), "Set BAGATKA_AZURE_SANDBOXES_GROUP to run against the real service.");
        string[] parts = group!.Split('/');
        SandboxGroupClient client = new SandboxGroupClient(SandboxGroupClient.GetEndpoint(parts[3]), new SandboxGroupId(parts[0], parts[1], parts[2]), new AzureCliCredential());
        string run = Guid.CreateVersion7().ToString("N", System.Globalization.CultureInfo.InvariantCulture);
        DiskImage? image = null;
        DiskImage? committed = null;
        Sandbox? sandbox = null;
        try
        {
            DiskImageCreateOptions imageOptions = new DiskImageCreateOptions("registry.k8s.io/pause:3.10");
            imageOptions.Labels["run"] = run;
            Operation<DiskImage> imaging = await client.CreateDiskImageAsync(WaitUntil.Completed, imageOptions, Ct);
            image = imaging.Value;
            SandboxCreateOptions options = new SandboxCreateOptions(SandboxSource.FromDiskImage(image.Id), new SandboxResources("250m", "512Mi"))
            {
                AutoSuspend = SandboxAutoSuspend.AfterIdle(TimeSpan.FromMinutes(5), SandboxSuspendMode.Memory),
            };
            options.Labels["run"] = run;
            options.Entrypoint.Add("/pause");
            Operation<Sandbox> creating = await client.CreateSandboxAsync(WaitUntil.Completed, options, Ct);
            sandbox = creating.Value;

            Operation<Sandbox> stopping = await client.StopSandboxAsync(WaitUntil.Completed, sandbox.Id, Ct);
            Sandbox stopped = stopping.Value;
            Operation<Sandbox> resuming = await client.ResumeSandboxAsync(WaitUntil.Completed, sandbox.Id, Ct);
            Sandbox resumed = resuming.Value;
            Operation<DiskImage> committing = await client.CommitSandboxAsync(WaitUntil.Completed, sandbox.Id, labels: null, Ct);
            committed = committing.Value;
            Assert.Equal(SandboxState.Running, sandbox.State);
            Assert.Equal(SandboxState.Stopped, stopped.State);
            Assert.NotNull(stopped.SnapshotId);
            Assert.Equal(SandboxState.Running, resumed.State);
            Assert.Equal(DiskImageState.Ready, committed.State);
        }
        finally
        {
            if (sandbox is not null)
            {
                await client.DeleteSandboxAsync(sandbox.Id, Ct);
            }

            if (committed is not null)
            {
                await client.DeleteDiskImageAsync(committed.Id, Ct);
            }

            if (image is not null)
            {
                await client.DeleteDiskImageAsync(image.Id, Ct);
            }
        }

        NullableResponse<Sandbox> gone = await client.GetSandboxIfExistsAsync(sandbox.Id, Ct);
        Assert.False(gone.HasValue);
    }
}
