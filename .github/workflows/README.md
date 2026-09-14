# Workflow conventions

- Workflow names: `<scope> / <operation> [/ <environment>]`; scope identifies a component, artifact, or solution.
- Files: `<scope>-<operation>.yml`, kebab-case.
- Jobs: explicit, scoped `name` values describing the work.
- Run names: workflow name plus PR number, branch, or deployment environment.
- Keep validation, publishing, and deployment separate.
