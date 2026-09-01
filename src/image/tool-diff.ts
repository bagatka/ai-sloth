import { readFileSync, statSync } from "node:fs";
import { resolve } from "node:path";
import { createTwoFilesPatch } from "diff";

const MAX_SOURCE_BYTES = 512 * 1024;

export type WriteDiffSnapshot = {
  path: string;
  existed: boolean;
  before: string;
  after: string;
};

export function captureWriteDiff(
  toolName: string,
  input: unknown,
  cwd: string,
): WriteDiffSnapshot | undefined {
  if (toolName !== "write" || !isRecord(input)) return;
  if (typeof input.path !== "string" || typeof input.content !== "string") return;
  if (input.content.includes("\0")) return;
  if (Buffer.byteLength(input.content) > MAX_SOURCE_BYTES) return;

  const absolutePath = resolve(cwd, input.path);
  try {
    if (statSync(absolutePath).size > MAX_SOURCE_BYTES) return;
    const before = readFileSync(absolutePath, "utf8");
    if (before.includes("\0")) return;
    return {
      path: input.path,
      existed: true,
      before,
      after: input.content,
    };
  } catch (error) {
    if (isMissingFile(error)) {
      return {
        path: input.path,
        existed: false,
        before: "",
        after: input.content,
      };
    }
    return;
  }
}

export function createWriteDiff(
  snapshot: WriteDiffSnapshot | undefined,
): string | undefined {
  if (!snapshot || snapshot.before === snapshot.after) return;
  const path = snapshot.path.replaceAll("\\", "/");
  return createTwoFilesPatch(
    snapshot.existed ? `a/${path}` : "/dev/null",
    `b/${path}`,
    snapshot.before,
    snapshot.after,
    undefined,
    undefined,
    { context: 3 },
  );
}

function isMissingFile(error: unknown): boolean {
  return error instanceof Error
    && "code" in error
    && (error as NodeJS.ErrnoException).code === "ENOENT";
}

function isRecord(value: unknown): value is Record<string, unknown> {
  return Boolean(value) && typeof value === "object" && !Array.isArray(value);
}
