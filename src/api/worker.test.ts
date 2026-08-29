import { expect, test } from "bun:test";
import app from "./worker";

test("unknown routes return a JSON 404 response", async () => {
  const response = await app.request("https://example.com/unknown");

  expect(response.status).toBe(404);
  expect(response.headers.get("Content-Type")).toContain("application/json");
  expect(await response.text()).toBe('{"error":"Not found"}');
});
