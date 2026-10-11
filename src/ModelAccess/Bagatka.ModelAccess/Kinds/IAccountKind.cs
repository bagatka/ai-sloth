using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using Bagatka.ModelAccess.Catalog;

namespace Bagatka.ModelAccess.Kinds;

// One way to pay for a vendor's models: everything that differs between kinds, behind ModelVendors,
// which is the same for every kind. A plan (IPlanKind) also signs in, renews, and has limits.
internal interface IAccountKind
{
    public ModelAccessKind Id { get; }

    // Whether it may name another endpoint than its vendor's, one that speaks the same API.
    public bool TakesEndpoint { get; }

    // Where models.dev's catalog has its models, and how they're priced: at the vendor's price, as an
    // estimate at the vendor's API price for a plan, or not at all at an endpoint of the account's own.
    public CatalogPricing Pricing(bool ownEndpoint);

    // Where an agent's calls go, and the headers that pay for them.
    public ModelEndpoint Endpoint(ModelCredential credential, Uri? ownEndpoint);

    // The models the vendor offers the account, with what it states of each; throws when the vendor
    // can't be reached.
    public Task<IReadOnlyList<Offered>> OfferedAsync(ModelEndpoint endpoint, CancellationToken ct);
}
