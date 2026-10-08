using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

namespace Bagatka.Foundation.Modules.Events;

// Delivers the events in one module's outbox to the reactions registered for them, after they commit
// (PATTERNS.md, entry 15): oldest first, every second, at least once. A message is deleted once all its reactions succeed; one whose reactions fail is delivered
// again with growing waits, its successful reactions too, and after MaxAttempts it is parked and logged
// for an operator. Messages wait only for their own retries, never for others'. Only the active
// instance delivers.
internal sealed class OutboxDispatcher<TDbContext>(
    IDbContextFactory<TDbContext> databases,
    IServiceProvider services,
    IEnumerable<Reaction> reactions,
    ActiveInstance active,
    TimeProvider time,
    ILogger<OutboxDispatcher<TDbContext>> logger) : BackgroundService
    where TDbContext : DbContext
{
    private const int BatchSize = 20;
    private const int MaxAttempts = 10;
    private static readonly TimeSpan Interval = TimeSpan.FromSeconds(1);
    private static readonly TimeSpan MaxRetryDelay = TimeSpan.FromMinutes(10);

    private readonly ILookup<string, Reaction> _reactions = reactions.ToLookup(reaction => reaction.EventType, StringComparer.Ordinal);

    protected override Task ExecuteAsync(CancellationToken stoppingToken)
    {
        return active.RunAsync(WorkAsync, stoppingToken);
    }

    private async Task WorkAsync(CancellationToken stoppingToken)
    {
        using PeriodicTimer timer = new PeriodicTimer(Interval, time);
        do
        {
            await PassAsync(stoppingToken);
        }
        while (await timer.WaitForNextTickAsync(stoppingToken));
    }

    // One failure never stops the job: a failed pass is logged and the next one tries again.
    private async Task PassAsync(CancellationToken ct)
    {
        try
        {
            await using TDbContext db = await databases.CreateDbContextAsync(ct);
            DateTimeOffset now = time.GetUtcNow();
            List<OutboxMessage> due = await db.Set<OutboxMessage>()
                .Where(message => message.ParkedAt == null && (message.RetryAt == null || message.RetryAt <= now))
                .OrderBy(message => message.Id)
                .Take(BatchSize)
                .ToListAsync(ct);
            foreach (OutboxMessage message in due)
            {
                bool delivered = await DeliverAsync(message, ct);
                if (delivered)
                {
                    db.Set<OutboxMessage>().Remove(message);
                }
                else
                {
                    message.Failed(MaxAttempts, RetryDelay, time.GetUtcNow());
                    if (message.ParkedAt is not null)
                    {
                        Log.EventParked(logger, message.Id, message.Type, message.Attempts);
                    }
                }

                await db.SaveChangesAsync(ct);
            }
        }
        catch (Exception exception) when (exception is not OperationCanceledException)
        {
            Log.DispatchFailed(logger, exception, typeof(TDbContext).Name);
        }
    }

    // Whether every reaction to the message succeeded; an event nobody reacts to is delivered at once.
    private async Task<bool> DeliverAsync(OutboxMessage message, CancellationToken ct)
    {
        bool delivered = true;
        foreach (Reaction reaction in _reactions[message.Type])
        {
            try
            {
                Result handled = await reaction.DeliverAsync(services, message.Payload, ct);
                if (handled.Failed)
                {
                    Log.ReactionFailed(logger, null, reaction.Name, message.Id, handled.Error.Message);
                    delivered = false;
                }
            }
            catch (Exception exception) when (exception is not OperationCanceledException)
            {
                Log.ReactionFailed(logger, exception, reaction.Name, message.Id, exception.Message);
                delivered = false;
            }
        }

        return delivered;
    }

    private static TimeSpan RetryDelay(int attempts)
    {
        double seconds = Math.Min(MaxRetryDelay.TotalSeconds, Math.Pow(2, attempts));
        return TimeSpan.FromSeconds(seconds);
    }
}
