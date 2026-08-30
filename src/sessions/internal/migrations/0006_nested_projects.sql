CREATE TABLE session_projects (
  id TEXT PRIMARY KEY,
  workspace_id TEXT NOT NULL,
  github_repository_id TEXT NOT NULL,
  parent_project_id TEXT,
  name TEXT NOT NULL CHECK (length(name) BETWEEN 1 AND 100),
  instructions TEXT NOT NULL DEFAULT '' CHECK (length(instructions) <= 16384),
  version INTEGER NOT NULL DEFAULT 1 CHECK (version > 0),
  created_at TEXT NOT NULL DEFAULT CURRENT_TIMESTAMP,
  updated_at TEXT NOT NULL DEFAULT CURRENT_TIMESTAMP,
  FOREIGN KEY (parent_project_id) REFERENCES session_projects(id)
);

CREATE INDEX session_projects_by_parent
  ON session_projects(
    workspace_id,
    github_repository_id,
    parent_project_id,
    created_at,
    id
  );

CREATE TRIGGER session_projects_limit_workspace
BEFORE INSERT ON session_projects
WHEN (
  SELECT COUNT(*) FROM session_projects
  WHERE workspace_id = NEW.workspace_id
) >= 500
BEGIN
  SELECT RAISE(ABORT, 'workspace project limit reached');
END;

CREATE TRIGGER session_projects_validate_parent_insert
BEFORE INSERT ON session_projects
WHEN NEW.parent_project_id IS NOT NULL
BEGIN
  SELECT CASE WHEN NOT EXISTS (
    SELECT 1 FROM session_projects
    WHERE id = NEW.parent_project_id
      AND workspace_id = NEW.workspace_id
      AND github_repository_id = NEW.github_repository_id
  ) THEN RAISE(ABORT, 'invalid project parent') END;
  SELECT CASE WHEN (
    WITH RECURSIVE ancestors(id, parent_project_id, depth) AS (
      SELECT id, parent_project_id, 1 FROM session_projects
      WHERE id = NEW.parent_project_id
      UNION ALL
      SELECT projects.id, projects.parent_project_id, ancestors.depth + 1
      FROM session_projects AS projects
      JOIN ancestors ON projects.id = ancestors.parent_project_id
      WHERE ancestors.depth < 12
    )
    SELECT MAX(depth) FROM ancestors
  ) >= 12 THEN RAISE(ABORT, 'project nesting limit reached') END;
END;

CREATE TRIGGER session_projects_validate_parent_update
BEFORE UPDATE OF parent_project_id ON session_projects
WHEN NEW.parent_project_id IS NOT OLD.parent_project_id
BEGIN
  SELECT CASE WHEN NEW.parent_project_id IS NOT NULL AND NOT EXISTS (
    SELECT 1 FROM session_projects
    WHERE id = NEW.parent_project_id
      AND workspace_id = NEW.workspace_id
      AND github_repository_id = NEW.github_repository_id
  ) THEN RAISE(ABORT, 'invalid project parent') END;
  SELECT CASE WHEN NEW.parent_project_id IS NOT NULL AND EXISTS (
    WITH RECURSIVE ancestors(id, parent_project_id, depth) AS (
      SELECT id, parent_project_id, 1 FROM session_projects
      WHERE id = NEW.parent_project_id
      UNION ALL
      SELECT projects.id, projects.parent_project_id, ancestors.depth + 1
      FROM session_projects AS projects
      JOIN ancestors ON projects.id = ancestors.parent_project_id
      WHERE ancestors.depth < 12
    )
    SELECT 1 FROM ancestors WHERE id = NEW.id
  ) THEN RAISE(ABORT, 'project cycle') END;
  SELECT CASE WHEN (
    COALESCE((
      WITH RECURSIVE ancestors(id, parent_project_id, depth) AS (
        SELECT id, parent_project_id, 1 FROM session_projects
        WHERE id = NEW.parent_project_id
        UNION ALL
        SELECT projects.id, projects.parent_project_id, ancestors.depth + 1
        FROM session_projects AS projects
        JOIN ancestors ON projects.id = ancestors.parent_project_id
        WHERE ancestors.depth < 12
      )
      SELECT MAX(depth) FROM ancestors
    ), 0)
    + COALESCE((
      WITH RECURSIVE descendants(id, depth) AS (
        SELECT NEW.id, 1
        UNION ALL
        SELECT projects.id, descendants.depth + 1
        FROM session_projects AS projects
        JOIN descendants ON projects.parent_project_id = descendants.id
        WHERE descendants.depth < 12
      )
      SELECT MAX(depth) FROM descendants
    ), 1)
  ) > 12 THEN RAISE(ABORT, 'project nesting limit reached') END;
END;

ALTER TABLE durable_sessions
  ADD COLUMN name TEXT NOT NULL DEFAULT 'Untitled session'
  CHECK (length(name) BETWEEN 1 AND 100);
ALTER TABLE durable_sessions
  ADD COLUMN project_id TEXT REFERENCES session_projects(id);

CREATE INDEX durable_sessions_by_project
  ON durable_sessions(
    workspace_id,
    github_repository_id,
    project_id,
    created_at,
    id
  )
  WHERE deleting = 0;

ALTER TABLE durable_session_revisions
  ADD COLUMN project_instructions TEXT NOT NULL DEFAULT '';

ALTER TABLE durable_revision_attempts
  ADD COLUMN name TEXT NOT NULL DEFAULT 'Untitled session';
ALTER TABLE durable_revision_attempts
  ADD COLUMN project_id TEXT;
ALTER TABLE durable_revision_attempts
  ADD COLUMN project_instructions TEXT NOT NULL DEFAULT '';
