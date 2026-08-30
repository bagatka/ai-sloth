import type { AccountOperations } from "./authentication";
import { createAccountOperations } from "./internal/accounts";
import { D1AccountRepository } from "./internal/store";

export interface AccountBindings {
  ACCOUNTS_DB: D1Database;
}

export function bindAccounts(bindings: AccountBindings): AccountOperations {
  return createAccountOperations(
    new D1AccountRepository(bindings.ACCOUNTS_DB),
  );
}
