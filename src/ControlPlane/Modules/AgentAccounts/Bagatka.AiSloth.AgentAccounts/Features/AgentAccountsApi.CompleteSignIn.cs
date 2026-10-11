using Bagatka.AiSloth.AgentAccounts.Data;
using System;
using System.Threading;
using System.Threading.Tasks;
using Bagatka.AiSloth.AgentAccounts.Contracts;
using Bagatka.AiSloth.AgentAccounts.Model;
using Bagatka.Foundation;
using Bagatka.Foundation.Modules;
using Bagatka.Sdk.GitHub;
using Bagatka.Sdk.OpenAI;
using Microsoft.EntityFrameworkCore;

namespace Bagatka.AiSloth.AgentAccounts;

internal sealed partial class AgentAccountsApi
{
    public async Task<Result<SignInProgress>> CompleteSignInAsync(Actor actor, CompleteSignIn command, CancellationToken ct)
    {
        await using AgentAccountsDbContext db = await databases.CreateDbContextAsync(ct);

        if (actor is not UserActor user)
        {
            return new Result<SignInProgress>(AgentAccountsErrors.SignInNotFound);
        }

        DateTimeOffset now = time.GetUtcNow();
        SignIn? signIn = await db.SignIns.SingleOrDefaultAsync(found => found.Id == command.Id && found.UserId == user.UserId, ct);
        if (signIn is not null && !signIn.ExpiredAt(now))
        {
            return await CompleteChatGptSignInAsync(db, user, signIn, command.ReturnedTo, now, ct);
        }

        CodeSignIn? codeSignIn = await db.CodeSignIns.SingleOrDefaultAsync(found => found.Id == command.Id && found.UserId == user.UserId, ct);
        if (codeSignIn is null || codeSignIn.ExpiredAt(now))
        {
            return new Result<SignInProgress>(AgentAccountsErrors.SignInNotFound);
        }

        return await CompleteCodeSignInAsync(db, user, codeSignIn, now, ct);
    }

    private async Task<Result<SignInProgress>> CompleteChatGptSignInAsync(AgentAccountsDbContext db, UserActor user, SignIn signIn, Uri? returnedTo, DateTimeOffset now, CancellationToken ct)
    {
        if (returnedTo is null)
        {
            return new Result<SignInProgress>(Error.Validation("returnedTo", "Give the address ChatGPT sent the browser to."));
        }

        Result<SignInAnswer> answer = signIn.Read(returnedTo);
        if (answer.Failed)
        {
            return new Result<SignInProgress>(answer.Error);
        }

        // A code works once: whatever happens next, this attempt is over (it expires on its own).
        string clientId = answer.Output.ClientId;
        Result<ChatGptTokens> exchanged = await chatGpt.ExchangeCodeAsync(clientId, answer.Output.Code, signIn.OpenVerifier(box), signIn.Callback, ct);
        if (exchanged.Failed)
        {
            return new Result<SignInProgress>(Error.Validation("returnedTo", "ChatGPT refused the sign-in (" + exchanged.Error.Message + "); start again."));
        }

        ChatGptTokens tokens = exchanged.Output;
        if (!tokens.AllowsPlanUse)
        {
            return new Result<SignInProgress>(Error.Validation("returnedTo", "ChatGPT didn't allow using your plan; sign in again and allow it."));
        }

        ChatGptIdToken? identity = tokens.IdToken is null ? null : ChatGptSignInClient.ReadIdToken(tokens.IdToken);
        if (!signIn.IssuedFor(identity, clientId, settings.ChatGptAuthority, now))
        {
            return new Result<SignInProgress>(Error.Validation("returnedTo", "ChatGPT's answer didn't check out; start again."));
        }

        PlanSession session = PlanSession.Start(clientId, identity, tokens, now);
        AgentAccount account = AgentAccount.SignedIn(user.UserId, signIn.Kind, signIn.Name, session, box, time);
        db.Accounts.Add(account);
        db.SignIns.Remove(signIn);
        return await AddedAsync(db, user, account, ct);
    }

