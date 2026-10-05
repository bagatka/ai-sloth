using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Bagatka.AiSloth.Users.Contracts;
using Bagatka.AiSloth.Users.Model;
using Bagatka.Foundation;
using Microsoft.EntityFrameworkCore;

namespace Bagatka.AiSloth.Users;

internal sealed partial class UsersApi
{
    public async Task<Result<IReadOnlyList<SessionSummary>>> ListSessionsAsync(Actor actor, CancellationToken ct)
    {
        if (actor is not UserActor user)
        {
            return new Result<IReadOnlyList<SessionSummary>>(Error.Unauthorized);
        }

        List<Session> sessions = await db.Sessions.AsNoTracking().Where(session => session.UserId == user.UserId).ToListAsync(ct);
        DateTimeOffset now = time.GetUtcNow();
        return new Result<IReadOnlyList<SessionSummary>>([.. sessions
            .Where(session => !session.ExpiredAt(now))
            .OrderByDescending(session => session.StartedAt)
            .Select(session => session.ToSummary())]);
    }
}
