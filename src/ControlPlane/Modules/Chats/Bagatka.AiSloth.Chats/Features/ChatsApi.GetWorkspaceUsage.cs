using Bagatka.AiSloth.Chats.Data;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Bagatka.AiSloth.Chats.Contracts;
using Bagatka.AiSloth.Chats.Model;
using Bagatka.AiSloth.Workspaces.Contracts;
using Bagatka.Foundation;
using Microsoft.EntityFrameworkCore;

namespace Bagatka.AiSloth.Chats;

internal sealed partial class ChatsApi
{
    private const int MaxUsageDays = 90;

    // A model call's day in a time zone.
    private sealed record CallDay(Guid Id, DateOnly Day);

    public async Task<Result<WorkspaceUsage>> GetWorkspaceUsageAsync(Actor actor, WorkspaceId workspaceId, int days, string timeZone, CancellationToken ct)
    {
        if (days is < 1 or > MaxUsageDays)
        {
            return new Result<WorkspaceUsage>(Error.Validation("days", "Must be 1 to 90."));
        }

        bool known = TimeZoneInfo.TryFindSystemTimeZoneById(timeZone, out TimeZoneInfo? zone);
        if (!known || zone is null)
        {
            return new Result<WorkspaceUsage>(Error.Validation("timeZone", "Must be a time zone, such as Europe/Berlin."));
        }

        await using ChatsDbContext db = await databases.CreateDbContextAsync(ct);

        // Whoever sees the workspace's chats sees what they used.
        AccessLevel? access = await workspaces.GetAccessAsync(actor, Resource.Workspace(workspaceId), ct);
        if (access is null)
        {
            return new Result<WorkspaceUsage>(WorkspacesErrors.NotFound);
        }

        // The days end today where the person is, and start at the first one's midnight there.
        DateOnly today = DateOnly.FromDateTime(TimeZoneInfo.ConvertTime(time.GetUtcNow(), zone).DateTime);
        DateTime firstMidnight = today.AddDays(1 - days).ToDateTime(TimeOnly.MinValue);
        DateTimeOffset from = new DateTimeOffset(firstMidnight, zone.GetUtcOffset(firstMidnight)).ToUniversalTime();
        IQueryable<ModelCall> calls = db.ModelCalls.AsNoTracking().Where(call => call.WorkspaceId == workspaceId && call.At >= from);

        // Each call's day where the person is, which PostgreSQL works out, so the calls are added up there
        // rather than read: EF translates no time zone conversion of a DateTimeOffset.
        IQueryable<CallDay> callDays = db.Database.SqlQuery<CallDay>(
            $"SELECT id, (at AT TIME ZONE {timeZone})::date AS day FROM chats.model_calls WHERE workspace_id = {workspaceId.Value} AND at >= {from}");
        List<Spent<DateOnly>> byDay = await calls
            .Join(callDays, call => call.Id, callDay => callDay.Id, (call, callDay) => new { Call = call, callDay.Day })
            .GroupBy(dated => dated.Day, dated => dated.Call)
            .Select(SpentOf<DateOnly>())
            .ToListAsync(ct);
        List<Spent<ChatId>> byChat = await calls.GroupBy(call => call.ChatId).Select(SpentOf<ChatId>()).ToListAsync(ct);

        List<DayUsage> eachDay = [.. Enumerable.Range(0, days).Select(index => DayOf(today.AddDays(index + 1 - days), byDay))];
        List<ChatCost> chats = [.. byChat
            .OrderByDescending(chat => chat.Cost() ?? 0)
            .ThenByDescending(chat => chat.Calls)
            .Select(chat => new ChatCost(chat.Key, chat.Calls, chat.Cost(), chat.Estimate))];
        Spent<DateOnly> total = Spent<DateOnly>.Sum(default, byDay);
        return new Result<WorkspaceUsage>(new WorkspaceUsage(eachDay, chats, total.Calls, total.Tokens(), total.Cost(), total.Estimate));
    }

    private static DayUsage DayOf(DateOnly day, List<Spent<DateOnly>> byDay)
    {
        Spent<DateOnly>? spent = byDay.Find(found => found.Key == day);
        return spent is null
            ? new DayUsage(day, 0, new TokenCounts(0, 0, 0, 0, 0), Cost: null, Estimate: false)
            : new DayUsage(day, spent.Calls, spent.Tokens(), spent.Cost(), spent.Estimate);
    }
}
