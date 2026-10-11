using Bagatka.AiSloth.AgentAccounts.Data;
using System;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Bagatka.AiSloth.AgentAccounts.Contracts;
using Bagatka.AiSloth.AgentAccounts.Model;
using Bagatka.Foundation;
using Bagatka.Foundation.Modules;
using Bagatka.Sdk.Anthropic;
using Bagatka.Sdk.GitHub;
using Bagatka.Sdk.OpenAI;
using Microsoft.EntityFrameworkCore;

namespace Bagatka.AiSloth.AgentAccounts;

internal sealed partial class AgentAccountsApi
{
    public async Task<Result<SignInStarted>> StartSignInAsync(Actor actor, StartSignIn command, CancellationToken ct)
    {
        await using AgentAccountsDbContext db = await databases.CreateDbContextAsync(ct);

        if (actor is not UserActor user)
        {
            return new Result<SignInStarted>(Error.Forbidden);
        }

        Error? refused = null;
        if (!KindRules.Known(command.Kind))
        {
            refused = Error.Validation("kind", "Unknown kind of account.");
        }
        else if (!KindRules.AddedBySignIn(command.Kind))
        {
            refused = Error.Validation("kind", "This kind of account is added with its secret.");
        }
        else if (!KindRules.Allowed(command.Kind, settings))
        {
            refused = Error.Validation("kind", "This host doesn't allow this kind of account.");
        }
        if (refused is not null)
        {
            return new Result<SignInStarted>(refused);
        }

        Result<AgentAccountName> name = AgentAccountName.Parse(command.Name);
        if (name.Failed)
        {
            return new Result<SignInStarted>(name.Error);
        }

        // Maintenance, without rules: the person's sign-ins that can no longer complete.
        DateTimeOffset now = time.GetUtcNow();
        await db.SignIns.Where(old => old.UserId == user.UserId && old.ExpiresAt <= now).ExecuteDeleteAsync(ct);
        await db.CodeSignIns.Where(old => old.UserId == user.UserId && old.ExpiresAt <= now).ExecuteDeleteAsync(ct);

        if (KindRules.SignsInWithCode(command.Kind))
        {
            return await StartCodeSignInAsync(db, user, command, name.Output, ct);
        }

        return await StartPageSignInAsync(db, user, command, name.Output, ct);
    }

    // A Claude plan, as Claude Code signs in: the person approves at Claude's page, which then shows them
    // a code to bring back.
    private async Task<Result<SignInStarted>> StartPageSignInAsync(AgentAccountsDbContext db, UserActor user, StartSignIn command, AgentAccountName name, CancellationToken ct)
    {
        SignIn signIn = SignIn.Start(user.UserId, command.Kind, name, box, time);
        db.SignIns.Add(signIn);
        Result saved = await db.SaveAsync(ct);
        if (saved.Failed)
        {
            return new Result<SignInStarted>(saved.Error);
        }

        Uri url = claude.AuthorizationUrl(signIn.State, ClaudeSignInClient.CodeChallenge(signIn.OpenVerifier(box)));
        return new Result<SignInStarted>(new SignInStarted(signIn.Id, url, UserCode: null, signIn.ExpiresAt, TimeSpan.Zero));
    }

    // A Copilot plan: the person enters GitHub's code for the host's Copilot app at GitHub. A ChatGPT
    // plan: they enter OpenAI's code for Codex at OpenAI, as Codex's own sign-in asks.
    private async Task<Result<SignInStarted>> StartCodeSignInAsync(AgentAccountsDbContext db, UserActor user, StartSignIn command, AgentAccountName name, CancellationToken ct)
    {
        CodeSignIn signIn;
        Uri verificationUrl;
        if (command.Kind == AgentAccountKind.ChatGptPlan)
        {
            CodexDeviceCode code = await codex.StartAsync(ct);
            signIn = CodeSignIn.Start(user.UserId, command.Kind, name, code.DeviceAuthId, code.UserCode, code.ExpiresIn, code.Interval, box, time);
            verificationUrl = code.VerificationUrl;
        }
        else
        {
            GitHubDeviceCode code = await github.RequestDeviceCodeAsync(settings.CopilotClientId, ct);
            signIn = CodeSignIn.Start(user.UserId, command.Kind, name, code.DeviceCode, code.UserCode, code.ExpiresIn, code.Interval, box, time);
            verificationUrl = code.VerificationUri;
        }

        db.CodeSignIns.Add(signIn);
        Result saved = await db.SaveAsync(ct);
        if (saved.Failed)
        {
            return new Result<SignInStarted>(saved.Error);
        }

        return new Result<SignInStarted>(signIn.ToStarted(verificationUrl));
    }
}
