using Bagatka.AiSloth.Users.Data;
using System.Threading;
using System.Threading.Tasks;
using Bagatka.AiSloth.Users.Contracts;
using Bagatka.AiSloth.Users.Model;
using Bagatka.Foundation;
using Bagatka.Foundation.Modules;
using Microsoft.EntityFrameworkCore;

namespace Bagatka.AiSloth.Users;

internal sealed partial class UsersApi
{
    public async Task<Result> EndSessionAsync(Actor actor, SessionId id, CancellationToken ct)
    {
        await using UsersDbContext db = await databases.CreateDbContextAsync(ct);

        if (actor is not UserActor user)
        {
            return new Result(Error.Unauthorized);
        }

        Session? session = await db.Sessions.SingleOrDefaultAsync(found => found.Id == id && found.UserId == user.UserId, ct);
        if (session is null)
        {
            return new Result(UsersErrors.SessionNotFound);
        }

        db.Sessions.Remove(session);
        return await db.SaveAsync(ct);
    }
}
