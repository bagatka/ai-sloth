import type {
  ProjectDetails,
  SessionCatalogItem,
  SessionCatalogOperations,
  SessionCatalogOutcome,
  SessionCatalogPage,
  SessionSummary,
} from "../catalog";

const PAGE_SIZE = 20;
const MAX_NAME_LENGTH = 100;
const MAX_INSTRUCTIONS_BYTES = 16 * 1024;
const MAX_EFFECTIVE_INSTRUCTIONS_BYTES = 32 * 1024;

export function createSessionCatalog(
  database: D1Database,
): SessionCatalogOperations {
  return {
    async listItems(input) {
      if (
        !isId(input.workspaceId)
        || !isRepositoryId(input.githubRepositoryId)
        || (input.parentProjectId !== null && !isId(input.parentProjectId))
      ) {
        return failure("invalid_input");
      }
      const cursor = decodeCursor(input.cursor);
      if (input.cursor !== null && !cursor) return failure("invalid_input");

      try {
        if (input.parentProjectId !== null) {
          const parent = await findProject(database, {
            workspaceId: input.workspaceId,
            githubRepositoryId: input.githubRepositoryId,
            projectId: input.parentProjectId,
          });
          if (!parent) return failure("not_found");
        }
        const before = cursor?.direction === "before";
        const stored = await database
          .prepare(before ? LIST_ITEMS_BEFORE_SQL : LIST_ITEMS_AFTER_SQL)
          .bind(
            input.workspaceId,
            input.githubRepositoryId,
            input.parentProjectId,
            input.workspaceId,
            input.githubRepositoryId,
            input.parentProjectId,
            cursor?.createdAt ?? null,
            cursor?.createdAt ?? null,
            cursor?.createdAt ?? null,
            cursor?.id ?? null,
            PAGE_SIZE + 1,
          )
          .all<StoredCatalogItem>();
        if (!stored.success) throw new Error("Could not list project items");
        const hasMore = stored.results.length > PAGE_SIZE;
        const page = stored.results.slice(0, PAGE_SIZE);
        const visible = before ? page.reverse() : page;
        const first = visible[0];
        const last = visible[visible.length - 1];
        return success({
          items: visible.map(catalogItem),
          previousCursor: first && (before ? hasMore : cursor !== null)
            ? encodeCursor("before", first)
            : null,
          nextCursor: last && (before || hasMore)
            ? encodeCursor("after", last)
            : null,
        });
      } catch (error) {
        return internalFailure("Project item listing failed", error);
      }
    },

    async createProject(input) {
      const name = normalizeName(input.name);
      if (
        !name
        || !isId(input.workspaceId)
        || !isRepositoryId(input.githubRepositoryId)
        || (input.parentProjectId !== null && !isId(input.parentProjectId))
      ) {
        return failure("invalid_input");
      }
      const project: ProjectDetails = {
        kind: "project",
        id: crypto.randomUUID(),
        workspaceId: input.workspaceId,
        githubRepositoryId: input.githubRepositoryId,
        parentProjectId: input.parentProjectId,
        name,
        instructions: "",
        version: 1,
      };
      try {
        const write = await database.prepare(INSERT_PROJECT_SQL).bind(
          project.id,
          project.workspaceId,
          project.githubRepositoryId,
          project.parentProjectId,
          project.name,
        ).run();
        if (!write.success) throw new Error("Could not create project");
        return success(project);
      } catch (error) {
        return knownProjectFailure("Project creation failed", error);
      }
    },

    async getProject(input) {
      if (!validProjectIdentity(input)) return failure("invalid_input");
      try {
        const project = await findProject(database, input);
        return project ? success(project) : failure("not_found");
      } catch (error) {
        return internalFailure("Project loading failed", error);
      }
    },

    async updateProject(input) {
      if (!validProjectIdentity(input)) return failure("invalid_input");
      const hasName = input.name !== undefined;
      const hasInstructions = input.instructions !== undefined;
      const hasParent = input.parentProjectId !== undefined;
      const expectedVersion = input.expectedVersion;
      const name = hasName ? normalizeName(input.name ?? "") : null;
      const instructions = hasInstructions ? input.instructions ?? "" : null;
      if (
        (!hasName && !hasInstructions && !hasParent)
        || (hasName && !name)
        || (hasInstructions && !validInstructions(instructions ?? ""))
        || ((hasName || hasInstructions)
          && (!Number.isSafeInteger(expectedVersion) || (expectedVersion ?? 0) < 1))
        || (hasParent && input.parentProjectId !== null
          && (typeof input.parentProjectId !== "string"
            || !isId(input.parentProjectId)))
      ) {
        return failure("invalid_input");
      }

      try {
        const write = await database.prepare(UPDATE_PROJECT_SQL).bind(
          hasName ? name : null,
          hasInstructions ? instructions : null,
          hasParent ? 1 : 0,
          hasParent ? input.parentProjectId ?? null : null,
          expectedVersion ?? null,
          expectedVersion ?? null,
          input.projectId,
          input.workspaceId,
          input.githubRepositoryId,
        ).run();
        if (!write.success) throw new Error("Could not update project");
        if (!changed(write)) {
          return await findProject(database, input)
            ? failure("conflict")
            : failure("not_found");
        }
        const project = await findProject(database, input);
        if (!project) throw new Error("Updated project disappeared");
        return success(project);
      } catch (error) {
        return knownProjectFailure("Project update failed", error);
      }
    },

    async moveSession(input) {
      if (
        !isId(input.workspaceId)
        || !isRepositoryId(input.githubRepositoryId)
        || !isId(input.sessionId)
        || !isId(input.controllerUserId)
        || (input.projectId !== null && !isId(input.projectId))
      ) {
        return failure("invalid_input");
      }
      try {
        const write = await database.prepare(MOVE_SESSION_SQL).bind(
          input.projectId,
          input.sessionId,
          input.workspaceId,
          input.githubRepositoryId,
          input.controllerUserId,
          input.projectId,
          input.projectId,
          input.workspaceId,
          input.githubRepositoryId,
        ).run();
        if (!write.success) throw new Error("Could not move session");
        if (changed(write)) return success(undefined);

        const session = await database.prepare(FIND_SESSION_OWNER_SQL).bind(
          input.sessionId,
          input.workspaceId,
          input.githubRepositoryId,
        ).first<{ controllerUserId: string }>();
        if (!session) return failure("not_found");
        return session.controllerUserId === input.controllerUserId
          ? failure("not_found")
          : failure("not_controller");
      } catch (error) {
        return knownProjectFailure("Session move failed", error);
      }
    },

  };
}

