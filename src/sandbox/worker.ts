import { runAgentInSandbox } from "./agent";
import { parseAgentRunRequest, type AgentRunRequest } from "./contract";
import { HttpStatusCode } from "./http-status";
import type { SandboxBindings } from "./sandbox";

export { ContainerProxy } from "@cloudflare/sandbox";
export { Sandbox } from "./sandbox";

export default {
  async fetch(request: Request, env: SandboxBindings): Promise<Response> {
    const path = new URL(request.url).pathname;
    if (request.method !== "POST" || path !== "/run") {
      return new Response("Not found", { status: HttpStatusCode.NotFound });
    }

    if (!env.SANDBOX_API_TOKEN || !env.OPENROUTER_API_KEY) {
      return new Response("Service is not configured", {
        status: HttpStatusCode.ServiceUnavailable,
      });
    }

    if (
      request.headers.get("Authorization") !== `Bearer ${env.SANDBOX_API_TOKEN}`
    ) {
      return new Response("Unauthorized", {
        status: HttpStatusCode.Unauthorized,
      });
    }

    let input: AgentRunRequest | null;
    try {
      input = parseAgentRunRequest(await request.json());
    } catch {
      input = null;
    }

    if (!input) {
      return new Response(
        "Expected non-empty repositoryUrl, branch, and prompt strings",
        { status: HttpStatusCode.BadRequest },
      );
    }

    return runAgentInSandbox(env.Sandbox, input);
  },
};
