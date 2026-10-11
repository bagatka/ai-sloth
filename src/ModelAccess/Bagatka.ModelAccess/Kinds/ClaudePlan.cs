using System;
using System.Buffers.Text;
using System.Collections.Generic;
using System.Linq;
using System.Security.Cryptography;
using System.Threading;
using System.Threading.Tasks;
using Bagatka.ModelAccess.Catalog;
using Bagatka.Foundation;
using Bagatka.Sdk.Anthropic;

namespace Bagatka.ModelAccess.Kinds;

// A Claude plan, signed in as Claude Code does: the person approves at Claude's page, which shows them a
// code to bring back, with the sign-in's state after a '#'. It runs Claude Code, which runs on
// Anthropic's Agent SDK and calls Anthropic's API through the model gateway with its chat's token in
// place of the plan's. Its models are Anthropic's list for the plan, and its limits the five-hour and
// weekly windows Claude Code's /usage shows.
internal sealed class ClaudePlan(ModelAccessSettings settings, ClaudeSignInClient claude, AnthropicModelsClient anthropic, ClaudePlanUsageClient usage, TimeProvider time) : IPlanKind
{
    private const int MaxCodeLength = 2048;
    private static readonly TimeSpan SignInLifetime = TimeSpan.FromMinutes(10);

    public ModelAccessKind Id => ModelAccessKind.ClaudePlan;


    public bool TakesEndpoint => false;

    public CatalogPricing Pricing(bool ownEndpoint)
    {
        return new CatalogPricing(ModelCatalog.Anthropic, Estimate: true, Priced: true);
    }

    public ModelEndpoint Endpoint(ModelCredential credential, Uri? ownEndpoint)
    {
        return ModelEndpoints.Through(settings.AnthropicApi, "Authorization", "Bearer " + credential.Token);
    }

    // The plan's token lists the plan's models, with the beta Anthropic wants for it.
    public async Task<IReadOnlyList<Offered>> OfferedAsync(ModelEndpoint endpoint, CancellationToken ct)
    {
        Dictionary<string, string> headers = new Dictionary<string, string>(endpoint.Headers, StringComparer.OrdinalIgnoreCase) { ["anthropic-beta"] = ClaudePlanUsageClient.OAuthBeta };
        IReadOnlyList<AnthropicModel> served = await anthropic.ListModelsAsync(endpoint.Url, headers, ct);
        return [.. served.Select(Offered.Of)];
    }

    // The PKCE verifier is the sign-in's secret.
    public Task<PendingSignIn> StartSignInAsync(string state, CancellationToken ct)
    {
        string verifier = Base64Url.EncodeToString(RandomNumberGenerator.GetBytes(32));
        Uri url = claude.AuthorizationUrl(state, ClaudeSignInClient.CodeChallenge(verifier));
        return Task.FromResult(new PendingSignIn(url, UserCode: null, SignInLifetime, TimeSpan.Zero, verifier));
    }

    public async Task<Result<SignInStep>> ContinueSignInAsync(string state, string? userCode, string secret, string? code, CancellationToken ct)
    {
        string pasted = (code ?? string.Empty).Trim();
        int separator = pasted.LastIndexOf('#', StringComparison.Ordinal);
        if (pasted.Length is 0 or > MaxCodeLength || separator <= 0)
        {
            return new Result<SignInStep>(Error.Validation("code", "Paste the whole code Claude shows, with the part after its #."));
        }

        if (!string.Equals(pasted[(separator + 1)..], state, StringComparison.Ordinal))
        {
            return new Result<SignInStep>(Error.Validation("code", "This code belongs to another sign-in; start again."));
        }

        Result<ClaudeTokens> exchanged = await claude.ExchangeAsync(pasted[..separator], state, secret, ct);
        return exchanged.Failed
            ? new Result<SignInStep>(Error.Validation("code", "Claude refused the code (" + exchanged.Error.Message + "); start again."))
            : new Result<SignInStep>(new SignInStep(CredentialOf(exchanged.Output)));
    }

    public async Task<Result<ModelCredential>> RenewAsync(ModelCredential credential, CancellationToken ct)
    {
        Result<ClaudeTokens> renewed = await claude.RefreshAsync(credential.RefreshToken!, ct);
        return renewed.Failed ? new Result<ModelCredential>(renewed.Error) : new Result<ModelCredential>(CredentialOf(renewed.Output));
    }

    public async Task<IReadOnlyList<UsageMeter>> MetersAsync(ModelCredential credential, CancellationToken ct)
    {
        IReadOnlyList<ClaudePlanWindow> windows = await usage.GetWindowsAsync(settings.AnthropicApi, credential.Token, ct);
        return [.. windows.Select(window => new UsageMeter(window.Name, window.UsedPercent, 100, "percent", window.ResetsAt))];
    }

    private ModelCredential CredentialOf(ClaudeTokens tokens)
    {
        return new ModelCredential(tokens.AccessToken, tokens.RefreshToken, time.GetUtcNow() + tokens.ExpiresIn);
    }
}
