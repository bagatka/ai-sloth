using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Bagatka.AiSloth.Chats.Data;
using Bagatka.AiSloth.Chats.Model;
using Bagatka.AiSloth.Nooks.Contracts;
using Bagatka.AiSloth.Workspaces.Contracts;
using Bagatka.Foundation;
using Bagatka.Foundation.Modules;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

namespace Bagatka.AiSloth.Chats;

// Drafts (README, "Drafts"): chats nobody wrote in yet. Apps start a chat as someone starts writing
// its first message, so its nook and agent are ready when it is sent. A draft nobody writes in goes
// with its nook after the draft lifetime, and a person keeps at most two in a workspace.
internal sealed class Drafts(
    IDbContextFactory<ChatsDbContext> databases,
    IServiceScopeFactory scopes,
    ChatsSettings settings,
    ActiveInstance active,
    TimeProvider time,
    ILogger<Drafts> logger) : BackgroundService
{
    public const int MaxPerPerson = 2;
    private const int BatchSize = 20;
    private static readonly TimeSpan Interval = TimeSpan.FromSeconds(10);

    // Before a person starts a chat: their oldest drafts in the workspace go, so that with the new one
    // they keep at most two.
    public async Task MakeRoomAsync(UserId person, WorkspaceId workspace, CancellationToken ct)
    {
        List<Draft> older;
        await using (ChatsDbContext db = await databases.CreateDbContextAsync(ct))
        {
            older = await db.Drafts.AsNoTracking()
                .Where(draft => draft.StartedBy == person && draft.WorkspaceId == workspace)
                .OrderByDescending(draft => draft.StartedAt)
                .Skip(MaxPerPerson - 1)
                .ToListAsync(ct);
        }

        foreach (Draft draft in older)
        {
            await DropAsync(draft, ct);
        }
    }

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
    private async Task DropAsync(Draft draft, CancellationToken ct)
    {
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

        await using AsyncServiceScope scope = scopes.CreateAsyncScope();
        Result deleted = await scope.ServiceProvider.GetRequiredService<INooksApi>().DeleteAsync(SystemActors.Harness, draft.NookId, ct);
        if (deleted.Failed)
        {
            Log.DraftNookKept(logger, draft.NookId.Value, deleted.Error.Message);
        }

        Log.DraftDropped(logger, draft.ChatId.Value);
    }
}
