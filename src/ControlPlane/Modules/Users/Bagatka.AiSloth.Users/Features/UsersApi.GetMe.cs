using Bagatka.AiSloth.Users.Data;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Bagatka.AiSloth.Users.Contracts;
using Bagatka.Foundation;
using Microsoft.EntityFrameworkCore;

namespace Bagatka.AiSloth.Users;

internal sealed partial class UsersApi
{
    public async Task<Result<UserProfile>> GetMeAsync(Actor actor, CancellationToken ct)
    {
        await using UsersDbContext db = await databases.CreateDbContextAsync(ct);

        if (actor is not UserActor user)
        {
            return new Result<UserProfile>(Error.Unauthorized);
        }

        UserProfile? profile = await db.Users
            .Where(found => found.Id == user.UserId)
            .Select(found => new UserProfile(found.Id, found.Name.Value, found.CreatedAt))
            .SingleOrDefaultAsync(ct);
        return profile is null ? new Result<UserProfile>(UsersErrors.NotFound) : new Result<UserProfile>(profile);
    }
}
