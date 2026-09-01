export function isUnifiedPatch(value: string): boolean {
  return (
    /^---\s.+$/m.test(value) &&
    /^\+\+\+\s.+$/m.test(value) &&
    /^@@\s/m.test(value)
  )
}
