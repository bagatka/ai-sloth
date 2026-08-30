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
  current_revision INTEGER NOT NULL CHECK (current_revision > 0),
  current_commit_sha TEXT NOT NULL,
  published_revision INTEGER,
  published_commit_sha TEXT,
  pull_request_number INTEGER,
  pull_request_url TEXT,
  deleting INTEGER NOT NULL DEFAULT 0 CHECK (deleting IN (0, 1)),
  created_at TEXT NOT NULL DEFAULT CURRENT_TIMESTAMP,
  updated_at TEXT NOT NULL DEFAULT CURRENT_TIMESTAMP,
  CHECK (
    (published_revision IS NULL AND published_commit_sha IS NULL)
    OR (published_revision IS NOT NULL AND published_commit_sha IS NOT NULL)
  )
);

CREATE INDEX durable_sessions_by_workspace
  ON durable_sessions(workspace_id, created_at DESC);

CREATE TRIGGER durable_sessions_limit_controller
BEFORE INSERT ON durable_sessions
WHEN (
  SELECT COUNT(*) FROM durable_sessions
  WHERE controller_user_id = NEW.controller_user_id
) >= 20
BEGIN
  SELECT RAISE(ABORT, 'controller session limit reached');
END;

CREATE UNIQUE INDEX durable_sessions_by_publication_branch
  ON durable_sessions(github_repository_id, publication_branch);

CREATE TABLE durable_session_revisions (
  session_id TEXT NOT NULL,
  revision INTEGER NOT NULL CHECK (revision > 0),
  commit_sha TEXT NOT NULL,
  git_object_key TEXT NOT NULL UNIQUE,
  git_size INTEGER NOT NULL CHECK (git_size > 0),
  pi_object_key TEXT NOT NULL UNIQUE,
  pi_size INTEGER NOT NULL CHECK (pi_size > 0),
  created_at TEXT NOT NULL DEFAULT CURRENT_TIMESTAMP,
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
  created_at INTEGER NOT NULL
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
