using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Bagatka.Foundation;
using Xunit;

namespace Bagatka.Sandboxing.ConformanceTests;

/// <summary>
/// The guarantees of <see cref="ISandboxProvider"/>, which every provider must keep.
/// </summary>
public sealed class SandboxProviderConformanceTests
{
    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    [Theory]
    [MemberData(nameof(ProvidersUnderTest.Names), MemberType = typeof(ProvidersUnderTest))]
    public async Task Create_starts_a_sandbox_and_repeating_it_returns_the_same_sandbox(string provider)
    {
        await using ProviderUnderTest under = ProvidersUnderTest.Create(provider);
        SandboxSpec spec = Spec();

        SandboxObservation created = TestResults.Value(await under.Provider.CreateAsync(spec, Ct));
        SandboxObservation repeated = TestResults.Value(await under.Provider.CreateAsync(spec, Ct));
        SandboxObservation running = await WaitForAsync(under.Provider, spec.Key, SandboxState.Running);

        Assert.Equal(spec.Key, created.Key);
        Assert.Equal(spec.Key, repeated.Key);
        Assert.Equal(spec.Key, running.Key);
        Assert.Contains(await ListAsync(under.Provider), sandbox => sandbox.Key == spec.Key);
    }

    [Theory]
    [MemberData(nameof(ProvidersUnderTest.Names), MemberType = typeof(ProvidersUnderTest))]
    public async Task Create_with_a_used_key_and_a_different_spec_is_a_conflict(string provider)
    {
        await using ProviderUnderTest under = ProvidersUnderTest.Create(provider);
        SandboxSpec spec = Spec();
        TestResults.Value(await under.Provider.CreateAsync(spec, Ct));

        SandboxSpec different = spec with { Environment = Environment("GREETING", "goodbye") };
        Error error = TestResults.ErrorOf(await under.Provider.CreateAsync(different, Ct));

        Assert.Equal(ErrorKind.Conflict, error.Kind);
    }

    [Theory]
    [MemberData(nameof(ProvidersUnderTest.Names), MemberType = typeof(ProvidersUnderTest))]
    public async Task Create_rejects_a_location_the_backend_does_not_have(string provider)
    {
        await using ProviderUnderTest under = ProvidersUnderTest.Create(provider);
        SandboxSpec spec = Spec() with { Location = "nowhere" };

        Error error = TestResults.ErrorOf(await under.Provider.CreateAsync(spec, Ct));

        Assert.Equal(ErrorKind.Validation, error.Kind);
        Assert.Null(await under.Provider.ObserveAsync(spec.Key, Ct));
    }

    [Theory]
    [MemberData(nameof(ProvidersUnderTest.Names), MemberType = typeof(ProvidersUnderTest))]
    public async Task Create_rejects_resources_the_backend_cannot_run(string provider)
    {
        await using ProviderUnderTest under = ProvidersUnderTest.Create(provider);
        SandboxSpec spec = Spec() with { Resources = new SandboxResources(CpuMillicores: 0, MemoryMebibytes: 64) };

        Error error = TestResults.ErrorOf(await under.Provider.CreateAsync(spec, Ct));

        Assert.Equal(ErrorKind.Validation, error.Kind);
        Assert.Null(await under.Provider.ObserveAsync(spec.Key, Ct));
    }

    [Theory]
    [MemberData(nameof(ProvidersUnderTest.Names), MemberType = typeof(ProvidersUnderTest))]
    public async Task Suspend_releases_compute_and_resume_brings_the_sandbox_back(string provider)
    {
        await using ProviderUnderTest under = ProvidersUnderTest.Create(provider);
        SandboxSpec spec = Spec();
        TestResults.Value(await under.Provider.CreateAsync(spec, Ct));
        await WaitForAsync(under.Provider, spec.Key, SandboxState.Running);

        SandboxObservation suspended = TestResults.Value(await under.Provider.SuspendAsync(spec.Key, Ct));
        SandboxObservation suspendedAgain = TestResults.Value(await under.Provider.SuspendAsync(spec.Key, Ct));
        TestResults.Value(await under.Provider.ResumeAsync(spec.Key, Ct));
        SandboxObservation resumed = await WaitForAsync(under.Provider, spec.Key, SandboxState.Running);
        SandboxObservation resumedAgain = TestResults.Value(await under.Provider.ResumeAsync(spec.Key, Ct));

        Assert.Contains(suspended.State, new[] { SandboxState.Paused, SandboxState.Stopped });
        Assert.Equal(suspended.State, suspendedAgain.State);
        Assert.Equal(SandboxState.Running, resumed.State);
        Assert.Equal(SandboxState.Running, resumedAgain.State);
    }

