using System;
using System.Collections.Frozen;
using System.Collections.Generic;
using System.Linq;
using System.Net;
using System.Net.Http;
using System.Threading;
using System.Threading.Tasks;
using Bagatka.Foundation;
using Bagatka.ModelAccess.Catalog;
using Bagatka.ModelAccess.Kinds;

namespace Bagatka.ModelAccess;

/// <summary>
/// Paying for vendors' models, the same way for every kind (<see cref="ModelAccessKind"/>): where an
/// agent's calls go and which headers pay for them, the models each kind offers with what's known of
/// them, and for a plan, signing in, renewing, and its limits. The caller keeps every credential and
/// sign-in; vendors are asked on each call, models.dev's catalog once a day. Thread-safe.
/// </summary>
public sealed class ModelVendors
{
    private readonly FrozenDictionary<ModelAccessKind, IAccountKind> _kinds;
    private readonly ModelCatalog _catalog;

    internal ModelVendors(IEnumerable<IAccountKind> kinds, ModelCatalog catalog)
    {
        List<IAccountKind> ordered = [.. kinds];
        _kinds = ordered.ToFrozenDictionary(kind => kind.Id);
        _catalog = catalog;
        Kinds = [.. ordered.Select(kind => kind.Id)];
    }

    /// <summary>Every kind, in the order to offer them: API keys, then plans.</summary>
    public IReadOnlyList<ModelAccessKind> Kinds { get; }

    /// <summary>Whether the kind is a plan, signed in rather than added with a secret.</summary>
    public bool IsPlan(ModelAccessKind kind)
    {
        return KindOf(kind) is IPlanKind;
    }

    /// <summary>Whether the kind may name another endpoint than its vendor's that speaks the same API.</summary>
    public bool TakesEndpoint(ModelAccessKind kind)
    {
        return KindOf(kind).TakesEndpoint;
    }

    /// <summary>
    /// Where an agent's calls go, and the headers that pay for them. A call's path follows the
    /// endpoint's URL as the vendor's own clients build it.
    /// </summary>
    public ModelEndpoint Endpoint(ModelAccessKind kind, ModelCredential credential, Uri? ownEndpoint)
    {
        return KindOf(kind).Endpoint(credential, ownEndpoint);
    }

    /// <summary>
    /// The models the vendor offers the credential, with what's known of each: what the vendor states,
    /// over models.dev's word, priced as the kind pays (<see cref="FactsAsync"/>). Throws when the
    /// vendor can't be reached.
    /// </summary>
    public async Task<IReadOnlyList<AgentModel>> ListModelsAsync(ModelAccessKind kind, ModelCredential credential, Uri? ownEndpoint, CancellationToken ct)
    {
        IAccountKind access = KindOf(kind);
        IReadOnlyList<Offered> offered = await access.OfferedAsync(access.Endpoint(credential, ownEndpoint), ct);
        CatalogPricing pricing = access.Pricing(ownEndpoint is not null);
        List<AgentModel> models = [];
        foreach (Offered model in offered)
        {
            ModelsDev.Model? cataloged = await _catalog.FindAsync(pricing.Vendor, model.Id, ct);
            ModelFacts facts = ModelCatalog.Facts(pricing.Vendor, model.Id, cataloged, pricing.Estimate, pricing.Priced);
            models.Add(new AgentModel(model.Id, model.Name, model.UsesTools, model.With(facts)));
        }

        return models;
    }

    /// <summary>
    /// What models.dev says of a model, without asking the vendor: priced at the vendor's price for an
    /// API key, as an estimate at the vendor's API price for a plan, and not at all at an endpoint of the
    /// account's own. <see cref="ModelFacts.Unknown"/> when the catalog lacks it.
    /// </summary>
    public async Task<ModelFacts> FactsAsync(ModelAccessKind kind, string model, bool ownEndpoint, CancellationToken ct)
    {
        CatalogPricing pricing = KindOf(kind).Pricing(ownEndpoint);
        ModelsDev.Model? cataloged = await _catalog.FindAsync(pricing.Vendor, model, ct);
        return ModelCatalog.Facts(pricing.Vendor, model, cataloged, pricing.Estimate, pricing.Priced);
    }

    /// <summary>
    /// Starts a plan's sign-in as its vendor's agent does. <paramref name="state"/> ties a code the
    /// vendor's page shows to this sign-in. Throws when the vendor can't be reached.
    /// </summary>
    public Task<PendingSignIn> StartSignInAsync(ModelAccessKind kind, string state, CancellationToken ct)
    {
        return PlanOf(kind).StartSignInAsync(state, ct);
    }

    /// <summary>
    /// Continues a sign-in: with the code the vendor's page showed, or, for one with a user code, by
    /// asking the vendor whether the person approved it. Fails when the vendor refuses, the code
    /// expired or was declined, or a code brought back belongs to another sign-in.
    /// </summary>
    public Task<Result<SignInStep>> ContinueSignInAsync(ModelAccessKind kind, string state, string? userCode, string secret, string? code, CancellationToken ct)
    {
        return PlanOf(kind).ContinueSignInAsync(state, userCode, secret, code, ct);
    }

    /// <summary>
    /// The plan's credential renewed (<see cref="ModelCredential.RenewalDue"/>); fails when the vendor
    /// ended the sign-in. Never renew one credential twice at once: a renewal replaces the refresh token.
    /// </summary>
    public Task<Result<ModelCredential>> RenewAsync(ModelAccessKind kind, ModelCredential credential, CancellationToken ct)
    {
        return PlanOf(kind).RenewAsync(credential, ct);
    }

    /// <summary>
    /// What the plan has used of its limits, asked of its vendor now, as its own agent asks. No vendor
    /// documents these answers. Fails when the vendor asks to be asked later (429), as Anthropic does
    /// when asked every minute or so; throws when the vendor can't be reached or refuses otherwise.
    /// </summary>
    public async Task<Result<IReadOnlyList<UsageMeter>>> LimitsAsync(ModelAccessKind kind, ModelCredential credential, CancellationToken ct)
    {
        try
        {
            IReadOnlyList<UsageMeter> meters = await PlanOf(kind).MetersAsync(credential, ct);
            return new Result<IReadOnlyList<UsageMeter>>(meters);
        }
        catch (HttpRequestException exception) when (exception.StatusCode == HttpStatusCode.TooManyRequests)
        {
            return new Result<IReadOnlyList<UsageMeter>>(Error.Conflict("model_access.ask_later", "The plan's vendor asked to be asked later; its limits show again in a few minutes."));
        }
    }

    private IAccountKind KindOf(ModelAccessKind kind)
    {
        IAccountKind? found = _kinds.GetValueOrDefault(kind);
        if (found is null)
        {
            throw new ArgumentOutOfRangeException(nameof(kind), kind, "No such kind of model access.");
        }

        return found;
    }

    private IPlanKind PlanOf(ModelAccessKind kind)
    {
        if (KindOf(kind) is not IPlanKind plan)
        {
            throw new InvalidOperationException("A " + kind + " isn't a plan.");
        }

        return plan;
    }
}