export async function resolveProjectInstructions(
  database: D1Database,
  input: {
    workspaceId: string;
    githubRepositoryId: string;
    projectId: string | null;
  },
): Promise<string> {
  if (input.projectId === null) return "";
  const stored = await database.prepare(PROJECT_INSTRUCTION_CHAIN_SQL).bind(
    input.projectId,
    input.workspaceId,
    input.githubRepositoryId,
  ).all<{ name: string; instructions: string; depth: number }>();
  if (!stored.success) throw new Error("Could not load project instructions");
  if (stored.results.length === 0) {
    throw new ProjectContextError("not_found");
  }
  const sections = stored.results
    .sort((left, right) => right.depth - left.depth)
    .filter(({ instructions }) => instructions.trim().length > 0)
    .map(({ name, instructions }) => `## ${name}\n${instructions.trim()}`);
  const combined = sections.join("\n\n");
  if (new TextEncoder().encode(combined).byteLength > MAX_EFFECTIVE_INSTRUCTIONS_BYTES) {
    throw new ProjectContextError("instructions_too_large");
  }
  return combined;
}

export class ProjectContextError extends Error {
  constructor(readonly code: "not_found" | "instructions_too_large") {
    super(code);
  }
}

function validProjectIdentity(input: {
  workspaceId: string;
  githubRepositoryId: string;
  projectId: string;
}): boolean {
  return isId(input.workspaceId)
    && isRepositoryId(input.githubRepositoryId)
    && isId(input.projectId);
}

async function findProject(
  database: D1Database,
  input: { workspaceId: string; githubRepositoryId: string; projectId: string },
): Promise<ProjectDetails | null> {
  const stored = await database.prepare(FIND_PROJECT_SQL).bind(
    input.projectId,
    input.workspaceId,
    input.githubRepositoryId,
  ).first<Omit<ProjectDetails, "kind">>();
  return stored ? { ...stored, kind: "project" } : null;
}

