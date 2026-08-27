import {
  Sandbox as SandboxBase,
  type SandboxEnv,
} from "@cloudflare/sandbox";

export interface SandboxBindings extends SandboxEnv<Sandbox> {
  OPENROUTER_API_KEY: string;
  SANDBOX_API_TOKEN: string;
  SESSION_DB: D1Database;
  SESSION_SNAPSHOTS: R2Bucket;
}

declare global {
  namespace Cloudflare {
    interface Env extends SandboxBindings {}
  }
}

export class Sandbox extends SandboxBase<SandboxBindings> {
  interceptHttps = true;
}

// Assignment invokes SandboxBase's setter, registering the handler with ContainerProxy.
Sandbox.outboundByHost = {
  "openrouter.ai": (request: Request, env: SandboxBindings) => {
    const headers = new Headers(request.headers);
    headers.set("Authorization", `Bearer ${env.OPENROUTER_API_KEY}`);
    return fetch(new Request(request, { headers }));
  },
};
