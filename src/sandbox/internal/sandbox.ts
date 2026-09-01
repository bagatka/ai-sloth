import {
  Sandbox as SandboxBase,
  type SandboxEnv,
} from "@cloudflare/sandbox";

export interface SandboxBindings extends SandboxEnv<Sandbox> {
  BACKUP_BUCKET: R2Bucket;
  OPENROUTER_API_KEY: string;
  R2_ACCESS_KEY_ID?: string;
  R2_SECRET_ACCESS_KEY?: string;
  BACKUP_BUCKET_NAME?: string;
  CLOUDFLARE_R2_ACCOUNT_ID?: string;
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
