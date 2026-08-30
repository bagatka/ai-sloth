CREATE TABLE next_durable_sessions (
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

INSERT INTO next_durable_sessions SELECT * FROM durable_sessions;

CREATE TABLE next_durable_session_revisions (
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
  FOREIGN KEY (session_id) REFERENCES next_durable_sessions(id)
);

INSERT INTO next_durable_session_revisions
SELECT * FROM durable_session_revisions;

CREATE TABLE next_session_hot_backups (
  session_id TEXT PRIMARY KEY,
  revision INTEGER NOT NULL CHECK (revision > 0),
  backup_id TEXT NOT NULL,
  local INTEGER NOT NULL CHECK (local IN (0, 1)),
  expires_at INTEGER NOT NULL,
  FOREIGN KEY (session_id) REFERENCES next_durable_sessions(id)
);

INSERT INTO next_session_hot_backups SELECT * FROM session_hot_backups;

DROP TABLE session_hot_backups;
DROP TABLE durable_session_revisions;
DROP TABLE durable_sessions;

ALTER TABLE next_durable_sessions RENAME TO durable_sessions;
ALTER TABLE next_durable_session_revisions RENAME TO durable_session_revisions;
ALTER TABLE next_session_hot_backups RENAME TO session_hot_backups;

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

CREATE INDEX durable_sessions_by_project
  ON durable_sessions(
    workspace_id,
    github_repository_id,
    project_id,
    created_at,
    id
  )
  WHERE deleting = 0;

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

ALTER TABLE durable_revision_attempts ADD COLUMN turn_id TEXT;
ALTER TABLE durable_revision_attempts ADD COLUMN transcript_object_key TEXT;
