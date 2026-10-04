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
    public async Task<Result<UserId>> SignInAsync(Actor actor, SignIn command, CancellationToken ct)
    {
        // Only system code, which validated the provider's token, may vouch for an identity.
        if (actor is not SystemActor)
        {
            return new Result<UserId>(Error.Forbidden);
        }

        if (!User.Register(command, time).TryGetValue(out User? newcomer, out Error? invalid))
        {
            return new Result<UserId>(invalid);
        }

        if (await FindAsync() is UserId known)
        {
            return new Result<UserId>(known);
        }

        db.Users.Add(newcomer);
        if ((await db.SaveAsync(ct)).IsError(out Error? failed))
        {
            // Two first sign-ins at once, such as a web app's parallel requests: the other one won.
            return ReferenceEquals(failed, ModuleDbContextExtensions.AlreadyExists) && await FindAsync() is UserId raced
                ? new Result<UserId>(raced)
                : new Result<UserId>(failed);
        }

        return new Result<UserId>(newcomer.Id);

        async Task<UserId?> FindAsync()
        {
            return await db.Users
                .Where(user => user.Issuer == command.Issuer && user.Subject == command.Subject)
                .Select(user => (UserId?)user.Id)
                .SingleOrDefaultAsync(ct);
        }
    }
}
