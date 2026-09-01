import type {
  Page,
  Project,
  RepositoryItem,
  Session,
  RepositoryCatalog,
} from ".."

const REPOSITORIES_PER_PAGE = 10
const ITEMS_PER_PAGE = 8
const REPOSITORY_NAMES = [
  "platform",
  "web-console",
  "mobile-app",
  "design-system",
  "developer-docs",
  "billing",
  "observability",
  "edge-runtime",
  "customer-api",
  "infrastructure",
  "release-tools",
  "experiments",
]
const PROJECT_NAMES = [
  "Authentication refresh",
  "Performance pass",
  "Dependency upgrades",
  "Release readiness",
  "Accessibility review",
]
const SESSION_NAMES = [
  "Investigate failing tests",
  "Review the latest changes",
  "Improve error handling",
  "Trace the slow request",
  "Document the public API",
  "Simplify the data flow",
]

export function createInMemoryRepositoryCatalog(): RepositoryCatalog {
  const collections = new Map<string, RepositoryItem[]>()
  const projectParents = new Map<string, string | null>()
  const projectDepths = new Map<string, number>()
  const projectInstructions = new Map<string, string>()
  const projectVersions = new Map<string, number>()
  let createdItems = 0

  function rootItems(workspaceId: string, repositoryId: string) {
    const key = collectionKey(repositoryId, null)
    const existing = collections.get(key)
    if (existing) return existing

    const repositoryIndex = stableSeed(repositoryId)
    const items = Array.from({ length: 18 }, (_, index): RepositoryItem => {
      if (index % 3 === 0) {
        return session(
          `${workspaceId}:${repositoryIndex}:session:${index}`,
          SESSION_NAMES[(repositoryIndex + index) % SESSION_NAMES.length],
          repositoryIndex + index
        )
      }
      const project = projectItem(
        `${workspaceId}:${repositoryIndex}:project:${index}`,
        PROJECT_NAMES[(repositoryIndex + index) % PROJECT_NAMES.length]
      )
      projectParents.set(project.id, null)
      projectDepths.set(project.id, 0)
      return project
    })
    collections.set(key, items)
    return items
  }

  function projectItems(
    workspaceId: string,
    repositoryId: string,
    projectId: string
  ) {
    const key = collectionKey(repositoryId, projectId)
    const existing = collections.get(key)
    if (existing) return existing
    if (!projectParents.has(projectId)) throw new Error("Project not found")

    const repositoryIndex = stableSeed(repositoryId)
    const projectIndex = readSeed(projectId)
    const depth = projectDepths.get(projectId) ?? 0
    const items = Array.from({ length: 17 }, (_, index): RepositoryItem => {
      if (depth < 2 && index > 0 && index % 6 === 0) {
        const child = projectItem(
          `${projectId}:project:${index}`,
          PROJECT_NAMES[(projectIndex + index) % PROJECT_NAMES.length]
        )
        projectParents.set(child.id, projectId)
        projectDepths.set(child.id, depth + 1)
        return child
      }
      return session(
        `${workspaceId}:${repositoryIndex}:${projectIndex}:session:${index}`,
        `${SESSION_NAMES[(projectIndex + index) % SESSION_NAMES.length]} #${index + 1}`,
        repositoryIndex + projectIndex + index
      )
    })
    collections.set(key, items)
    return items
  }

  function itemsFor(
    workspaceId: string,
    repositoryId: string,
    parentProjectId: string | null
  ) {
    return parentProjectId
      ? projectItems(workspaceId, repositoryId, parentProjectId)
      : rootItems(workspaceId, repositoryId)
  }

  return {
    async listRepositories(workspaceId, cursor, signal) {
      signal.throwIfAborted()
      const repositories = Array.from({ length: 120 }, (_, index) => {
        const baseName = REPOSITORY_NAMES[index % REPOSITORY_NAMES.length]
        const sequence = Math.floor(index / REPOSITORY_NAMES.length)
        return {
          id: `${workspaceId}:repository:${index}`,
          name: sequence === 0 ? baseName : `${baseName}-${sequence + 1}`,
          owner: index % 3 === 0 ? "ai-sloth" : "team",
          defaultBranch: "main",
        }
      })
      return page(repositories, cursor, REPOSITORIES_PER_PAGE)
    },

    async listRepositoryItems(workspaceId, repositoryId, cursor, signal) {
      signal.throwIfAborted()
      return page(rootItems(workspaceId, repositoryId), cursor, ITEMS_PER_PAGE)
    },

    async listProjectItems(
      workspaceId,
      repositoryId,
      projectId,
      cursor,
      signal
    ) {
      signal.throwIfAborted()
      return page(
        projectItems(workspaceId, repositoryId, projectId),
        cursor,
        ITEMS_PER_PAGE
      )
    },

    async createItem(
      workspaceId,
      repositoryId,
      parentProjectId,
      input,
      signal
    ) {
      signal.throwIfAborted()
      const normalizedName = input.name.trim()
      if (!normalizedName || normalizedName.length > 100) {
        throw new Error("Item name must contain 1 to 100 characters")
      }

      createdItems += 1
      const id = `${workspaceId}:${repositoryId}:created:${createdItems}`
      const item =
        input.kind === "project"
          ? projectItem(id, normalizedName)
          : session(id, normalizedName, createdItems)
      if (item.kind === "project") {
        projectParents.set(item.id, parentProjectId)
        projectDepths.set(
          item.id,
          parentProjectId ? (projectDepths.get(parentProjectId) ?? 0) + 1 : 0
        )
      }
      itemsFor(workspaceId, repositoryId, parentProjectId).unshift(item)
      return item.kind === "session" ? item : null
    },

    async moveItem(
      workspaceId,
      repositoryId,
      itemIdentity,
      targetProjectId,
      signal
    ) {
      signal.throwIfAborted()
      const sourceProjectId =
        itemIdentity.kind === "project"
          ? (projectParents.get(itemIdentity.id) ?? null)
          : findSessionParent(collections, repositoryId, itemIdentity.id)
      if (sourceProjectId === targetProjectId) return

      const source = itemsFor(workspaceId, repositoryId, sourceProjectId)
      const index = source.findIndex((item) => item.id === itemIdentity.id)
      if (index < 0) throw new Error("Project tree item not found")
      const item = source[index]!
      if (item.kind === "project") {
        assertValidProjectMove(item.id, targetProjectId, projectParents)
      }

      const target = itemsFor(workspaceId, repositoryId, targetProjectId)
      source.splice(index, 1)
      target.unshift(item)
      if (item.kind === "project") {
        projectParents.set(item.id, targetProjectId)
        projectVersions.set(item.id, (projectVersions.get(item.id) ?? 1) + 1)
        updateDepths(item.id, targetProjectId, projectParents, projectDepths)
      }
    },

    async getProject(_workspaceId, _repositoryId, projectId, signal) {
      signal.throwIfAborted()
      const project = findProject(collections, projectId)
      if (!project) throw new Error("Project not found")
      return {
        ...project,
        parentProjectId: projectParents.get(projectId) ?? null,
        instructions: projectInstructions.get(projectId) ?? "",
        version: projectVersions.get(projectId) ?? 1,
      }
    },

    async updateProject(_workspaceId, _repositoryId, projectId, input, signal) {
      signal.throwIfAborted()
      const project = findProject(collections, projectId)
      if (!project) throw new Error("Project not found")
      const version = projectVersions.get(projectId) ?? 1
      if (input.expectedVersion !== version) {
        throw new Error("Project changed before it could be saved")
      }
      project.name = input.name.trim()
      projectInstructions.set(projectId, input.instructions)
      projectVersions.set(projectId, version + 1)
      return {
        ...project,
        parentProjectId: projectParents.get(projectId) ?? null,
        instructions: input.instructions,
        version: version + 1,
      }
    },
  }
}

