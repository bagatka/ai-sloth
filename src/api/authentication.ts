import type { ApiApp } from "./internal/environment";
import {
  currentAccount,
  login,
  logout,
  register,
} from "./internal/accounts/authentication-handlers";
import { requireAccount } from "./internal/accounts/authenticate";
import { withAccounts } from "./internal/accounts/bind";
import { limitAuthentication } from "./internal/accounts/rate-limit";
import { readRequestBody } from "./internal/http/json";

export function mapAuthenticationEndpoints(app: ApiApp): void {
  app.post(
    "/auth/register",
    readRequestBody,
    limitAuthentication,
    withAccounts,
    register,
  );
  app.post(
    "/auth/login",
    readRequestBody,
    limitAuthentication,
    withAccounts,
    login,
  );
  app.post("/auth/logout", withAccounts, requireAccount, logout);
  app.get("/auth/me", withAccounts, requireAccount, currentAccount);
}
