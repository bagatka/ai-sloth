using System;
using System.Collections.Generic;
using System.Globalization;
using System.Threading;
using System.Threading.Tasks;
using Bagatka.Sdk.Docker;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace Bagatka.Sandboxing.ConformanceTests;

/// <summary>
/// Behavior of the Docker provider that the contract can't observe: what its images contain and
/// what it leaves behind.
/// </summary>
public sealed class DockerSandboxProviderTests
{
    private const string SnapshotLabel = "com.bagatka.sandboxing.snapshot";
    private const string KeyLabel = "com.bagatka.sandboxing.key";

    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    [Fact]
    public async Task A_snapshot_never_carries_the_sandboxs_environment_values()
    {
        await using ProviderUnderTest under = ProvidersUnderTest.Create("docker");
        DockerClient docker = under.Services.GetRequiredService<DockerClient>();
        SandboxSpec source = Spec(new Dictionary<string, string>(StringComparer.Ordinal) { ["SLOTHD_TOKEN"] = "secret-token" });
        await TestResults.ValueAsync(under.Provider.CreateAsync(source, Ct));
        SnapshotKey snapshot = SnapshotKey.From(Guid.CreateVersion7());

        await TestResults.ValueAsync(under.Provider.SnapshotAsync(source.Key, snapshot, Ct));

        ImageDetails image = await SnapshotImageAsync(docker, snapshot);
        Assert.Contains("SLOTHD_TOKEN=", image.Environment, StringComparer.Ordinal);
        Assert.DoesNotContain(image.Environment, variable => variable.Contains("secret-token", StringComparison.Ordinal));
    }

    [Fact]
    public async Task Deleting_the_last_sandbox_created_from_a_deleted_snapshot_removes_its_image()
    {
        await using ProviderUnderTest under = ProvidersUnderTest.Create("docker");
        DockerClient docker = under.Services.GetRequiredService<DockerClient>();
        SandboxSpec source = Spec(new Dictionary<string, string>(StringComparer.Ordinal));
        await TestResults.ValueAsync(under.Provider.CreateAsync(source, Ct));
        SnapshotKey snapshot = SnapshotKey.From(Guid.CreateVersion7());
        await TestResults.ValueAsync(under.Provider.SnapshotAsync(source.Key, snapshot, Ct));
        ImageDetails image = await SnapshotImageAsync(docker, snapshot);
        SandboxSpec fork = Spec(new Dictionary<string, string>(StringComparer.Ordinal)) with { Source = new SandboxSource(snapshot) };
        await TestResults.ValueAsync(under.Provider.CreateAsync(fork, Ct));

        await under.Provider.DeleteSnapshotAsync(snapshot, Ct);
        ImageDetails? whileUsed = await docker.InspectImageAsync(image.Id, Ct);
        await under.Provider.DeleteAsync(fork.Key, Ct);
        ImageDetails? afterUse = await docker.InspectImageAsync(image.Id, Ct);

        Assert.NotNull(whileUsed);
        Assert.Null(afterUse);
    }

    [Fact]
    public async Task A_sandbox_from_one_tag_removes_the_repositorys_other_tags_nothing_uses()
    {
        await using ProviderUnderTest under = ProvidersUnderTest.Create("docker");
        DockerClient docker = under.Services.GetRequiredService<DockerClient>();
        SandboxSpec source = Spec(new Dictionary<string, string>(StringComparer.Ordinal));
        await TestResults.ValueAsync(under.Provider.CreateAsync(source, Ct));
        string repository = "aisloth-test-" + Guid.CreateVersion7().ToString("N", CultureInfo.InvariantCulture);
        CommitConfiguration plain = new CommitConfiguration([], new Dictionary<string, string>(StringComparer.Ordinal));
        string container = await ContainerOfAsync(docker, source.Key);
        SandboxSpec running = Spec(new Dictionary<string, string>(StringComparer.Ordinal)) with { Source = new SandboxSource(new SandboxImage(repository + ":running")) };
        SandboxSpec newest = Spec(new Dictionary<string, string>(StringComparer.Ordinal)) with { Source = new SandboxSource(new SandboxImage(repository + ":new")) };
        try
        {
            await docker.CommitContainerAsync(container, repository, "old", plain, Ct);
            await docker.CommitContainerAsync(container, repository, "running", plain, Ct);
            await TestResults.ValueAsync(under.Provider.CreateAsync(running, Ct));

            await docker.CommitContainerAsync(container, repository, "new", plain, Ct);
            await TestResults.ValueAsync(under.Provider.CreateAsync(newest, Ct));

            ImageDetails? old = await docker.InspectImageAsync(repository + ":old", Ct);
            ImageDetails? stillUsed = await docker.InspectImageAsync(repository + ":running", Ct);
            ImageDetails? current = await docker.InspectImageAsync(repository + ":new", Ct);
            ImageDetails? other = await docker.InspectImageAsync("registry.k8s.io/pause:3.10", Ct);

            Assert.Null(old);
            Assert.NotNull(stillUsed);
            Assert.NotNull(current);
            Assert.NotNull(other);
        }
        finally
        {
            // The images this test made aren't in the provider's scope, so they go here, once nothing uses them.
            await under.Provider.DeleteAsync(running.Key, CancellationToken.None);
            await under.Provider.DeleteAsync(newest.Key, CancellationToken.None);
            foreach (string tag in new[] { "old", "running", "new" })
            {
                await docker.RemoveImageAsync(repository + ":" + tag, force: true, CancellationToken.None);
            }
        }
    }

    private static SandboxSpec Spec(IReadOnlyDictionary<string, string> environment)
    {
        return new SandboxSpec(SandboxKey.From(Guid.CreateVersion7()), ProvidersUnderTest.Image, ProvidersUnderTest.Resources, environment, Location: null);
    }

    private static async Task<string> ContainerOfAsync(DockerClient docker, SandboxKey key)
    {
        IReadOnlyList<ContainerListItem> containers = await docker.ListContainersAsync([KeyLabel + "=" + key.Value.ToString("N", CultureInfo.InvariantCulture)], Ct);
        return Assert.Single(containers).Id;
    }

    private static async Task<ImageDetails> SnapshotImageAsync(DockerClient docker, SnapshotKey snapshot)
    {
        string label = SnapshotLabel + "=" + snapshot.Value.ToString("N", CultureInfo.InvariantCulture);
        IReadOnlyList<ImageListItem> images = await docker.ListImagesAsync([label], Ct);
        ImageListItem listed = Assert.Single(images);
        ImageDetails? image = await docker.InspectImageAsync(listed.Id, Ct);
        Assert.NotNull(image);
        return image;
    }
}
