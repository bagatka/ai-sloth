import type { ISandbox } from "@cloudflare/sandbox";
import { REPOSITORY_DIR } from "./git";

const PI_TIMEOUT = 5 * 60 * 1000;
const PI_OUTPUT_LIMIT = 1024 * 1024;

export async function runPi(sandbox: ISandbox, prompt: string) {
  const pi = await sandbox.exec(
    ["pi", "--print", "--no-session", "--no-approve", "--", prompt],
    {
      cwd: REPOSITORY_DIR,
      env: {
        OPENROUTER_API_KEY: "injected-by-egress-proxy",
        PI_SKIP_VERSION_CHECK: "1",
        PI_TELEMETRY: "0",
      },
      timeout: PI_TIMEOUT,
    },
  );

  return pi.output({
    encoding: "utf8",
    maxBytes: PI_OUTPUT_LIMIT,
  });
}
