CREATE TABLE sessions (
  id TEXT PRIMARY KEY,
  repository_url TEXT NOT NULL,
  commit_sha TEXT NOT NULL,
  created_at TEXT NOT NULL DEFAULT CURRENT_TIMESTAMP
);

CREATE TABLE session_snapshots (
  session_id TEXT NOT NULL,
  revision INTEGER NOT NULL CHECK (revision > 0),
  object_key TEXT NOT NULL UNIQUE,
  created_at TEXT NOT NULL DEFAULT CURRENT_TIMESTAMP,
  PRIMARY KEY (session_id, revision),
  FOREIGN KEY (session_id) REFERENCES sessions(id)
);
