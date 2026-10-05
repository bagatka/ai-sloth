using System;
using System.Threading;
using System.Threading.Tasks;
using Bagatka.AiSloth.AgentAccounts.Contracts;
using Bagatka.AiSloth.AgentAccounts.Model;
using Bagatka.Foundation;
using Bagatka.Foundation.Modules;
using Bagatka.Sdk.OpenAI;
using Microsoft.EntityFrameworkCore;

namespace Bagatka.AiSloth.AgentAccounts;

internal sealed partial class AgentAccountsApi
{
    public async Task<Result<AgentAccountSummary>> CompleteSignInAsync(Actor actor, CompleteSignIn command, CancellationToken ct)
    {
        if (actor is not UserActor user)
        {
            return new Result<AgentAccountSummary>(AgentAccountsErrors.SignInNotFound);
        }

        SignIn? signIn = await db.SignIns.SingleOrDefaultAsync(found => found.Id == command.Id && found.UserId == user.UserId, ct);
        DateTimeOffset now = time.GetUtcNow();
        if (signIn is null || signIn.ExpiredAt(now))
        {
            return new Result<AgentAccountSummary>(AgentAccountsErrors.SignInNotFound);
        }

        Result<SignInAnswer> answer = signIn.Read(command.ReturnedTo);
        if (answer.Failed)
        {
            return new Result<AgentAccountSummary>(answer.Error);
        }

        // A code works once: whatever happens next, this attempt is over (it expires on its own).
        string clientId = answer.Output.ClientId;
        Result<ChatGptTokens> exchanged = await chatGpt.ExchangeCodeAsync(clientId, answer.Output.Code, signIn.OpenVerifier(box), signIn.Callback, ct);
        if (exchanged.Failed)
        {
            return new Result<AgentAccountSummary>(Error.Validation("returnedTo", "ChatGPT refused the sign-in (" + exchanged.Error.Message + "); start again."));
        }

        ChatGptTokens tokens = exchanged.Output;
        if (!tokens.AllowsPlanUse)
        {
            return new Result<AgentAccountSummary>(Error.Validation("returnedTo", "ChatGPT didn't allow using your plan; sign in again and allow it."));
        }

        ChatGptIdToken? identity = tokens.IdToken is null ? null : ChatGptSignInClient.ReadIdToken(tokens.IdToken);
        if (!signIn.IssuedFor(identity, clientId, settings.ChatGptAuthority, now))
        {
            return new Result<AgentAccountSummary>(Error.Validation("returnedTo", "ChatGPT's answer didn't check out; start again."));
        }

        PlanSession session = PlanSession.Start(clientId, identity, tokens, now);
        AgentAccount account = AgentAccount.SignedIn(user.UserId, signIn.Kind, signIn.Name, session, box, time);
        db.Accounts.Add(account);
        db.SignIns.Remove(signIn);
        Result saved = await db.SaveAsync(ct);
        if (saved.Failed)
        {
            return new Result<AgentAccountSummary>(saved.Error);
        }

        return new Result<AgentAccountSummary>(account.ToSummary());
    }
}
