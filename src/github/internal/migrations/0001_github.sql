CREATE TABLE github_connection_flows (
  state_hash TEXT PRIMARY KEY,
  user_id TEXT NOT NULL,
  callback_url TEXT NOT NULL,
  code_verifier TEXT NOT NULL,
  expires_at INTEGER NOT NULL,
  created_at INTEGER NOT NULL
);

CREATE INDEX github_connection_flows_by_user
  ON github_connection_flows(user_id, created_at DESC);

CREATE TABLE github_connections (
  user_id TEXT PRIMARY KEY,
  github_user_id TEXT NOT NULL UNIQUE,
  login TEXT NOT NULL,
  access_token TEXT NOT NULL,
  access_token_expires_at INTEGER NOT NULL,
  refresh_token TEXT,
  refresh_token_expires_at INTEGER,
  created_at TEXT NOT NULL DEFAULT CURRENT_TIMESTAMP,
  updated_at TEXT NOT NULL DEFAULT CURRENT_TIMESTAMP,
  CHECK (
    (refresh_token IS NULL AND refresh_token_expires_at IS NULL)
    OR (refresh_token IS NOT NULL AND refresh_token_expires_at IS NOT NULL)
  )
);
