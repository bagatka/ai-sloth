using System;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Bagatka.AiSloth.AgentAccounts.Contracts;
using Bagatka.AiSloth.AgentAccounts.Data;
using Bagatka.AiSloth.AgentAccounts.Model;
using Bagatka.Foundation;
using Bagatka.Foundation.Modules;
using Microsoft.EntityFrameworkCore;
using Bagatka.ModelAccess;

namespace Bagatka.AiSloth.AgentAccounts;

internal sealed partial class AgentAccountsApi
{
    public async Task<Result<SignInStarted>> StartSignInAsync(Actor actor, StartSignIn command, CancellationToken ct)
    {
        if (actor is not UserActor user)
        {
            return new Result<SignInStarted>(Error.Forbidden);
        }

        if (!vendors.Kinds.Contains(command.Kind) || !vendors.IsPlan(command.Kind))
        {
            return new Result<SignInStarted>(Error.Validation("kind", "Only a plan is added by signing in; an API key is added with its secret."));
        }

        if (!Allowed(command.Kind))
        {
            return new Result<SignInStarted>(Error.Validation("kind", "This host doesn't allow this kind of account."));
        }

        Result<AgentAccountName> name = AgentAccountName.Parse(command.Name);
        if (name.Failed)
        {
            return new Result<SignInStarted>(name.Error);
        }

        await using AgentAccountsDbContext db = await databases.CreateDbContextAsync(ct);

        // Maintenance, without rules: the person's sign-ins that can no longer complete.
        DateTimeOffset now = time.GetUtcNow();
        await db.SignIns.Where(old => old.UserId == user.UserId && old.ExpiresAt <= now).ExecuteDeleteAsync(ct);

        SignInId id = SignInId.New();
        PendingSignIn pending = await vendors.StartSignInAsync(command.Kind, StateOf(id), ct);
        SignIn signIn = SignIn.Start(id, user.UserId, command.Kind, name.Output, pending.UserCode, pending.ExpiresIn, pending.Interval, pending.Secret, box, time);
        db.SignIns.Add(signIn);
        Result saved = await db.SaveAsync(ct);
        if (saved.Failed)
        {
            return new Result<SignInStarted>(saved.Error);
        }

        return new Result<SignInStarted>(new SignInStarted(id, pending.Url, pending.UserCode, signIn.ExpiresAt, pending.Interval));
    }
}