    // Asks GitHub whether the person approved the code, as often as it allows. The token becomes the
    // plan's secret, which the model gateway adds to its agents' calls to Copilot.
    private async Task<Result<SignInProgress>> CompleteCodeSignInAsync(AgentAccountsDbContext db, UserActor user, CodeSignIn signIn, DateTimeOffset now, CancellationToken ct)
    {
        TimeSpan wait = signIn.WaitAt(now);
        if (wait > TimeSpan.Zero)
        {
            return new Result<SignInProgress>(new SignInProgress(Account: null, wait));
        }

        if (signIn.Kind == AgentAccountKind.CodexPlan)
        {
            return await CompleteCodexSignInAsync(db, user, signIn, now, ct);
        }

        GitHubDevicePoll poll = await github.PollDeviceAsync(settings.CopilotClientId, signIn.DeviceCode(box), ct);
        if (poll.Tokens is null)
        {
            return await NotYetAsync(db, signIn, poll, now, ct);
        }

        // This host never renews a Copilot plan's token, so one that expires would stop working.
        db.CodeSignIns.Remove(signIn);
        if (poll.Tokens.ExpiresIn is not null)
        {
            _ = await db.SaveAsync(ct);
            return new Result<SignInProgress>(Error.Conflict("agent_accounts.copilot_token_expires", "GitHub gave a token that expires, which this host can't renew: its Copilot app must not expire its tokens."));
        }

        Result<AgentAccount> added = AgentAccount.Add(workspaceId: null, user.UserId, signIn.Kind, signIn.Name, poll.Tokens.AccessToken, endpoint: null, box, time);
        if (added.Failed)
        {
            return new Result<SignInProgress>(added.Error);
        }

        db.Accounts.Add(added.Output);
        return await AddedAsync(db, user, added.Output, ct);
    }

    // Asks OpenAI whether the person approved Codex's code. The plan's session, renewed by this host,
    // becomes its secret; the model gateway adds its token to Codex's calls to ChatGPT's Codex backend.
    private async Task<Result<SignInProgress>> CompleteCodexSignInAsync(AgentAccountsDbContext db, UserActor user, CodeSignIn signIn, DateTimeOffset now, CancellationToken ct)
    {
        ChatGptTokens? tokens = await codex.PollAsync(signIn.DeviceCode(box), signIn.UserCode!, ct);
        if (tokens is null)
        {
            signIn.Polled(slowDown: null, now);
            _ = await db.SaveAsync(ct);
            return new Result<SignInProgress>(new SignInProgress(Account: null, signIn.Interval));
        }

        // A code works once: whatever happens next, this sign-in is over.
        db.CodeSignIns.Remove(signIn);
        ChatGptIdToken? identity = tokens.IdToken is null ? null : ChatGptSignInClient.ReadIdToken(tokens.IdToken);
        if (identity is null)
        {
            _ = await db.SaveAsync(ct);
            return new Result<SignInProgress>(Error.Conflict("agent_accounts.codex_no_identity", "OpenAI signed Codex in without saying who; start again."));
        }

        PlanSession session = PlanSession.Start(CodexSignInClient.ClientId, identity, tokens, now);
        AgentAccount account = AgentAccount.SignedIn(user.UserId, signIn.Kind, signIn.Name, session, box, time);
        db.Accounts.Add(account);
        return await AddedAsync(db, user, account, ct);
    }

    // Waiting for the person, or over: expired, declined, or anything else GitHub says.
    private static async Task<Result<SignInProgress>> NotYetAsync(AgentAccountsDbContext db, CodeSignIn signIn, GitHubDevicePoll poll, DateTimeOffset now, CancellationToken ct)
    {
        bool waiting = poll.Error is "authorization_pending" or "slow_down";
        if (waiting)
        {
            signIn.Polled(poll.Interval, now);
        }
        else
        {
            db.CodeSignIns.Remove(signIn);
        }

        _ = await db.SaveAsync(ct);
        return waiting
            ? new Result<SignInProgress>(new SignInProgress(Account: null, signIn.Interval))
            : new Result<SignInProgress>(AgentAccountsErrors.SignInNotFound);
    }

    private async Task<Result<SignInProgress>> AddedAsync(AgentAccountsDbContext db, UserActor user, AgentAccount account, CancellationToken ct)
    {
        Result saved = await db.SaveAsync(ct);
        if (saved.Failed)
        {
            return new Result<SignInProgress>(saved.Error);
        }

        CaptureAdded(user.UserId, account);
        return new Result<SignInProgress>(new SignInProgress(account.ToSummary(), TimeSpan.Zero));
    }
}
