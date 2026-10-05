using System;
using System.Globalization;
using System.Linq;
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
    // The app's name ChatGPT suggests to the person when they first allow it.
    private const string AppName = "AiSloth";

    public async Task<Result<SignInStarted>> StartSignInAsync(Actor actor, StartSignIn command, CancellationToken ct)
    {
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

        bool returnsThere = command.Callback.AbsoluteUri.Length <= SignIn.MaxCallbackLength && ChatGptSignInClient.AcceptsRedirect(command.Callback);
        if (!returnsThere)
        {
            return new Result<SignInStarted>(Error.Validation("callback", "ChatGPT returns only to http://127.0.0.1:<port>/auth/callback."));
        }

        // Maintenance, without rules: the person's sign-ins that can no longer complete.
        DateTimeOffset now = time.GetUtcNow();
        await db.SignIns.Where(old => old.UserId == user.UserId && old.ExpiresAt <= now).ExecuteDeleteAsync(ct);

        SignIn signIn = SignIn.Start(user.UserId, command.Kind, name.Output, command.Callback, box, time);
        db.SignIns.Add(signIn);
        Result saved = await db.SaveAsync(ct);
        if (saved.Failed)
        {
            return new Result<SignInStarted>(saved.Error);
        }

        // OpenAI identifies each installation; this deployment's ID derives from its encryption key.
        string hostId = "urn:uuid:" + box.DeriveId("openai-agent-host").ToString("D", CultureInfo.InvariantCulture);
        Uri url = chatGpt.AuthorizationUrl(signIn.Authorization(AppName, hostId, box));
        return new Result<SignInStarted>(new SignInStarted(signIn.Id, url, signIn.ExpiresAt));
    }
}
