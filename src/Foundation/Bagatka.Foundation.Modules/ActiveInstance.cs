using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Bagatka.Foundation.Modules.Data;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

namespace Bagatka.Foundation.Modules;

/// <summary>
/// The control-plane instance that does background work: the one holding the lease, a row in the
/// database every instance shares (PATTERNS.md, entry 23). Every instance serves requests; only the
/// active one runs modules' background work, through <see cref="RunAsync"/>.
/// </summary>
/// <remarks>
/// A deploy starts the new instance beside the old one. The new one waits; the old one, once told to
/// stop, hands over: its background work stops, the lease goes free, and the new one takes it within
/// a second. Then <see cref="Leaving"/> ends the old one's long-lived connections, so daemons, machines,
/// and watching clients reconnect to the new one, while its requests in flight finish. A holder that
/// died without handing over loses the lease after <see cref="LeaseFor"/>; one that can't renew its
/// lease in that time stops itself, so two instances never work at once.
/// </remarks>
public sealed partial class ActiveInstance : BackgroundService
{
    /// <summary>How long a lease lasts without being renewed.</summary>
    public static readonly TimeSpan LeaseFor = TimeSpan.FromSeconds(15);

    // The holder renews well before the lease lapses; a waiting instance asks often, to take over quickly.
    private static readonly TimeSpan RenewEvery = TimeSpan.FromSeconds(5);
    private static readonly TimeSpan AskEvery = TimeSpan.FromSeconds(1);

    private readonly IDbContextFactory<InstanceLeaseDbContext> _databases;
    private readonly IHostApplicationLifetime _lifetime;
    private readonly TimeProvider _time;
    private readonly ILogger<ActiveInstance> _logger;
    private readonly Guid _instance = Guid.CreateVersion7();
    private readonly Lock _gate = new Lock();
    private readonly TaskCompletionSource _active = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
    private readonly CancellationTokenSource _handingOver = new CancellationTokenSource();
    private readonly CancellationTokenSource _leaving = new CancellationTokenSource();
    private readonly List<Task> _working = [];
    private bool _handedOver;
    private Task _handover = Task.CompletedTask;
    private CancellationTokenRegistration _stopping;

    internal ActiveInstance(IDbContextFactory<InstanceLeaseDbContext> databases, IHostApplicationLifetime lifetime, TimeProvider time, ILogger<ActiveInstance> logger)
    {
        _databases = databases;
        _lifetime = lifetime;
        _time = time;
        _logger = logger;
    }

    /// <summary>
    /// Cancelled once this instance handed its background work over and is going away: long-lived
    /// connections end then, so their other ends reconnect to the instance that is active now.
    /// </summary>
    public CancellationToken Leaving => _leaving.Token;

    /// <summary>
    /// Runs <paramref name="work"/> once this instance is active, until it hands over, which cancels the
    /// work's token and waits for the work to return. Returns at once when the instance stops before it
    /// was ever active. Background services call it from <c>ExecuteAsync</c>.
    /// </summary>
    public async Task RunAsync(Func<CancellationToken, Task> work, CancellationToken stoppingToken)
    {
        using CancellationTokenSource running = CancellationTokenSource.CreateLinkedTokenSource(stoppingToken, _handingOver.Token);
        try
        {
            await _active.Task.WaitAsync(running.Token);
        }
        catch (OperationCanceledException) when (running.IsCancellationRequested)
        {
            return;
        }

        // Counted before it starts, so a handover that begins meanwhile waits for it.
        TaskCompletionSource done = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        lock (_gate)
        {
            if (_handedOver)
            {
                return;
            }

            _working.Add(done.Task);
        }

        try
        {
            await work(running.Token);
        }
        catch (OperationCanceledException) when (running.IsCancellationRequested)
        {
            // Handed over, or stopped.
        }
        finally
        {
            done.SetResult();
        }
    }

    /// <inheritdoc/>
    public override async Task StartAsync(CancellationToken cancellationToken)
    {
        _stopping = _lifetime.ApplicationStopping.Register(() => _handover = HandOverAsync());
        await base.StartAsync(cancellationToken);
    }

    /// <inheritdoc/>
    public override async Task StopAsync(CancellationToken cancellationToken)
    {
        await base.StopAsync(cancellationToken);
        await _handover.WaitAsync(cancellationToken);
    }

    /// <inheritdoc/>
    public override void Dispose()
    {
        _stopping.Dispose();
        _handingOver.Dispose();
        _leaving.Dispose();
        base.Dispose();
    }

