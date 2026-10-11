namespace Bagatka.ModelAccess.Catalog;

// Where the catalog has an account's models (one of ModelCatalog's vendors), whether their prices are
// a plan's estimate, and whether they're priced at all.
internal sealed record CatalogPricing(string Vendor, bool Estimate, bool Priced);
