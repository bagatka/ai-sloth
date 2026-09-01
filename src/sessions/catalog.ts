export type Project = {
  kind: "project";
  id: string;
  name: string;
};

export type SessionSummary = {
  kind: "session";
  id: string;
  name: string;
  status: "running" | "waiting" | "completed" | "failed";
  revision: number | null;
  projectId: string | null;
  githubRepositoryId: string;
  controllerUserId: string;
  createdAt: string;
  updatedAt: string;
};

export type SessionCatalogItem = Project | Pick<SessionSummary, "kind" | "id" | "name" | "status">;

export type SessionCatalogPage = {
  items: SessionCatalogItem[];
  previousCursor: string | null;
  nextCursor: string | null;
};

export type ProjectDetails = Project & {
  workspaceId: string;
  githubRepositoryId: string;
  parentProjectId: string | null;
  instructions: string;
  version: number;
};

export type SessionCatalogFailureCode =
  | "invalid_input"
  | "not_found"
  | "not_controller"
  | "conflict"
  | "internal_error";

export type SessionCatalogOutcome<T> =
  | { ok: true; value: T }
  | { ok: false; code: SessionCatalogFailureCode };

export type SessionCatalogOperations = {
  listItems(input: {
    workspaceId: string;
    githubRepositoryId: string;
    parentProjectId: string | null;
    cursor: string | null;
  }): Promise<SessionCatalogOutcome<SessionCatalogPage>>;
  createProject(input: {
    workspaceId: string;
    githubRepositoryId: string;
    parentProjectId: string | null;
    name: string;
  }): Promise<SessionCatalogOutcome<ProjectDetails>>;
  getProject(input: {
    workspaceId: string;
    githubRepositoryId: string;
    projectId: string;
  }): Promise<SessionCatalogOutcome<ProjectDetails>>;
  updateProject(input: {
    workspaceId: string;
    githubRepositoryId: string;
    projectId: string;
    name?: string;
    instructions?: string;
    expectedVersion?: number;
    parentProjectId?: string | null;
  }): Promise<SessionCatalogOutcome<ProjectDetails>>;
  moveSession(input: {
    workspaceId: string;
    githubRepositoryId: string;
    sessionId: string;
    controllerUserId: string;
    projectId: string | null;
  }): Promise<SessionCatalogOutcome<undefined>>;
};
