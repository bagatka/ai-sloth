CREATE TABLE sessions (
  id TEXT PRIMARY KEY,
  repository_url TEXT NOT NULL,
  commit_sha TEXT NOT NULL,
  created_at TEXT NOT NULL DEFAULT CURRENT_TIMESTAMP,
  workspace_id TEXT,
  created_by_user_id TEXT,
  github_repository_id TEXT,
  github_user_id TEXT,
  base_ref TEXT,
  session_branch TEXT,
  pull_request_number INTEGER,
  pull_request_url TEXT
);

CREATE INDEX sessions_by_workspace
  ON sessions(workspace_id, created_at DESC);

CREATE UNIQUE INDEX sessions_by_github_branch
  ON sessions(github_repository_id, session_branch)
  WHERE github_repository_id IS NOT NULL AND session_branch IS NOT NULL;

CREATE TRIGGER sessions_require_workspace_ownership
BEFORE INSERT ON sessions
WHEN NEW.workspace_id IS NULL OR NEW.created_by_user_id IS NULL
BEGIN
  SELECT RAISE(ABORT, 'session workspace ownership is required');
END;

CREATE TRIGGER sessions_require_github_source
BEFORE INSERT ON sessions
WHEN NEW.github_repository_id IS NULL OR NEW.github_user_id IS NULL OR NEW.base_ref IS NULL OR NEW.session_branch IS NULL
BEGIN
  SELECT RAISE(ABORT, 'session GitHub source is required');
END;

CREATE TABLE session_snapshots (
  session_id TEXT NOT NULL,
  revision INTEGER NOT NULL CHECK (revision > 0),
  object_key TEXT NOT NULL UNIQUE,
  created_at TEXT NOT NULL DEFAULT CURRENT_TIMESTAMP,
  PRIMARY KEY (session_id, revision),
  FOREIGN KEY (session_id) REFERENCES sessions(id)
);

CREATE TABLE session_publications (
  session_id TEXT PRIMARY KEY,
  workspace_id TEXT NOT NULL,
  controller_user_id TEXT NOT NULL,
  github_repository_id TEXT NOT NULL,
  github_user_id TEXT NOT NULL,
  repository_url TEXT NOT NULL,
  base_ref TEXT NOT NULL,
  session_branch TEXT NOT NULL,
  revision INTEGER NOT NULL CHECK (revision > 0),
  expected_commit_sha TEXT NOT NULL,
  commit_sha TEXT NOT NULL,
  object_key TEXT NOT NULL UNIQUE,
  created_at TEXT NOT NULL DEFAULT CURRENT_TIMESTAMP
);

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

CREATE TABLE durable_sessions (
  id TEXT PRIMARY KEY,
  workspace_id TEXT NOT NULL,
  controller_user_id TEXT NOT NULL,
  github_repository_id TEXT NOT NULL,
  github_user_id TEXT NOT NULL,
  repository_url TEXT NOT NULL,
  base_ref TEXT NOT NULL,
  base_commit_sha TEXT NOT NULL,
  publication_branch TEXT NOT NULL,
  current_revision INTEGER CHECK (current_revision > 0),
  current_commit_sha TEXT,
  published_revision INTEGER,
  published_commit_sha TEXT,
  pull_request_number INTEGER,
  pull_request_url TEXT,
  deleting INTEGER NOT NULL DEFAULT 0 CHECK (deleting IN (0, 1)),
  created_at TEXT NOT NULL DEFAULT CURRENT_TIMESTAMP,
  updated_at TEXT NOT NULL DEFAULT CURRENT_TIMESTAMP,
  name TEXT NOT NULL DEFAULT 'Untitled session'
    CHECK (length(name) BETWEEN 1 AND 100),
  project_id TEXT REFERENCES session_projects(id),
  CHECK (
    (current_revision IS NULL AND current_commit_sha IS NULL)
    OR (current_revision IS NOT NULL AND current_commit_sha IS NOT NULL)
  ),
  CHECK (
    (published_revision IS NULL AND published_commit_sha IS NULL)
    OR (published_revision IS NOT NULL AND published_commit_sha IS NOT NULL)
  )
);

CREATE INDEX durable_sessions_by_workspace
  ON durable_sessions(workspace_id, created_at DESC);

CREATE UNIQUE INDEX durable_sessions_by_publication_branch
  ON durable_sessions(github_repository_id, publication_branch);

