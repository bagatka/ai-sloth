import { getSandbox } from "@cloudflare/sandbox";
import type { SandboxBindings } from "../sandbox";
import { checkoutRepository, type RepositoryCheckoutResult } from "./git";
import { readPiSession, runPi } from "./pi";
import { SessionStore, type SessionAttempt } from "./session-store";

export async function runSession(
  namespace: SandboxBindings["Sandbox"],
  sessions: SessionStore,
  session: SessionAttempt,
  prompt: string,
): Promise<SessionRunResult> {
  const sandbox = getSandbox(namespace, crypto.randomUUID(), {
    sleepAfter: "30s",
    keepAlive: false,
  });

  try {
    const checkout = requireCheckout(
      await checkoutRepository(sandbox, session.repository),
    );
    const previousPiSession = await sessions.restore(session);
    const agent = requireAgent(await runPi(sandbox, prompt, previousPiSession));
    const piSession = await readPiSession(sandbox);

    await sessions.commit(session, checkout.commitSha, piSession);

    return {
      commitSha: checkout.commitSha,
      output: agent.stdout,
      truncated: agent.truncated,
    };
  } finally {
    try {
      await sandbox.destroy();
    } catch (error) {
      console.error(
        "Sandbox cleanup failed",
        error instanceof Error ? error.message : "Unknown error",
      );
    }
  }
}

export class SessionRunError extends Error {
  constructor(
    readonly code: SessionRunErrorCode,
    readonly details?: string,
  ) {
    super(code);
  }
}

function requireCheckout(
  result: RepositoryCheckoutResult,
): Extract<RepositoryCheckoutResult, { ok: true }> {
  if (result.ok) {
    return result;
  }

  throw new SessionRunError(
    result.timedOut ? "checkout_timeout" : "checkout_failed",
    result.details,
  );
}

function requireAgent<T extends AgentResult>(result: T): T {
  if (result.timedOut) {
    throw new SessionRunError("agent_timeout");
  }
  if (result.exitCode !== 0) {
    throw new SessionRunError("agent_failed", result.stderr);
  }
  return result;
}

type AgentResult = {
  timedOut: boolean;
  exitCode: number;
  stderr: string;
};

type SessionRunResult = {
  commitSha: string;
  output: string;
  truncated: boolean;
};

type SessionRunErrorCode =
  | "checkout_timeout"
  | "checkout_failed"
  | "agent_timeout"
  | "agent_failed";
