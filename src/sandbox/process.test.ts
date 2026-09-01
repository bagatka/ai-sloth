import { expect, test } from "bun:test";
import type { SandboxProcess } from "@cloudflare/sandbox";
import { readSandboxProcessOutput } from "./process";

test("releases sandbox process RPC capabilities after collecting output", async () => {
  let disposed = false;
  const process = {
    capability: {
      [Symbol.dispose]() {
        disposed = true;
      },
    },
    async output() {
      return {
        stdout: "done",
        stderr: "",
        exitCode: 0,
        timedOut: false,
        truncated: false,
      };
    },
  } as unknown as SandboxProcess;

  expect((await readSandboxProcessOutput(process, 4096)).stdout).toBe("done");
  expect(disposed).toBeTrue();
});

test("releases sandbox process RPC capabilities when output fails", async () => {
  let disposed = false;
  const process = {
    capability: {
      [Symbol.dispose]() {
        disposed = true;
      },
    },
    async output() {
      throw new Error("unavailable");
    },
  } as unknown as SandboxProcess;

  await expect(readSandboxProcessOutput(process, 4096)).rejects.toThrow("unavailable");
  expect(disposed).toBeTrue();
});