    [Theory]
    [MemberData(nameof(ProvidersUnderTest.Names), MemberType = typeof(ProvidersUnderTest))]
    public async Task Operations_on_a_missing_sandbox_are_not_found(string provider)
    {
        await using ProviderUnderTest under = ProvidersUnderTest.Create(provider);
        SandboxKey missing = SandboxKey.From(Guid.CreateVersion7());
        SnapshotKey snapshot = SnapshotKey.From(Guid.CreateVersion7());

        Assert.Equal(ErrorKind.NotFound, TestResults.ErrorOf(await under.Provider.SuspendAsync(missing, Ct)).Kind);
        Assert.Equal(ErrorKind.NotFound, TestResults.ErrorOf(await under.Provider.ResumeAsync(missing, Ct)).Kind);
        Assert.Equal(ErrorKind.NotFound, TestResults.ErrorOf(await under.Provider.SnapshotAsync(missing, snapshot, Ct)).Kind);
        Assert.Null(await under.Provider.ObserveAsync(missing, Ct));
    }

    [Theory]
    [MemberData(nameof(ProvidersUnderTest.Names), MemberType = typeof(ProvidersUnderTest))]
    public async Task Delete_removes_the_sandbox_and_is_safe_to_repeat(string provider)
    {
        await using ProviderUnderTest under = ProvidersUnderTest.Create(provider);
        SandboxSpec spec = Spec();
        TestResults.Value(await under.Provider.CreateAsync(spec, Ct));

        await under.Provider.DeleteAsync(spec.Key, Ct);
        await under.Provider.DeleteAsync(spec.Key, Ct);

        Assert.Null(await under.Provider.ObserveAsync(spec.Key, Ct));
        Assert.DoesNotContain(await ListAsync(under.Provider), sandbox => sandbox.Key == spec.Key);
    }

    [Theory]
    [MemberData(nameof(ProvidersUnderTest.Names), MemberType = typeof(ProvidersUnderTest))]
    public async Task A_snapshot_starts_new_sandboxes_that_outlive_its_deletion(string provider)
    {
        await using ProviderUnderTest under = ProvidersUnderTest.Create(provider);
        SandboxSpec source = Spec();
        TestResults.Value(await under.Provider.CreateAsync(source, Ct));
        await WaitForAsync(under.Provider, source.Key, SandboxState.Running);
        SnapshotKey snapshot = SnapshotKey.From(Guid.CreateVersion7());

        SnapshotObservation taken = TestResults.Value(await under.Provider.SnapshotAsync(source.Key, snapshot, Ct));
        SnapshotObservation repeated = TestResults.Value(await under.Provider.SnapshotAsync(source.Key, snapshot, Ct));
        SandboxSpec fork = Spec(new SandboxSource(snapshot));
        TestResults.Value(await under.Provider.CreateAsync(fork, Ct));
        await WaitForAsync(under.Provider, fork.Key, SandboxState.Running);
        List<SnapshotObservation> beforeDeletion = await ListSnapshotsAsync(under.Provider);
        await under.Provider.DeleteSnapshotAsync(snapshot, Ct);
        await under.Provider.DeleteSnapshotAsync(snapshot, Ct);

        Assert.Equal(source.Key, taken.Source);
        Assert.Equal(taken.Key, repeated.Key);
        Assert.Contains(beforeDeletion, listed => listed.Key == snapshot);
        Assert.DoesNotContain(await ListSnapshotsAsync(under.Provider), listed => listed.Key == snapshot);
        Assert.Equal(SandboxState.Running, (await under.Provider.ObserveAsync(fork.Key, Ct))?.State);
    }

