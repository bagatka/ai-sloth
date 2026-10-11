using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Bagatka.AiSloth.AgentAccounts.Catalog;
using Bagatka.AiSloth.AgentAccounts.Contracts;
using Bagatka.AiSloth.AgentAccounts.Model;
using Bagatka.Foundation;
using Bagatka.Sdk.GitHub;

namespace Bagatka.AiSloth.AgentAccounts.Kinds;

// A Copilot plan, signed in with a code the person enters at GitHub for this host's Copilot app, whose
// token never expires and so is never renewed. Copilot's CLI and pi call Copilot's API through the
// model gateway, which adds the plan's token and the headers Copilot's CLI sends. Its models are the
// chat models the plan's policy enables, and its limits its quotas: premium and chat requests of those
// it allows, or, on a plan without limits that bills what it uses, the credits spent.
internal sealed class CopilotPlan(AgentAccountsSettings settings, GitHubClient github, CopilotModelsClient copilot) : IPlanKind
{
    public AgentAccountKind Id => AgentAccountKind.CopilotPlan;

    public bool Allowed => true;

    public bool TakesEndpoint => false;

    public CatalogPricing Pricing(bool ownEndpoint)
    {
        return new CatalogPricing(ModelCatalog.Copilot, Estimate: true, Priced: true);
    }

    public ModelEndpoint Endpoint(Credential credential, Uri? ownEndpoint)
    {
        return new ModelEndpoint(settings.CopilotApi, CopilotApi.Headers(credential.Token));
    }

    public async Task<IReadOnlyList<Offered>> OfferedAsync(ModelEndpoint endpoint, CancellationToken ct)
    {
        IReadOnlyList<CopilotModel> listed = await copilot.ListModelsAsync(endpoint.Url, endpoint.Headers, ct);
        return [.. listed.Select(Offered.Of)];
    }

    public async Task<PendingSignIn> StartSignInAsync(SignInId id, CancellationToken ct)
    {
        GitHubDeviceCode code = await github.RequestDeviceCodeAsync(settings.CopilotClientId, ct);
        return new PendingSignIn(code.VerificationUri, code.UserCode, code.ExpiresIn, code.Interval, code.DeviceCode);
    }

    // Waiting for the person, or over: expired, declined, or anything else GitHub says.
    public async Task<Result<SignInStep>> ContinueSignInAsync(SignInId id, string? userCode, string secret, string? code, CancellationToken ct)
    {
        GitHubDevicePoll poll = await github.PollDeviceAsync(settings.CopilotClientId, secret, ct);
        if (poll.Tokens is null)
        {
            bool waiting = poll.Error is "authorization_pending" or "slow_down";
            return waiting ? new Result<SignInStep>(new SignInStep(Credential: null, poll.Interval)) : new Result<SignInStep>(AgentAccountsErrors.SignInNotFound);
        }

        // A token that expires would stop working, since this host never renews it.
        return poll.Tokens.ExpiresIn is null
            ? new Result<SignInStep>(new SignInStep(new Credential(poll.Tokens.AccessToken)))
            : new Result<SignInStep>(Error.Conflict("agent_accounts.copilot_token_expires", "GitHub gave a token that expires, which this host can't renew: its Copilot app must not expire its tokens."));
    }

    // Its token never expires, so its credential has no refresh token and is never due.
    public Task<Result<Credential>> RenewAsync(Credential credential, CancellationToken ct)
    {
        throw new InvalidOperationException("A Copilot plan's token is never renewed.");
    }

    public async Task<IReadOnlyList<UsageMeter>> MetersAsync(Credential credential, CancellationToken ct)
    {
        CopilotQuota quota = await github.GetCopilotQuotaAsync(credential.Token, ct);
        (string Id, string Name)[] counted = [("premium_interactions", "Premium requests"), ("chat", "Chat requests")];
        List<UsageMeter> meters = [];
        foreach ((string id, string name) in counted)
        {
            CopilotQuotaSnapshot? snapshot = quota.Snapshots.FirstOrDefault(found => string.Equals(found.Id, id, StringComparison.Ordinal));
            if (snapshot is { Unlimited: false, Entitlement: > 0 })
            {
                meters.Add(new UsageMeter(name, snapshot.Entitlement - snapshot.Remaining, snapshot.Entitlement, "requests", quota.ResetsAt));
            }
        }

        CopilotQuotaSnapshot? billed = quota.Snapshots.FirstOrDefault(found => found is { Unlimited: true, CreditsUsed: not null });
        if (billed is not null)
        {
            meters.Add(new UsageMeter("Credits", billed.CreditsUsed!.Value, Limit: null, "credits", quota.ResetsAt));
        }

        return meters;
    }
}
