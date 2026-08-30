import { describe, expect, test } from "bun:test";
import {
  readJsonBody,
  RequestBodyTooLargeError,
} from "../internal/http/body";
import {
  isSessionId,
  parseNewSessionRequest,
  parseSessionMessageRequest,
} from "../internal/sessions/request";

describe("new session request", () => {
  test("accepts a GitHub repository ID", () => {
    expect(parseNewSessionRequest({
      githubRepositoryId: "1296269",
      branch: "main",
      prompt: "Review it",
    })).toEqual({
      githubRepositoryId: "1296269",
      branch: "main",
      prompt: "Review it",
      name: "Review it",
      projectId: null,
    });
  });

  test("rejects repository URLs and invalid IDs", () => {
    expect(parseNewSessionRequest({
      repositoryUrl: "https://github.com/owner/repository",
      branch: "main",
      prompt: "Review it",
    })).toBeNull();
    expect(parseNewSessionRequest({
      githubRepositoryId: "not-an-id",
      branch: "main",
      prompt: "Review it",
    })).toBeNull();
  });

  test("rejects unsafe branches and empty prompts", () => {
    expect(parseNewSessionRequest({
      githubRepositoryId: "1296269",
      branch: "--upload-pack=command",
      prompt: "Review it",
    })).toBeNull();
    expect(parseNewSessionRequest({
      githubRepositoryId: "1296269",
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
