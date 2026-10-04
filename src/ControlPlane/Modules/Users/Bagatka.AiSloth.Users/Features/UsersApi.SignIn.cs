using System.Linq;
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
    public async Task<Result<UserId>> SignInAsync(Actor actor, VerifiedIdentity identity, CancellationToken ct)
    {
        // Only system code, which validated the provider's token, may vouch for an identity.
        if (actor is not SystemActor)
        {
            return new Result<UserId>(Error.Forbidden);
        }

        Result<User> registration = User.Register(identity, time);
        if (registration.Failed)
        {
            return new Result<UserId>(registration.Error);
        }

        UserId? existing = await FindUserIdAsync(identity, ct);
        if (existing is not null)
        {
            return new Result<UserId>(existing.Value);
        }

        User newcomer = registration.Output;
        db.Users.Add(newcomer);
        Result saved = await db.SaveAsync(ct);
        if (saved.Failed)
        {
            // Two first sign-ins at once, such as a web app's parallel requests: the other one won.
            bool otherSignInWon = saved.Error == ModuleDbContextExtensions.AlreadyExists;
            UserId? winner = null;
            if (otherSignInWon)
            {
                winner = await FindUserIdAsync(identity, ct);
            }

            return winner is null ? new Result<UserId>(saved.Error) : new Result<UserId>(winner.Value);
        }

        return new Result<UserId>(newcomer.Id);
    }

    private async Task<UserId?> FindUserIdAsync(VerifiedIdentity identity, CancellationToken ct)
    {
        return await db.Users
            .Where(user => user.Issuer == identity.Issuer && user.Subject == identity.Subject)
            .Select(user => (UserId?)user.Id)
            .SingleOrDefaultAsync(ct);
    }
}
