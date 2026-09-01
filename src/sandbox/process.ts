import type {
  ProcessOutput,
  SandboxProcess,
} from "@cloudflare/sandbox";

export async function readSandboxProcessOutput(
  process: SandboxProcess,
  maxBytes: number,
): Promise<ProcessOutput<string>> {
  try {
    return await process.output({ encoding: "utf8", maxBytes });
  } finally {
    disposeSandboxProcess(process);
  }
}

export function disposeSandboxProcess(process: SandboxProcess): void {
  const disposable = process as SandboxProcess & {
    [Symbol.dispose]?: () => void;
    capability?: { [Symbol.dispose]?: () => void };
  };
  const dispose = disposable[Symbol.dispose];
  if (dispose) {
    dispose.call(disposable);
    return;
  }
  // Sandbox 0.13's process wrapper owns an RPC capability but does not expose
  // its disposal in SandboxProcess. Release it after output/log consumption.
  disposable.capability?.[Symbol.dispose]?.();
}
