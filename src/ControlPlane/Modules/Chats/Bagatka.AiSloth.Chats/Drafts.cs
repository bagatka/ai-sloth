using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Globalization;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Bagatka.AiSloth.Chats.Data;
using Bagatka.AiSloth.Chats.Model;
using Bagatka.AiSloth.Nooks.Contracts;
using Bagatka.Foundation;
using Bagatka.Foundation.Modules;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

namespace Bagatka.AiSloth.Chats;

// Drafts (README, "Drafts"): chats nobody wrote in yet. Apps start a chat as someone starts writing
// its first message, so its nook and agent are ready when it is sent. A draft nobody writes in goes
// with its nook after the draft lifetime.
internal sealed class Drafts(
    IDbContextFactory<ChatsDbContext> databases,
    INooksApi nooks,
    ChatsSettings settings,
    ActiveInstance active,
    TimeProvider time,
    ILogger<Drafts> logger) : BackgroundService
{
    private const int BatchSize = 20;
    private static readonly TimeSpan Interval = TimeSpan.FromSeconds(10);

    // A trace for each draft dropped; looking for them leaves none.
    private static readonly ActivitySource Traces = new ActivitySource("Bagatka.AiSloth.Chats");

    // Only the active instance runs it (PATTERNS.md, entry 23).
    protected override Task ExecuteAsync(CancellationToken stoppingToken)
    {
        return active.RunAsync(WorkAsync, stoppingToken);
    }

    // One failure never stops the job: a failed pass is logged and the next tries again.
    private async Task WorkAsync(CancellationToken stoppingToken)
    {
        using PeriodicTimer timer = new PeriodicTimer(Interval, time);
        while (await timer.WaitForNextTickAsync(stoppingToken))
        {
            try
            {
                List<Draft> expired;
                await using (ChatsDbContext db = await databases.CreateDbContextAsync(stoppingToken))
                {
                    DateTimeOffset startedBefore = time.GetUtcNow() - settings.DraftLifetime;
                    expired = await db.Drafts.AsNoTracking().Where(draft => draft.StartedAt < startedBefore).OrderBy(draft => draft.StartedAt).Take(BatchSize).ToListAsync(stoppingToken);
                }

                foreach (Draft draft in expired)
                {
                    await DropAsync(draft, stoppingToken);
                }
            }
            catch (Exception exception) when (exception is not OperationCanceledException)
            {
                Log.DraftsFailed(logger, exception);
            }
        }
    }

    // Deletes the draft, its chat, and its nook, unless someone wrote in it meanwhile: a first message
    // removes the draft in the same save, so only one of the two happens. The chat's runner retires
    // when it finds the chat gone.
    // Not handled: the host stopping between the commit and the nook's delete leaves the nook in
    // place; handling it would take a durable record of the nooks still to delete.
    private async Task DropAsync(Draft draft, CancellationToken ct)
    {
        using Activity? traced = Traces.StartActivity("drop draft");
        traced?.SetTag("chat.id", draft.ChatId.Value.ToString("D", CultureInfo.InvariantCulture));
        await using (ChatsDbContext db = await databases.CreateDbContextAsync(ct))
        {
            await using Microsoft.EntityFrameworkCore.Storage.IDbContextTransaction transaction = await db.Database.BeginTransactionAsync(ct);
            int dropped = await db.Drafts.Where(found => found.ChatId == draft.ChatId).ExecuteDeleteAsync(ct);
            if (dropped == 0)
            {
                return;
            }

            await db.Events.Where(stored => stored.ChatId == draft.ChatId).ExecuteDeleteAsync(ct);
            await db.Chats.Where(chat => chat.Id == draft.ChatId).ExecuteDeleteAsync(ct);
            await transaction.CommitAsync(ct);
        }

        Result deleted = await nooks.DeleteAsync(SystemActors.Harness, draft.NookId, ct);
        if (deleted.Failed)
        {
            Log.DraftNookKept(logger, draft.NookId.Value, deleted.Error.Message);
        }

        Log.DraftDropped(logger, draft.ChatId.Value);
    }
}