    [Theory]
    [MemberData(nameof(ProvidersUnderTest.Names), MemberType = typeof(ProvidersUnderTest))]
    public async Task A_snapshot_key_taken_from_another_sandbox_is_a_conflict(string provider)
    {
        await using ProviderUnderTest under = ProvidersUnderTest.Create(provider);
        SandboxSpec first = Spec();
        SandboxSpec second = Spec();
        TestResults.Value(await under.Provider.CreateAsync(first, Ct));
        TestResults.Value(await under.Provider.CreateAsync(second, Ct));
        SnapshotKey snapshot = SnapshotKey.From(Guid.CreateVersion7());
        TestResults.Value(await under.Provider.SnapshotAsync(first.Key, snapshot, Ct));

        Error error = TestResults.ErrorOf(await under.Provider.SnapshotAsync(second.Key, snapshot, Ct));

        Assert.Equal(ErrorKind.Conflict, error.Kind);
    }

    [Theory]
    [MemberData(nameof(ProvidersUnderTest.Names), MemberType = typeof(ProvidersUnderTest))]
    public async Task Creating_from_a_missing_snapshot_is_not_found(string provider)
    {
        await using ProviderUnderTest under = ProvidersUnderTest.Create(provider);
        SandboxSpec spec = Spec(new SandboxSource(SnapshotKey.From(Guid.CreateVersion7())));

        Error error = TestResults.ErrorOf(await under.Provider.CreateAsync(spec, Ct));

        Assert.Equal(ErrorKind.NotFound, error.Kind);
    }

    [Theory]
    [MemberData(nameof(ProvidersUnderTest.Names), MemberType = typeof(ProvidersUnderTest))]
    public async Task A_provider_sees_only_its_own_scope(string provider)
    {
        await using ProviderUnderTest mine = ProvidersUnderTest.Create(provider);
        await using ProviderUnderTest theirs = ProvidersUnderTest.Create(provider);
        SandboxSpec myAsk = Spec();
        SandboxSpec theirAsk = Spec();
        TestResults.Value(await mine.Provider.CreateAsync(myAsk, Ct));
        TestResults.Value(await theirs.Provider.CreateAsync(theirAsk, Ct));

        List<SandboxObservation> listed = await ListAsync(mine.Provider);

        Assert.Contains(listed, sandbox => sandbox.Key == myAsk.Key);
        Assert.DoesNotContain(listed, sandbox => sandbox.Key == theirAsk.Key);
        Assert.Null(await mine.Provider.ObserveAsync(theirAsk.Key, Ct));
    }

    private static SandboxSpec Spec(SandboxSource? source = null)
    {
        return new SandboxSpec(
            SandboxKey.From(Guid.CreateVersion7()),
            source ?? ProvidersUnderTest.Image,
            ProvidersUnderTest.Resources,
            Environment("GREETING", "hello"),
            Location: null);
    }

    private static Dictionary<string, string> Environment(string name, string value)
    {
        return new Dictionary<string, string>(StringComparer.Ordinal) { [name] = value };
    }

    private static async Task<SandboxObservation> WaitForAsync(ISandboxProvider provider, SandboxKey key, SandboxState state)
    {
        using CancellationTokenSource deadline = CancellationTokenSource.CreateLinkedTokenSource(Ct);
        deadline.CancelAfter(TimeSpan.FromMinutes(2));
        while (true)
        {
            SandboxObservation? observed = await provider.ObserveAsync(key, deadline.Token);
            if (observed?.State == state)
            {
                return observed;
            }

            Assert.NotEqual(SandboxState.Failed, observed?.State);
            await Task.Delay(TimeSpan.FromMilliseconds(200), deadline.Token);
        }
    }

    private static async Task<List<SandboxObservation>> ListAsync(ISandboxProvider provider)
    {
        return await provider.ListAsync(Ct).ToListAsync(Ct);
    }

    private static async Task<List<SnapshotObservation>> ListSnapshotsAsync(ISandboxProvider provider)
    {
        return await provider.ListSnapshotsAsync(Ct).ToListAsync(Ct);
    }
}