    /// <inheritdoc/>
    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        using CancellationTokenSource asking = CancellationTokenSource.CreateLinkedTokenSource(stoppingToken, _handingOver.Token);
        DateTimeOffset heldUntil = DateTimeOffset.MinValue;
        while (!asking.IsCancellationRequested)
        {
            DateTimeOffset until = _time.GetUtcNow() + LeaseFor;
            bool held = await TryHoldAsync(until, asking.Token);
            DateTimeOffset now = _time.GetUtcNow();
            if (held)
            {
                heldUntil = until;
                bool activated = _active.TrySetResult();
                if (activated)
                {
                    Log.Activated(_logger, _instance);
                }
            }
            else if (_active.Task.IsCompleted && now >= heldUntil)
            {
                // Another instance may hold it now: this one's work must stop before it starts its own.
                Log.LeaseLost(_logger, _instance);
                _lifetime.StopApplication();
                return;
            }

            await DelayAsync(_active.Task.IsCompleted ? RenewEvery : AskEvery, asking.Token);
        }
    }

    // Takes the lease, or renews it, when it's free, lapsed, or already this instance's. One statement,
    // so two instances can't both take it. False also when the database can't be reached: the caller
    // decides whether the lease is lost.
    private async Task<bool> TryHoldAsync(DateTimeOffset until, CancellationToken ct)
    {
        try
        {
            DateTimeOffset now = until - LeaseFor;
            await using InstanceLeaseDbContext db = await _databases.CreateDbContextAsync(ct);

            // A compare-and-set on the lease row, which has no rules beyond this one (PATTERNS.md, entry 13).
            int taken = await db.Leases
                .Where(lease => lease.Id == InstanceLease.Only && (lease.Holder == _instance || lease.Holder == null || lease.ExpiresAt < now))
                .ExecuteUpdateAsync(lease => lease.SetProperty(row => row.Holder, _instance).SetProperty(row => row.ExpiresAt, until), ct);
            return taken == 1;
        }
        catch (Exception exception) when (exception is not OperationCanceledException)
        {
            Log.LeaseUnreachable(_logger, exception);
            return false;
        }
    }

    // Stops the background work, frees the lease for the next instance, then lets long-lived
    // connections go. Started when the host begins stopping, before it drains requests in flight.
    private async Task HandOverAsync()
    {
        Task[] working;
        lock (_gate)
        {
            _handedOver = true;
            working = [.. _working];
        }

        await _handingOver.CancelAsync();
        await Task.WhenAll(working);
        if (_active.Task.IsCompleted)
        {
            await ReleaseAsync();
        }

        await _leaving.CancelAsync();
    }

    // Not handled: a database out of reach while stopping leaves the lease to lapse, so the next
    // instance starts its work up to LeaseFor later.
    private async Task ReleaseAsync()
    {
        try
        {
            await using InstanceLeaseDbContext db = await _databases.CreateDbContextAsync(CancellationToken.None);
            await db.Leases
                .Where(lease => lease.Id == InstanceLease.Only && lease.Holder == _instance)
                .ExecuteUpdateAsync(lease => lease.SetProperty(row => row.Holder, (Guid?)null), CancellationToken.None);
            Log.HandedOver(_logger, _instance);
        }
        catch (Exception exception) when (exception is not OperationCanceledException)
        {
            Log.LeaseUnreachable(_logger, exception);
        }
    }

    private async Task DelayAsync(TimeSpan delay, CancellationToken ct)
    {
        try
        {
            await Task.Delay(delay, _time, ct);
        }
        catch (OperationCanceledException) when (ct.IsCancellationRequested)
        {
            // Stopping: the loop ends.
        }
    }

    private static partial class Log
    {
        [LoggerMessage(Level = LogLevel.Information, Message = "Instance {Instance} is active: it runs the background work")]
        public static partial void Activated(ILogger logger, Guid instance);

        [LoggerMessage(Level = LogLevel.Information, Message = "Instance {Instance} handed its background work over")]
        public static partial void HandedOver(ILogger logger, Guid instance);

        [LoggerMessage(Level = LogLevel.Critical, Message = "Instance {Instance} lost its lease and stops, so two instances never work at once")]
        public static partial void LeaseLost(ILogger logger, Guid instance);

        [LoggerMessage(Level = LogLevel.Warning, Message = "The instance lease couldn't be read or written")]
        public static partial void LeaseUnreachable(ILogger logger, Exception exception);
    }
}