function catalogItem(stored: StoredCatalogItem): SessionCatalogItem {
  return stored.kind === "project"
    ? { kind: "project", id: stored.id, name: stored.name }
    : {
        kind: "session",
        id: stored.id,
        name: stored.name,
        status: stored.status ?? "completed",
      };
}

function normalizeName(value: string): string | null {
  const name = value.trim().replace(/\s+/g, " ");
  return name.length > 0
    && name.length <= MAX_NAME_LENGTH
    && !/[\u0000-\u001f\u007f]/.test(name)
    ? name
    : null;
}

function validInstructions(value: string): boolean {
  return new TextEncoder().encode(value).byteLength <= MAX_INSTRUCTIONS_BYTES;
}

function isId(value: string): boolean {
  return /^[0-9a-f]{8}-[0-9a-f]{4}-4[0-9a-f]{3}-[89ab][0-9a-f]{3}-[0-9a-f]{12}$/.test(value);
}

function isRepositoryId(value: string): boolean {
  return /^[1-9][0-9]{0,19}$/.test(value);
}

function changed(write: D1Result): boolean {
  return (write.meta.changes ?? 0) > 0;
}

function success<T>(value: T): SessionCatalogOutcome<T> {
  return { ok: true, value };
}

function failure<T>(code: Exclude<SessionCatalogOutcome<T>, { ok: true }>["code"]): SessionCatalogOutcome<T> {
  return { ok: false, code };
}

function knownProjectFailure<T>(message: string, error: unknown): SessionCatalogOutcome<T> {
  if (error instanceof Error) {
    if (
      error.message.includes("project nesting limit")
      || error.message.includes("project cycle")
      || error.message.includes("workspace project limit")
    ) {
      return failure("conflict");
    }
    if (error.message.includes("invalid project parent") || error.message.includes("FOREIGN KEY")) {
      return failure("not_found");
    }
  }
  return internalFailure(message, error);
}

function internalFailure<T>(message: string, _error: unknown): SessionCatalogOutcome<T> {
  console.error(message);
  return failure("internal_error");
}

function encodeCursor(
  direction: "after" | "before",
  item: { createdAt: string; id: string },
): string {
  return btoa(`${direction}\u0000${item.createdAt}\u0000${item.id}`)
    .replaceAll("+", "-")
    .replaceAll("/", "_")
    .replace(/=+$/, "");
}

function decodeCursor(value: string | null): {
  direction: "after" | "before";
  createdAt: string;
  id: string;
} | null {
  if (value === null) return null;
  if (value.length > 256) return null;
  try {
    const normalized = value.replaceAll("-", "+").replaceAll("_", "/");
    const decoded = atob(normalized + "=".repeat((4 - normalized.length % 4) % 4));
    const [direction, createdAt, id, extra] = decoded.split("\u0000");
    return extra === undefined
      && (direction === "after" || direction === "before")
      && /^\d{4}-\d{2}-\d{2} /.test(createdAt ?? "")
      && isId(id ?? "")
      ? { direction, createdAt: createdAt!, id: id! }
      : null;
  } catch {
    return null;
  }
}

type StoredCatalogItem = {
  kind: "project" | "session";
  id: string;
  name: string;
  status: "running" | "waiting" | "completed" | "failed" | null;
  createdAt: string;
};

const LIST_ITEMS_AFTER_SQL = `
  SELECT kind, id, name, status, createdAt
  FROM (
    SELECT
      'project' AS kind,
      id,
      name,
      NULL AS status,
      created_at AS createdAt
    FROM session_projects
    WHERE workspace_id = ?
      AND github_repository_id = ?
      AND parent_project_id IS ?
    UNION ALL
    SELECT
      'session' AS kind,
      id,
      name,
      CASE (
        SELECT status FROM session_turns
        WHERE session_id = durable_sessions.id
        ORDER BY ordinal DESC LIMIT 1
      )
        WHEN 'running' THEN 'running'
        WHEN 'finalizing' THEN 'waiting'
        WHEN 'failed' THEN 'failed'
        WHEN 'interrupted' THEN 'failed'
        ELSE CASE WHEN current_revision IS NULL THEN 'failed' ELSE 'completed' END
      END AS status,
      created_at AS createdAt
    FROM durable_sessions
    WHERE workspace_id = ?
      AND github_repository_id = ?
      AND project_id IS ?
      AND deleting = 0
  )
  WHERE ? IS NULL OR createdAt > ? OR (createdAt = ? AND id > ?)
  ORDER BY createdAt, id
  LIMIT ?
`;

