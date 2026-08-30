import {
  getSandbox,
  type DirectoryBackup,
  type ISandbox,
} from "@cloudflare/sandbox";
import { readSandboxProcessOutput } from "../process";
import type { SandboxBindings } from "./sandbox";

const AGENT_USER = "agent";
const SANDBOX_STATE_ROOT = "/workspace/state";
const PROJECT_DIRECTORY = `${SANDBOX_STATE_ROOT}/project`;
const GIT_DIRECTORY = `${SANDBOX_STATE_ROOT}/git`;

export type SandboxInstance = {
  readonly sandbox: ISandbox & { destroy(): Promise<void> };
  readonly projectDirectory: string;
  readonly gitDirectory: string;
};

export type SandboxBackup = {
  id: string;
  local: boolean;
};

export function createSandboxInstance(
  namespace: SandboxBindings["Sandbox"],
): Promise<SandboxInstance> {
  return startSandboxInstance(namespace);
}

export function restoreSandboxBackup(
  namespace: SandboxBindings["Sandbox"],
  backup: SandboxBackup,
): Promise<SandboxInstance> {
  return startSandboxInstance(namespace, backup);
}

async function startSandboxInstance(
  namespace: SandboxBindings["Sandbox"],
  backup?: SandboxBackup,
): Promise<SandboxInstance> {
  const sandbox = getSandbox(namespace, crypto.randomUUID(), {
    sleepAfter: "30s",
    keepAlive: false,
  });
  const instance = {
    sandbox,
    projectDirectory: PROJECT_DIRECTORY,
    gitDirectory: GIT_DIRECTORY,
  };

  try {
    if (backup) {
      const restored = await sandbox.restoreBackup(directoryBackup(backup));
      if (!restored.success) throw new Error("Could not restore sandbox backup");
    }
    return instance;
  } catch (error) {
    await destroySandboxInstance(instance);
    throw error;
  }
}

export async function stopAgentProcesses(instance: SandboxInstance): Promise<void> {
  const process = await instance.sandbox.exec([
    "sh",
    "-c",
    `pkill -STOP -u ${AGENT_USER} 2>/dev/null || true; pkill -KILL -u ${AGENT_USER} 2>/dev/null || true; ! pgrep -u ${AGENT_USER} >/dev/null`,
  ]);
  const output = await readSandboxProcessOutput(process, 4096);
  if (output.exitCode !== 0) {
    throw new Error("Could not stop all agent processes");
  }
}

export async function createSandboxBackup(
  instance: SandboxInstance,
  input: { name: string; ttlSeconds: number; local: boolean },
): Promise<SandboxBackup> {
  const backup = await instance.sandbox.createBackup({
    dir: SANDBOX_STATE_ROOT,
    name: input.name,
    ttl: input.ttlSeconds,
    localBucket: input.local,
  });
  return { id: backup.id, local: backup.localBucket === true };
}

export async function deleteSandboxBackup(
  bucket: R2Bucket,
  backup: SandboxBackup,
): Promise<void> {
  if (!isBackupId(backup.id)) return;
  // The pinned Sandbox SDK has no delete operation. Keep its two-object layout
  // isolated here; the bucket lifecycle remains the final cleanup boundary.
  await bucket.delete([
    `backups/${backup.id}/data.sqsh`,
    `backups/${backup.id}/meta.json`,
  ]);
}

export async function destroySandboxInstance(instance: SandboxInstance): Promise<void> {
  try {
    await instance.sandbox.destroy();
  } catch (error) {
    console.error(
      "Sandbox cleanup failed",
      error instanceof Error ? error.message : "Unknown error",
    );
  }
}

function directoryBackup(backup: SandboxBackup): DirectoryBackup {
  if (!isBackupId(backup.id)) throw new Error("Invalid sandbox backup");
  return {
    id: backup.id,
    dir: SANDBOX_STATE_ROOT,
    ...(backup.local ? { localBucket: true } : {}),
  };
}

function isBackupId(value: string): boolean {
  return /^[0-9a-f]{8}-[0-9a-f]{4}-[1-5][0-9a-f]{3}-[89ab][0-9a-f]{3}-[0-9a-f]{12}$/.test(
    value,
  );
}
