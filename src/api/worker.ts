import { ContainerProxy, Sandbox } from "@ai-sloth/sandbox";
import type { SessionBindings } from "@ai-sloth/sessions";
import { SessionCoordinator } from "@ai-sloth/sessions/coordinator";
import { createApp } from "./app";
import type { ApiBindings } from "./internal/environment";

export { ContainerProxy, createApp, Sandbox, SessionCoordinator };
export type { ApiBindings } from "./internal/environment";
export type {
  NewSessionRequest,
  SessionMessageRequest,
} from "./internal/sessions/request";

declare global {
  namespace Cloudflare {
    interface Env extends ApiBindings, SessionBindings {}
  }
}

export default createApp();