const LIST_ITEMS_BEFORE_SQL = `
  SELECT kind, id, name, status, createdAt
  FROM (
    SELECT
      'project' AS kind,
      id,
      name,
      NULL AS status,
      created_at AS createdAt
    FROM session_projects
    WHERE workspace_id = ?
      AND github_repository_id = ?
      AND parent_project_id IS ?
    UNION ALL
    SELECT
      'session' AS kind,
      id,
      name,
      CASE (
        SELECT status FROM session_turns
        WHERE session_id = durable_sessions.id
        ORDER BY ordinal DESC LIMIT 1
      )
        WHEN 'running' THEN 'running'
        WHEN 'finalizing' THEN 'waiting'
        WHEN 'failed' THEN 'failed'
        WHEN 'interrupted' THEN 'failed'
        ELSE CASE WHEN current_revision IS NULL THEN 'failed' ELSE 'completed' END
      END AS status,
      created_at AS createdAt
    FROM durable_sessions
    WHERE workspace_id = ?
      AND github_repository_id = ?
      AND project_id IS ?
      AND deleting = 0
  )
  WHERE ? IS NULL OR createdAt < ? OR (createdAt = ? AND id < ?)
  ORDER BY createdAt DESC, id DESC
  LIMIT ?
`;

const INSERT_PROJECT_SQL = `
  INSERT INTO session_projects (
    id, workspace_id, github_repository_id, parent_project_id, name
  ) VALUES (?, ?, ?, ?, ?)
`;

const FIND_PROJECT_SQL = `
  SELECT
    id,
    workspace_id AS workspaceId,
    github_repository_id AS githubRepositoryId,
    parent_project_id AS parentProjectId,
    name,
    instructions,
    version
  FROM session_projects
  WHERE id = ? AND workspace_id = ? AND github_repository_id = ?
`;

const UPDATE_PROJECT_SQL = `
  UPDATE session_projects
  SET
    name = COALESCE(?, name),
    instructions = COALESCE(?, instructions),
    parent_project_id = CASE WHEN ? = 1 THEN ? ELSE parent_project_id END,
    version = version + 1,
    updated_at = CURRENT_TIMESTAMP
  WHERE (? IS NULL OR version = ?)
    AND id = ? AND workspace_id = ? AND github_repository_id = ?
`;

const MOVE_SESSION_SQL = `
  UPDATE durable_sessions
  SET project_id = ?, updated_at = CURRENT_TIMESTAMP
  WHERE id = ?
    AND workspace_id = ?
    AND github_repository_id = ?
    AND controller_user_id = ?
    AND deleting = 0
    AND (
      ? IS NULL OR EXISTS (
        SELECT 1 FROM session_projects
        WHERE id = ?
          AND workspace_id = ?
          AND github_repository_id = ?
      )
    )
`;

const FIND_SESSION_OWNER_SQL = `
  SELECT controller_user_id AS controllerUserId
  FROM durable_sessions
  WHERE id = ? AND workspace_id = ? AND github_repository_id = ? AND deleting = 0
`;

const PROJECT_INSTRUCTION_CHAIN_SQL = `
  WITH RECURSIVE chain(id, parent_project_id, name, instructions, depth) AS (
    SELECT id, parent_project_id, name, instructions, 1
    FROM session_projects
    WHERE id = ? AND workspace_id = ? AND github_repository_id = ?
    UNION ALL
    SELECT projects.id, projects.parent_project_id, projects.name,
      projects.instructions, chain.depth + 1
    FROM session_projects AS projects
    JOIN chain ON projects.id = chain.parent_project_id
    WHERE chain.depth < 12
  )
  SELECT name, instructions, depth FROM chain
`;
