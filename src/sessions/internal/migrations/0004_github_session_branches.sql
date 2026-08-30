ALTER TABLE sessions ADD COLUMN github_repository_id TEXT;
ALTER TABLE sessions ADD COLUMN github_user_id TEXT;
ALTER TABLE sessions ADD COLUMN base_ref TEXT;
ALTER TABLE sessions ADD COLUMN session_branch TEXT;
ALTER TABLE sessions ADD COLUMN pull_request_number INTEGER;
ALTER TABLE sessions ADD COLUMN pull_request_url TEXT;

CREATE UNIQUE INDEX sessions_by_github_branch
  ON sessions(github_repository_id, session_branch)
  WHERE github_repository_id IS NOT NULL AND session_branch IS NOT NULL;

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

CREATE TRIGGER sessions_require_github_source
BEFORE INSERT ON sessions
WHEN NEW.github_repository_id IS NULL OR NEW.github_user_id IS NULL OR NEW.base_ref IS NULL OR NEW.session_branch IS NULL
BEGIN
  SELECT RAISE(ABORT, 'session GitHub source is required');
END;
