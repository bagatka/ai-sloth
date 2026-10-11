using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Bagatka.AiSloth.AgentAccounts.Catalog;
using Bagatka.AiSloth.AgentAccounts.Contracts;
using Bagatka.AiSloth.AgentAccounts.Model;
using Bagatka.Foundation;
using Bagatka.Sdk.OpenAI;

namespace Bagatka.AiSloth.AgentAccounts.Kinds;

// A ChatGPT plan, signed in as Codex does, with a code the person enters at OpenAI. It runs Codex, whose
// calls go to ChatGPT's Codex backend with the plan's token and the ChatGPT account it signed in to, as
// Codex's own do. Its models are those the backend offers the Codex the nooks run, and its limits the
// five-hour and weekly windows Codex's /status shows.
internal sealed class ChatGptPlan(AgentAccountsSettings settings, CodexSignInClient codex, OpenAIModelsClient openAI, ChatGptUsageClient usage, TimeProvider time) : IPlanKind
{
    // The Codex the nook images run (src/Daemon/Dockerfile: codex-acp 2.1.1).
    private const string NookCodexVersion = "0.159.3";

    public AgentAccountKind Id => AgentAccountKind.ChatGptPlan;

    public bool Allowed => settings.AllowChatGptPlans;

    public bool TakesEndpoint => false;

    public CatalogPricing Pricing(bool ownEndpoint)
    {
        return new CatalogPricing(ModelCatalog.OpenAI, Estimate: true, Priced: true);
    }

    public ModelEndpoint Endpoint(Credential credential, Uri? ownEndpoint)
    {
        Dictionary<string, string> headers = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase) { ["Authorization"] = "Bearer " + credential.Token };
        if (credential.VendorAccount is string account)
        {
            headers["ChatGPT-Account-Id"] = account;
        }

        return new ModelEndpoint(settings.CodexApi, headers);
    }

    public async Task<IReadOnlyList<Offered>> OfferedAsync(ModelEndpoint endpoint, CancellationToken ct)
    {
        IReadOnlyList<OpenAIModel> served = await openAI.ListCodexModelsAsync(endpoint.Url, endpoint.Headers, NookCodexVersion, ct);
        return [.. served.Select(model => new Offered(model.Id, model.DisplayName ?? model.Id, UsesTools: true, StatedFacts(model)))];
    }

    public async Task<PendingSignIn> StartSignInAsync(SignInId id, CancellationToken ct)
    {
        CodexDeviceCode code = await codex.StartAsync(ct);
        return new PendingSignIn(code.VerificationUrl, code.UserCode, code.ExpiresIn, code.Interval, code.DeviceAuthId);
    }

    public async Task<Result<SignInStep>> ContinueSignInAsync(SignInId id, string? userCode, string secret, string? code, CancellationToken ct)
    {
        ChatGptTokens? tokens = await codex.PollAsync(secret, userCode ?? string.Empty, ct);
        Credential? signedIn = tokens is null ? null : CredentialOf(tokens, tokens.AccountId);
        return new Result<SignInStep>(new SignInStep(signedIn));
    }

    public async Task<Result<Credential>> RenewAsync(Credential credential, CancellationToken ct)
    {
        Result<ChatGptTokens> renewed = await codex.RefreshAsync(credential.RefreshToken!, ct);
        return renewed.Failed
            ? new Result<Credential>(renewed.Error)
            : new Result<Credential>(CredentialOf(renewed.Output, renewed.Output.AccountId ?? credential.VendorAccount));
    }

    public async Task<IReadOnlyList<UsageMeter>> MetersAsync(Credential credential, CancellationToken ct)
    {
        IReadOnlyList<ChatGptUsageWindow> windows = await usage.GetWindowsAsync(settings.ChatGptBackend, credential.Token, credential.VendorAccount, ct);
        return [.. windows.Select(window => new UsageMeter(WindowName(window.Length), window.UsedPercent, 100, "percent", window.ResetsAt))];
    }

    private Credential CredentialOf(ChatGptTokens tokens, string? account)
    {
        return new Credential(tokens.AccessToken, tokens.RefreshToken, time.GetUtcNow() + tokens.ExpiresIn, account);
    }

    // What the backend states of a model: how hard it reasons, and its context.
    private static ModelFacts StatedFacts(OpenAIModel model)
    {
        return new ModelFacts(model.ReasoningEfforts.Count > 0 ? true : null, model.ReasoningEfforts, model.ContextWindow, MaxOutput: null, Price: null, CacheLifetime: null);
    }

    // A window by its length: five hours and a week on today's plans.
    private static string WindowName(TimeSpan? length)
    {
        int minutes = length is TimeSpan known ? (int)known.TotalMinutes : 0;
        return minutes switch
        {
            <= 0 => "Window",
            10_080 => "Week",
            1_440 => "Day",
            _ when minutes % 1_440 == 0 => string.Create(CultureInfo.InvariantCulture, $"{minutes / 1_440} days"),
            60 => "Hour",
            _ when minutes % 60 == 0 => string.Create(CultureInfo.InvariantCulture, $"{minutes / 60} hours"),
            _ => string.Create(CultureInfo.InvariantCulture, $"{minutes} minutes"),
        };
    }
}
