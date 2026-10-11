using System;
using System.Threading;
using System.Threading.Tasks;
using Bagatka.AiSloth.AgentAccounts.Contracts;
using Bagatka.AiSloth.AgentAccounts.Data;
using Bagatka.AiSloth.AgentAccounts.Kinds;
using Bagatka.AiSloth.AgentAccounts.Model;
using Bagatka.Foundation;
using Bagatka.Foundation.Modules;
using Microsoft.EntityFrameworkCore;

namespace Bagatka.AiSloth.AgentAccounts;

internal sealed partial class AgentAccountsApi
{
    // Asks the plan's vendor whether the person approved, as often as it allows, or exchanges the code
    // they brought back. Once signed in, the credential becomes the plan's secret, and the sign-in is
    // over; until it expires, a failed attempt can be retried.
    public async Task<Result<SignInProgress>> CompleteSignInAsync(Actor actor, CompleteSignIn command, CancellationToken ct)
    {
        await using AgentAccountsDbContext db = await databases.CreateDbContextAsync(ct);

        if (actor is not UserActor user)
        {
            return new Result<SignInProgress>(AgentAccountsErrors.SignInNotFound);
        }

        DateTimeOffset now = time.GetUtcNow();
        SignIn? signIn = await db.SignIns.SingleOrDefaultAsync(found => found.Id == command.Id && found.UserId == user.UserId, ct);
        if (signIn is null || signIn.ExpiredAt(now))
        {
            return new Result<SignInProgress>(AgentAccountsErrors.SignInNotFound);
        }

        TimeSpan wait = signIn.WaitAt(now);
        if (wait > TimeSpan.Zero)
        {
            return new Result<SignInProgress>(new SignInProgress(Account: null, wait));
        }

        IPlanKind plan = (IPlanKind)kinds.Of(signIn.Kind);
        Result<SignInStep> step = await plan.ContinueSignInAsync(signIn.Id, signIn.UserCode, signIn.Secret(box), command.Code, ct);
        if (step.Failed)
        {
            return new Result<SignInProgress>(step.Error);
        }

        if (step.Output.Credential is not Credential credential)
        {
            signIn.Polled(step.Output.Interval, now);
            _ = await db.SaveAsync(ct);
            return new Result<SignInProgress>(new SignInProgress(Account: null, signIn.Interval));
        }

        AgentAccount account = AgentAccount.SignedIn(signIn.UserId, signIn.Kind, signIn.Name, credential, box, time);
        db.Accounts.Add(account);
        db.SignIns.Remove(signIn);
        Result saved = await db.SaveAsync(ct);
        if (saved.Failed)
        {
            return new Result<SignInProgress>(saved.Error);
        }

        CaptureAdded(signIn.UserId, account);
        return new Result<SignInProgress>(new SignInProgress(account.ToSummary(), TimeSpan.Zero));
    }
}
