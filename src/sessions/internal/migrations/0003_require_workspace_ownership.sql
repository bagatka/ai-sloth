CREATE TRIGGER sessions_require_workspace_ownership
BEFORE INSERT ON sessions
WHEN NEW.workspace_id IS NULL OR NEW.created_by_user_id IS NULL
BEGIN
  SELECT RAISE(ABORT, 'session workspace ownership is required');
END;