CREATE INDEX durable_sessions_by_project
  ON durable_sessions(
    workspace_id,
    github_repository_id,
    project_id,
    created_at,
    id
  )
  WHERE deleting = 0;

CREATE TRIGGER durable_sessions_limit_controller
BEFORE INSERT ON durable_sessions
WHEN (
  SELECT COUNT(*) FROM durable_sessions
  WHERE controller_user_id = NEW.controller_user_id
) >= 20
BEGIN
  SELECT RAISE(ABORT, 'controller session limit reached');
END;

CREATE TABLE durable_session_revisions (
  session_id TEXT NOT NULL,
  revision INTEGER NOT NULL CHECK (revision > 0),
  commit_sha TEXT NOT NULL,
  git_object_key TEXT NOT NULL UNIQUE,
  git_size INTEGER NOT NULL CHECK (git_size > 0),
  pi_object_key TEXT NOT NULL UNIQUE,
  pi_size INTEGER NOT NULL CHECK (pi_size > 0),
  created_at TEXT NOT NULL DEFAULT CURRENT_TIMESTAMP,
  project_instructions TEXT NOT NULL DEFAULT '',
  PRIMARY KEY (session_id, revision),
  FOREIGN KEY (session_id) REFERENCES durable_sessions(id)
);

CREATE TABLE durable_revision_attempts (
  session_id TEXT PRIMARY KEY,
  workspace_id TEXT NOT NULL,
  controller_user_id TEXT NOT NULL,
  github_repository_id TEXT NOT NULL,
  github_user_id TEXT NOT NULL,
  repository_url TEXT NOT NULL,
  base_ref TEXT NOT NULL,
  base_commit_sha TEXT NOT NULL,
  publication_branch TEXT NOT NULL,
  revision INTEGER NOT NULL CHECK (revision > 0),
  expected_commit_sha TEXT NOT NULL,
  git_object_key TEXT NOT NULL UNIQUE,
  pi_object_key TEXT NOT NULL UNIQUE,
  created_at INTEGER NOT NULL,
  name TEXT NOT NULL DEFAULT 'Untitled session',
  project_id TEXT,
  project_instructions TEXT NOT NULL DEFAULT '',
  turn_id TEXT,
  transcript_object_key TEXT
);

CREATE TABLE session_hot_backups (
  session_id TEXT PRIMARY KEY,
  revision INTEGER NOT NULL CHECK (revision > 0),
  backup_id TEXT NOT NULL,
  local INTEGER NOT NULL CHECK (local IN (0, 1)),
  expires_at INTEGER NOT NULL,
  FOREIGN KEY (session_id) REFERENCES durable_sessions(id)
);

CREATE TABLE project_sandbox_backups (
  controller_user_id TEXT NOT NULL,
  github_repository_id TEXT NOT NULL,
  base_commit_sha TEXT NOT NULL,
  image_version TEXT NOT NULL,
  backup_id TEXT NOT NULL,
  local INTEGER NOT NULL CHECK (local IN (0, 1)),
  expires_at INTEGER NOT NULL,
  last_used_at INTEGER NOT NULL,
  PRIMARY KEY (
    controller_user_id,
    github_repository_id,
    base_commit_sha,
    image_version
  )
);

CREATE INDEX project_sandbox_backups_by_project
  ON project_sandbox_backups(
    controller_user_id,
    github_repository_id,
    image_version,
    last_used_at DESC
  );

CREATE TABLE session_turns (
  id TEXT PRIMARY KEY,
  session_id TEXT NOT NULL,
  ordinal INTEGER NOT NULL CHECK (ordinal > 0 AND ordinal <= 100),
  idempotency_key TEXT NOT NULL UNIQUE,
  status TEXT NOT NULL CHECK (
    status IN ('running', 'finalizing', 'succeeded', 'failed', 'interrupted')
  ),
  failure_code TEXT,
  result_revision INTEGER,
  transcript_object_key TEXT NOT NULL UNIQUE,
  transcript_size INTEGER CHECK (transcript_size >= 0),
  last_event_sequence INTEGER CHECK (last_event_sequence >= 0),
  created_at TEXT NOT NULL DEFAULT CURRENT_TIMESTAMP,
  completed_at TEXT,
  UNIQUE (session_id, ordinal),
  FOREIGN KEY (session_id) REFERENCES durable_sessions(id)
);

CREATE INDEX session_turns_by_session
  ON session_turns(session_id, ordinal DESC);
