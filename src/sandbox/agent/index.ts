import { getSandbox } from "@cloudflare/sandbox";
import type { AgentRunRequest, AgentRunResponse } from "../contract";
import { HttpStatusCode } from "../http-status";
import type { SandboxBindings } from "../sandbox";
import { cloneRepository } from "./git";
import { runPi } from "./pi";

export async function runAgentInSandbox(
  namespace: SandboxBindings["Sandbox"],
  input: AgentRunRequest,
): Promise<Response> {
  const sandbox = getSandbox(namespace, crypto.randomUUID(), {
    sleepAfter: "30s",
    keepAlive: false,
  });

  try {
    const clone = await cloneRepository(
      sandbox,
      input.repositoryUrl,
      input.branch,
    );
    if (clone.timedOut) {
      return Response.json(
        { error: "Repository clone timed out" },
        { status: HttpStatusCode.GatewayTimeout },
      );
    }
    if (clone.exitCode !== 0) {
      return Response.json(
        { error: "Repository clone failed", details: clone.stderr },
        { status: HttpStatusCode.UnprocessableContent },
      );
    }

    const result = await runPi(sandbox, input.prompt);
    if (result.timedOut) {
      return Response.json(
        { error: "Agent timed out" },
        { status: HttpStatusCode.GatewayTimeout },
      );
    }
    if (result.exitCode !== 0) {
      return Response.json(
        { error: "Agent failed", details: result.stderr },
        { status: HttpStatusCode.InternalServerError },
      );
    }

    return Response.json(
      {
        output: result.stdout,
        truncated: result.truncated,
      } satisfies AgentRunResponse,
      { status: HttpStatusCode.Ok },
    );
  } catch {
    return Response.json(
      { error: "Sandbox run failed" },
      { status: HttpStatusCode.InternalServerError },
    );
  } finally {
    await sandbox.destroy();
  }
}
