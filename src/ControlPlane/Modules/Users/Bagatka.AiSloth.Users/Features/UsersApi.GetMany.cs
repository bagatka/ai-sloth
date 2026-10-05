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
    // Not handled: a call with thousands of IDs; clients ask for the people on one screen.
    public async Task<IReadOnlyList<UserSummary>> GetManyAsync(Actor actor, IReadOnlyCollection<UserId> ids, CancellationToken ct)
    {
        if (actor is AnonymousActor || ids.Count == 0)
        {
            return [];
        }

        List<User> users = await db.Users.AsNoTracking().Where(user => ids.Contains(user.Id)).ToListAsync(ct);
        return [.. users.Select(user => user.ToSummary())];
    }
}
