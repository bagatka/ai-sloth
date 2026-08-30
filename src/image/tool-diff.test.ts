import { expect, test } from "bun:test";
import { mkdtemp, rm, writeFile } from "node:fs/promises";
import { join } from "node:path";
import { tmpdir } from "node:os";
import { captureWriteDiff, createWriteDiff } from "./tool-diff.js";

test("creates a unified patch for write tool changes", async () => {
  const directory = await mkdtemp(join(tmpdir(), "ai-sloth-write-diff-"));
  try {
    await writeFile(join(directory, "example.ts"), "export const value = 1;\n");
    const snapshot = captureWriteDiff(
      "write",
      { path: "example.ts", content: "export const value = 2;\n" },
      directory,
    );

    const patch = createWriteDiff(snapshot);

    expect(patch).toContain("--- a/example.ts");
    expect(patch).toContain("+++ b/example.ts");
    expect(patch).toContain("-export const value = 1;");
    expect(patch).toContain("+export const value = 2;");
  } finally {
    await rm(directory, { recursive: true, force: true });
  }
});

test("creates an added-file patch for a new write", () => {
  const snapshot = captureWriteDiff(
    "write",
    { path: `${crypto.randomUUID()}.ts`, content: "export {};\n" },
    tmpdir(),
  );

  expect(createWriteDiff(snapshot)).toContain("--- /dev/null");
});
