const MAX_WORKSPACE_NAME_LENGTH = 100;

export function normalizeWorkspaceName(value: string): string | null {
  const name = value.trim().normalize("NFC");
  if (
    name.length === 0
    || name.length > MAX_WORKSPACE_NAME_LENGTH
    || /[\u0000-\u001f\u007f]/.test(name)
  ) {
    return null;
  }
  return name;
}

export function isId(value: string): boolean {
  return /^[0-9a-f]{8}-[0-9a-f]{4}-4[0-9a-f]{3}-[89ab][0-9a-f]{3}-[0-9a-f]{12}$/.test(
    value,
  );
}
