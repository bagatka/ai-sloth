ALTER TABLE sessions ADD COLUMN workspace_id TEXT;
ALTER TABLE sessions ADD COLUMN created_by_user_id TEXT;

CREATE INDEX sessions_by_workspace
  ON sessions(workspace_id, created_at DESC);
