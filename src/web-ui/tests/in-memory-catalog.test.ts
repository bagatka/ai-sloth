import { expect, test } from "bun:test"
import { createInMemoryRepositoryCatalog } from "../src/repository-navigation"

const WORKSPACE_ID = "a47f6e35-b7f3-4c6f-91f6-93f0479ec15b"
const active = () => new AbortController().signal

test("repository catalog replaces bounded repository pages", async () => {
  const catalog = createInMemoryRepositoryCatalog()
  const first = await catalog.listRepositories(WORKSPACE_ID, null, active())

  expect(first.items).toHaveLength(10)
  expect(first.previousCursor).toBeNull()
  expect(first.nextCursor).not.toBeNull()

  const second = await catalog.listRepositories(
    WORKSPACE_ID,
    first.nextCursor,
    active()
  )
  expect(second.items).toHaveLength(10)
  expect(second.previousCursor).not.toBeNull()
  expect(second.items.map(({ id }) => id)).not.toEqual(
    first.items.map(({ id }) => id)
  )
})

test("repository projects and sessions are independently paginated", async () => {
  const catalog = createInMemoryRepositoryCatalog()
  const repositories = await catalog.listRepositories(
    WORKSPACE_ID,
    null,
    active()
  )
  const repository = repositories.items[0]!
  const items = await catalog.listRepositoryItems(
    WORKSPACE_ID,
    repository.id,
    null,
    active()
  )
  const project = items.items.find((item) => item.kind === "project")

  expect(items.items.length).toBeLessThanOrEqual(8)
  expect(project).toBeDefined()
  if (!project || project.kind !== "project") return

  const projectItems = await catalog.listProjectItems(
    WORKSPACE_ID,
    repository.id,
    project.id,
    null,
    active()
  )
  expect(projectItems.items.length).toBeLessThanOrEqual(8)
  expect(projectItems.nextCursor).not.toBeNull()
})

test("project tree items can be created, nested, and moved out", async () => {
  const catalog = createInMemoryRepositoryCatalog()
  const repositories = await catalog.listRepositories(
    WORKSPACE_ID,
    null,
    active()
  )
  const repository = repositories.items[0]!
  const root = await catalog.listRepositoryItems(
    WORKSPACE_ID,
    repository.id,
    null,
    active()
  )
  const projects = root.items.filter((item) => item.kind === "project")
  const project = projects[0]!
  const secondProject = projects[1]!
  const session = root.items.find((item) => item.kind === "session")!

  await catalog.moveItem(
    WORKSPACE_ID,
    repository.id,
    { kind: secondProject.kind, id: secondProject.id },
    project.id,
    active()
  )
  let nested = await catalog.listProjectItems(
    WORKSPACE_ID,
    repository.id,
    project.id,
    null,
    active()
  )
  expect(nested.items[0]?.id).toBe(secondProject.id)

  await catalog.moveItem(
    WORKSPACE_ID,
    repository.id,
    { kind: secondProject.kind, id: secondProject.id },
    null,
    active()
  )

  await catalog.moveItem(
    WORKSPACE_ID,
    repository.id,
    { kind: session.kind, id: session.id },
    project.id,
    active()
  )
  nested = await catalog.listProjectItems(
    WORKSPACE_ID,
    repository.id,
    project.id,
    null,
    active()
  )
  expect(nested.items[0]?.id).toBe(session.id)

  await catalog.moveItem(
    WORKSPACE_ID,
    repository.id,
    { kind: session.kind, id: session.id },
    null,
    active()
  )
  const movedOut = await catalog.listRepositoryItems(
    WORKSPACE_ID,
    repository.id,
    null,
    active()
  )
  expect(movedOut.items[0]?.id).toBe(session.id)

  await catalog.createItem(
    WORKSPACE_ID,
    repository.id,
    project.id,
    { kind: "project", name: "Nested project" },
    active()
  )
  nested = await catalog.listProjectItems(
    WORKSPACE_ID,
    repository.id,
    project.id,
    null,
    active()
  )
  const child = nested.items[0]!
  expect(child).toMatchObject({ kind: "project", name: "Nested project" })

  await expect(
    catalog.moveItem(
      WORKSPACE_ID,
      repository.id,
      { kind: project.kind, id: project.id },
      child.id,
      active()
    )
  ).rejects.toThrow("cannot be moved into itself")
})
