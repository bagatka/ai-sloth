export { ContainerProxy } from "@cloudflare/sandbox";
export { Sandbox } from "./internal/sandbox";
export type { SandboxBindings } from "./internal/sandbox";
export {
  createSandboxBackup,
  createSandboxInstance,
  deleteSandboxBackup,
  destroySandboxInstance,
  restoreSandboxBackup,
  stopAgentProcesses,
} from "./internal/sandbox-instance";
export type {
  SandboxBackup,
  SandboxInstance,
} from "./internal/sandbox-instance";
