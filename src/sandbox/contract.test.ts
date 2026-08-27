import { describe, expect, test } from "bun:test";
import {
  isSessionId,
  parseNewSessionRequest,
  parseSessionMessageRequest,
  readJsonBody,
  RequestBodyTooLargeError,
} from "./contract";

describe("new session request", () => {
  test("normalizes a public GitHub repository", () => {
    expect(parseNewSessionRequest({
      repositoryUrl: "https://github.com/owner/repository",
      branch: "main",
      prompt: "Review it",
    })).toEqual({
      repositoryUrl: "https://github.com/owner/repository.git",
      branch: "main",
      prompt: "Review it",
    });
  });

  test("rejects repositories outside the supported boundary", () => {
    for (const repositoryUrl of [
      "http://github.com/owner/repository",
      "https://example.com/owner/repository",
      "https://github.com/owner/repository/issues",
      "https://token@github.com/owner/repository",
    ]) {
      expect(parseNewSessionRequest({
        repositoryUrl,
        branch: "main",
        prompt: "Review it",
      })).toBeNull();
    }
  });

  test("rejects unsafe branches and empty prompts", () => {
    expect(parseNewSessionRequest({
      repositoryUrl: "https://github.com/owner/repository",
      branch: "--upload-pack=command",
      prompt: "Review it",
    })).toBeNull();
    expect(parseNewSessionRequest({
      repositoryUrl: "https://github.com/owner/repository",
      branch: "main",
      prompt: "   ",
    })).toBeNull();
  });
});

describe("continued session request", () => {
  test("accepts only a non-empty prompt", () => {
    expect(parseSessionMessageRequest({ prompt: "Continue" })).toEqual({
      prompt: "Continue",
    });
    expect(parseSessionMessageRequest({ prompt: "" })).toBeNull();
  });

  test("recognizes generated session IDs", () => {
    expect(isSessionId("b47f6e35-b7f3-4c6f-91f6-93f0479ec15b")).toBeTrue();
    expect(isSessionId("not-a-session")).toBeFalse();
  });
});

test("request bodies are bounded before parsing", async () => {
  const request = new Request("https://example.com/sessions", {
    method: "POST",
    body: "x".repeat(20 * 1024 + 1),
  });

  expect(readJsonBody(request)).rejects.toBeInstanceOf(RequestBodyTooLargeError);
});
