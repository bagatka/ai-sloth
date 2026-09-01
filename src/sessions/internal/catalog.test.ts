import { expect, test } from "bun:test";
import {
  ProjectContextError,
  resolveProjectInstructions,
} from "./catalog";

const input = {
  workspaceId: "c47f6e35-b7f3-4c6f-91f6-93f0479ec15b",
  githubRepositoryId: "1296269",
  projectId: "b47f6e35-b7f3-4c6f-91f6-93f0479ec15b",
};

test("combines project instructions from root to leaf", async () => {
  const database = instructionDatabase([
    { name: "Leaf", instructions: "Leaf rules", depth: 1 },
    { name: "Middle", instructions: "", depth: 2 },
    { name: "Root", instructions: "Root rules", depth: 3 },
  ]);

  expect(await resolveProjectInstructions(database, input)).toBe(
    "## Root\nRoot rules\n\n## Leaf\nLeaf rules",
  );
});

test("rejects an unknown project", async () => {
  await expect(
    resolveProjectInstructions(instructionDatabase([]), input),
  ).rejects.toEqual(new ProjectContextError("not_found"));
});

function instructionDatabase(
  results: Array<{ name: string; instructions: string; depth: number }>,
): D1Database {
  return {
    prepare() {
      return {
        bind() {
          return {
            async all() {
              return { success: true, results };
            },
          };
        },
      };
    },
  } as unknown as D1Database;
}
