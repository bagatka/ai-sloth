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

    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    [Fact]
    public async Task A_snapshot_never_carries_the_sandboxs_environment_values()
    {
        await using ProviderUnderTest under = ProvidersUnderTest.Create("docker");
        DockerClient docker = under.Services.GetRequiredService<DockerClient>();
        SandboxSpec source = Spec(new Dictionary<string, string>(StringComparer.Ordinal) { ["SLOTHD_TOKEN"] = "secret-token" });
        TestResults.Value(await under.Provider.CreateAsync(source, Ct));
        SnapshotKey snapshot = SnapshotKey.From(Guid.CreateVersion7());

        TestResults.Value(await under.Provider.SnapshotAsync(source.Key, snapshot, Ct));

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
        TestResults.Value(await under.Provider.CreateAsync(source, Ct));
        SnapshotKey snapshot = SnapshotKey.From(Guid.CreateVersion7());
        TestResults.Value(await under.Provider.SnapshotAsync(source.Key, snapshot, Ct));
        ImageDetails image = await SnapshotImageAsync(docker, snapshot);
        SandboxSpec fork = Spec(new Dictionary<string, string>(StringComparer.Ordinal)) with { Source = new SandboxSource(snapshot) };
        TestResults.Value(await under.Provider.CreateAsync(fork, Ct));

        await under.Provider.DeleteSnapshotAsync(snapshot, Ct);
        ImageDetails? whileUsed = await docker.InspectImageAsync(image.Id, Ct);
        await under.Provider.DeleteAsync(fork.Key, Ct);

        Assert.NotNull(whileUsed);
        Assert.Null(await docker.InspectImageAsync(image.Id, Ct));
    }

    private static SandboxSpec Spec(IReadOnlyDictionary<string, string> environment)
    {
        return new SandboxSpec(SandboxKey.From(Guid.CreateVersion7()), ProvidersUnderTest.Image, ProvidersUnderTest.Resources, environment);
    }

    private static async Task<ImageDetails> SnapshotImageAsync(DockerClient docker, SnapshotKey snapshot)
    {
        string label = SnapshotLabel + "=" + snapshot.Value.ToString("N", CultureInfo.InvariantCulture);
        ImageListItem listed = Assert.Single(await docker.ListImagesAsync([label], Ct));
        ImageDetails? image = await docker.InspectImageAsync(listed.Id, Ct);
        Assert.NotNull(image);
        return image;
    }
}