function findSessionParent(
  collections: ReadonlyMap<string, RepositoryItem[]>,
  repositoryId: string,
  sessionId: string
): string | null {
  for (const [key, items] of collections) {
    if (!key.startsWith(`${repositoryId}:`)) continue
    if (
      items.some((item) => item.kind === "session" && item.id === sessionId)
    ) {
      const parent = key.slice(repositoryId.length + 1)
      return parent === "root" ? null : parent
    }
  }
  throw new Error("Session not found")
}

function findProject(
  collections: ReadonlyMap<string, RepositoryItem[]>,
  projectId: string
): Project | null {
  for (const items of collections.values()) {
    const project = items.find(
      (item): item is Project =>
        item.kind === "project" && item.id === projectId
    )
    if (project) return project
  }
  return null
}

function projectItem(id: string, name: string): Project {
  return { kind: "project", id, name }
}

function session(id: string, name: string, seed: number): Session {
  return { kind: "session", id, name, status: sessionStatus(seed) }
}

function collectionKey(repositoryId: string, projectId: string | null) {
  return `${repositoryId}:${projectId ?? "root"}`
}

function page<T>(
  items: readonly T[],
  cursor: string | null,
  size: number
): Page<T> {
  const offset = readCursor(cursor, items.length)
  const nextOffset = offset + size
  return {
    items: items.slice(offset, nextOffset),
    previousCursor: offset > 0 ? String(Math.max(0, offset - size)) : null,
    nextCursor: nextOffset < items.length ? String(nextOffset) : null,
  }
}

function readCursor(cursor: string | null, length: number): number {
  if (cursor === null) return 0
  if (!/^\d+$/.test(cursor)) throw new Error("Invalid repository cursor")
  const offset = Number(cursor)
  if (!Number.isSafeInteger(offset) || offset < 0 || offset >= length) {
    throw new Error("Invalid repository cursor")
  }
  return offset
}

function stableSeed(value: string): number {
  let seed = 0
  for (const character of value) {
    seed = (seed * 31 + character.charCodeAt(0)) % 100_000
  }
  return seed
}

function readSeed(id: string): number {
  const match = id.match(/:(\d+)$/)
  return match ? Number(match[1]) : 0
}

function sessionStatus(seed: number): Session["status"] {
  return seed % 5 === 0 ? "running" : seed % 3 === 0 ? "waiting" : "completed"
}

function assertValidProjectMove(
  projectId: string,
  targetProjectId: string | null,
  parents: ReadonlyMap<string, string | null>
) {
  let ancestor = targetProjectId
  while (ancestor) {
    if (ancestor === projectId) {
      throw new Error("A project cannot be moved into itself")
    }
    ancestor = parents.get(ancestor) ?? null
  }
}

function updateDepths(
  projectId: string,
  parentProjectId: string | null,
  parents: ReadonlyMap<string, string | null>,
  depths: Map<string, number>
) {
  depths.set(
    projectId,
    parentProjectId ? (depths.get(parentProjectId) ?? 0) + 1 : 0
  )
  for (const [childId, parentId] of parents) {
    if (parentId === projectId) {
      updateDepths(childId, projectId, parents, depths)
    }
  }
}
