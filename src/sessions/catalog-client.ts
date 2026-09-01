import type { SessionCatalogOperations } from "./catalog";
import { createSessionCatalog } from "./internal/catalog";

export interface SessionCatalogBindings {
  SESSION_DB: D1Database;
}

export function bindSessionCatalog(
  bindings: SessionCatalogBindings,
): SessionCatalogOperations {
  return createSessionCatalog(bindings.SESSION_DB);
}
