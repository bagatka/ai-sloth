import {
  getSandbox,
  proxyToSandbox,
  Sandbox as SandboxBase,
} from "@cloudflare/sandbox";

export { ContainerProxy } from "@cloudflare/sandbox";

const MAX_BODY_BYTES = 20 * 1024;
const MAX_PROMPT_LENGTH = 16 * 1024;
const MAX_RUN_MS = 10 * 60 * 1000;
const MAX_OUTPUT_BYTES = 1024 * 1024;
const REPOSITORY_DIR = "/workspace/repo";

type Env = {
  Sandbox: DurableObjectNamespace<Sandbox>;
  OPENROUTER_API_KEY: string;
  SANDBOX_API_TOKEN: string;
};

type RunRequest = {
  repository: string;
  branch: string;
  prompt: string;
};

export class Sandbox extends SandboxBase<Env> {
  interceptHttps = true;
}

Sandbox.outboundByHost = {
  "openrouter.ai": (request: Request, env: Env) => {
    if (!env.OPENROUTER_API_KEY) {
      return new Response("OpenRouter is not configured", { status: 503 });
    }

    const headers = new Headers(request.headers);
    headers.set("Authorization", `Bearer ${env.OPENROUTER_API_KEY}`);
    return fetch(new Request(request, { headers }));
  },
};

function errorResponse(message: string, status: number): Response {
  return Response.json({ error: message }, { status });
}

function normalizeRepository(value: unknown): string | null {
  if (typeof value !== "string" || value.length > 2048) {
    return null;
  }

  try {
    const url = new URL(value);
    if (
      url.protocol !== "https:" ||
      url.hostname !== "github.com" ||
      url.port ||
      url.username ||
      url.password ||
      url.search ||
      url.hash
    ) {
      return null;
    }

    const parts = url.pathname.split("/").filter(Boolean);
    if (parts.length !== 2) {
      return null;
    }

    const owner = parts[0];
    const repository = parts[1].replace(/\.git$/, "");
    const validPart = /^[A-Za-z0-9_.-]+$/;
    if (!validPart.test(owner) || !validPart.test(repository)) {
      return null;
    }

    return `https://github.com/${owner}/${repository}.git`;
  } catch {
    return null;
  }
}

function parseRunRequest(value: unknown): RunRequest | null {
  if (!value || typeof value !== "object" || Array.isArray(value)) {
    return null;
  }

  const input = value as Record<string, unknown>;
  const repository = normalizeRepository(input.url);
  const branch = input.branch;
  const prompt = input.prompt;

  if (
    !repository ||
    typeof branch !== "string" ||
    branch.length === 0 ||
    branch.length > 255 ||
    branch.startsWith("-") ||
    /[\u0000-\u001f\u007f]/.test(branch) ||
    typeof prompt !== "string" ||
    prompt.trim().length === 0 ||
    prompt.length > MAX_PROMPT_LENGTH
  ) {
    return null;
  }

  return { repository, branch, prompt };
}

async function readJson(request: Request): Promise<unknown> {
  if (!request.body) {
    throw new Error("Request body is required");
  }

  const reader = request.body.getReader();
  const chunks: Uint8Array[] = [];
  let length = 0;

  while (true) {
    const { done, value } = await reader.read();
    if (done) {
      break;
    }

    length += value.byteLength;
    if (length > MAX_BODY_BYTES) {
      await reader.cancel();
      throw new RangeError("Request body is too large");
    }
    chunks.push(value);
  }

  const body = new Uint8Array(length);
  let offset = 0;
  for (const chunk of chunks) {
    body.set(chunk, offset);
    offset += chunk.byteLength;
  }

  return JSON.parse(new TextDecoder().decode(body));
}

export default {
  async fetch(request: Request, env: Env): Promise<Response> {
    const proxy = await proxyToSandbox(request, env);
    if (proxy) {
      return proxy;
    }

    const url = new URL(request.url);
    if (request.method === "GET" && url.pathname === "/") {
      return Response.json({ endpoint: "POST /run" });
    }
    if (request.method !== "POST" || url.pathname !== "/run") {
      return errorResponse("Not found", 404);
    }
    if (!env.SANDBOX_API_TOKEN || !env.OPENROUTER_API_KEY) {
      return errorResponse("Service is not configured", 503);
    }
    if (request.headers.get("Authorization") !== `Bearer ${env.SANDBOX_API_TOKEN}`) {
      return errorResponse("Unauthorized", 401);
    }

    let input: RunRequest | null;
    try {
      input = parseRunRequest(await readJson(request));
    } catch (error) {
      return errorResponse(
        error instanceof RangeError ? error.message : "Invalid JSON body",
        error instanceof RangeError ? 413 : 400,
      );
    }
    if (!input) {
      return errorResponse(
        "Expected url, branch, and a non-empty prompt up to 16 KiB",
        400,
      );
    }

    const deadline = Date.now() + MAX_RUN_MS;
    const remaining = (): number => Math.max(1, deadline - Date.now());
    const sandbox = getSandbox(env.Sandbox, crypto.randomUUID(), {
      sleepAfter: "30s",
    });

    try {
      const clone = await sandbox.exec(
        [
          "git",
          "clone",
          "--depth",
          "1",
          "--single-branch",
          "--branch",
          input.branch,
          input.repository,
          REPOSITORY_DIR,
        ],
        { timeout: remaining() },
      );
      const cloneOutput = await clone.output({
        encoding: "utf8",
        maxBytes: 64 * 1024,
        timeout: remaining(),
      });
      if (cloneOutput.timedOut) {
        return errorResponse("Repository clone timed out", 504);
      }
      if (cloneOutput.exitCode !== 0) {
        return Response.json(
          { error: "Repository clone failed", details: cloneOutput.stderr },
          { status: 422 },
        );
      }

      const pi = await sandbox.exec(
        [
          "pi",
          "--print",
          "--no-session",
          "--no-approve",
          "--provider",
          "openrouter",
          "--model",
          "openai/gpt-5.6-luna",
          "--api-key",
          "injected-by-egress-proxy",
          "--thinking",
          "low",
          "--",
          input.prompt,
        ],
        {
          cwd: REPOSITORY_DIR,
          env: {
            PI_SKIP_VERSION_CHECK: "1",
            PI_TELEMETRY: "0",
          },
          timeout: remaining(),
        },
      );
      const output = await pi.output({
        encoding: "utf8",
        maxBytes: MAX_OUTPUT_BYTES,
        timeout: remaining(),
      });

      return Response.json(
        {
          output: output.stdout,
          stderr: output.stderr,
          exitCode: output.exitCode,
          timedOut: output.timedOut,
          truncated: output.truncated,
        },
        { status: output.timedOut ? 504 : output.exitCode === 0 ? 200 : 500 },
      );
    } catch (error) {
      console.error(
        "Sandbox run failed",
        error instanceof Error ? error.message : "Unknown error",
      );
      return errorResponse("Sandbox run failed", 500);
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
  },
};
