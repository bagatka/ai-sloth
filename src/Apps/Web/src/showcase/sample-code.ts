// Made-up code for the showcase: the fix from the chat's story, a few other changes, and a
// repository's paths for the file tree.

import type { FileChangeStatus } from "@/components/code"

export type SampleChange = {
  path: string
  status: FileChangeStatus
  before: string
  after: string
  additions: number
  deletions: number
}

const fixtureBefore = `import type { FrameLocator, Page } from "@playwright/test"

export async function payWithCard(payment: FrameLocator, number: string) {
  await payment.getByLabel("Card number").fill(number)
  await payment.getByLabel("Expiry").fill("12 / 30")
  await payment.getByLabel("CVC").fill("123")
  await payment.getByRole("button", { name: "Pay" }).click()
}
`

const fixtureAfter = `import type { FrameLocator, Page } from "@playwright/test"

// The provider's iframe posts "payment:ready" once its form can take input.
export async function waitForPaymentFrame(page: Page): Promise<FrameLocator> {
  await page.evaluate(
    () =>
      new Promise<void>((resolve) => {
        addEventListener("message", (event) => event.data === "payment:ready" && resolve())
      })
  )
  return page.frameLocator("#payment-frame")
}

export async function payWithCard(payment: FrameLocator, number: string) {
  await payment.getByLabel("Card number").fill(number)
  await payment.getByLabel("Expiry").fill("12 / 30")
  await payment.getByLabel("CVC").fill("123")
  await payment.getByRole("button", { name: "Pay" }).click()
}
`

const testBefore = `import { expect, test } from "@playwright/test"
import { payWithCard } from "./fixtures/payment"

test.describe("checkout", () => {
  test("pays for the cart with a saved card", async ({ page }) => {
    await page.goto("/cart")
    await page.getByRole("button", { name: "Check out" }).click()

    // The payment form loads in an iframe from the provider.
    await page.waitForTimeout(2000)
    const payment = page.frameLocator("#payment-frame")
    await payWithCard(payment, "4242 4242 4242 4242")

    await expect(page.getByText("Order placed")).toBeVisible()
  })

  test("shows why a declined card failed", async ({ page }) => {
    await page.goto("/cart")
    await page.getByRole("button", { name: "Check out" }).click()

    await page.waitForTimeout(2000)
    const payment = page.frameLocator("#payment-frame")
    await payWithCard(payment, "4000 0000 0000 0002")

    await expect(page.getByText("Your card was declined")).toBeVisible()
  })
})
`

const testAfter = `import { expect, test } from "@playwright/test"
import { payWithCard, waitForPaymentFrame } from "./fixtures/payment"

test.describe("checkout", () => {
  test("pays for the cart with a saved card", async ({ page }) => {
    await page.goto("/cart")
    await page.getByRole("button", { name: "Check out" }).click()

    // The payment form loads in an iframe from the provider; wait until it says it's ready
    // instead of guessing how long that takes.
    const payment = await waitForPaymentFrame(page)
    await payWithCard(payment, "4242 4242 4242 4242")

    await expect(page.getByText("Order placed")).toBeVisible()
  })

  test("shows why a declined card failed", async ({ page }) => {
    await page.goto("/cart")
    await page.getByRole("button", { name: "Check out" }).click()

    const payment = await waitForPaymentFrame(page)
    await payWithCard(payment, "4000 0000 0000 0002")

    await expect(page.getByText("Your card was declined")).toBeVisible()
  })
})
`

const readmeBefore = `# Acme web

The storefront at acme.dev.

## Tests

Run them with \`npm test\`. End-to-end tests need the dev server running.
`

const readmeAfter = `# Acme web

The storefront at acme.dev.

## Tests

Run them with \`npm test\`. End-to-end tests need the dev server running.

Never wait a fixed time in a test: wait for what the page says it's done, such as the payment
frame's \`payment:ready\` message.
`

const ordersFixture = `import type { Page } from "@playwright/test"

export async function placeOrder(page: Page, items: string[]) {
  await page.goto("/shop")
  for (const item of items) {
    await page.getByRole("button", { name: \`Add \${item}\` }).click()
  }
  await page.getByRole("link", { name: "Cart" }).click()
}
`

const sleepHelper = `// Waits a fixed time. Prefer waiting for what the page reports.
export const sleep = (ms: number) => new Promise((resolve) => setTimeout(resolve, ms))
`

export const changes: SampleChange[] = [
  {
    path: "tests/fixtures/payment.ts",
    status: "modified",
    before: fixtureBefore,
    after: fixtureAfter,
    additions: 11,
    deletions: 0,
  },
  {
    path: "tests/checkout.test.ts",
    status: "modified",
    before: testBefore,
    after: testAfter,
    additions: 5,
    deletions: 6,
  },
  {
    path: "README.md",
    status: "modified",
    before: readmeBefore,
    after: readmeAfter,
    additions: 3,
    deletions: 0,
  },
  {
    path: "tests/fixtures/orders.ts",
    status: "added",
    before: "",
    after: ordersFixture,
    additions: 9,
    deletions: 0,
  },
  {
    path: "tests/legacy/sleep.ts",
    status: "deleted",
    before: sleepHelper,
    after: "",
    additions: 0,
    deletions: 2,
  },
]

export const changedPaths = changes.map((change) => change.path)

export const repositoryPaths = [
  ".github/workflows/ci.yml",
  ".gitignore",
  "README.md",
  "package.json",
  "playwright.config.ts",
  "tsconfig.json",
  "src/app/layout.tsx",
  "src/app/page.tsx",
  "src/cart/cart.tsx",
  "src/cart/cart-item.tsx",
  "src/checkout/checkout.tsx",
  "src/checkout/payment-frame.tsx",
  "src/checkout/summary.tsx",
  "src/lib/money.ts",
  "src/lib/money.test.ts",
  "src/orders/export.ts",
  "src/orders/refund.ts",
  "src/orders/refund.test.ts",
  "src/shop/product-card.tsx",
  "src/shop/products.ts",
  "tests/checkout.test.ts",
  "tests/orders.test.ts",
  "tests/fixtures/orders.ts",
  "tests/fixtures/payment.ts",
  "tests/legacy/sleep.ts",
]

// A long generated file and a version with scattered edits, to feel how a big diff scrolls.
export function largeChange(lines: number): SampleChange {
  const before: string[] = []
  const after: string[] = []
  for (let line = 0; line < lines; line++) {
    const route = `  { path: "/shop/item-${line}", load: () => import("./pages/item-${line}"), title: "Item ${line}" },`
    before.push(route)
    if (line % 23 === 7) {
      after.push(route.replace("title", "cache: true, title"))
    } else if (line % 97 === 41) {
      after.push(
        route,
        `  { path: "/shop/item-${line}/reviews", load: () => import("./pages/reviews"), title: "Reviews" },`
      )
    } else if (line % 131 !== 64) {
      after.push(route)
    }
  }
  return {
    path: "src/generated/routes.ts",
    status: "modified",
    before: `export const routes = [\n${before.join("\n")}\n]\n`,
    after: `export const routes = [\n${after.join("\n")}\n]\n`,
    additions: 0,
    deletions: 0,
  }
}

// Many small edited files across a few folders, to feel how a big change set scrolls.
export function manyChanges(count: number): SampleChange[] {
  const folders = ["src/api", "src/cart", "src/orders", "src/shop", "tests"]
  return Array.from({ length: count }, (_, index) => {
    const lines = Array.from(
      { length: 24 + (index % 5) * 9 },
      (_, line) => `export const setting${line} = ${line * index}`
    )
    const before = lines.join("\n") + "\n"
    const changed = index % lines.length
    lines[changed] = `export const setting${changed} = "changed"`
    lines.splice(
      12,
      0,
      `// Added while moving setting${index} to the new store.`
    )
    return {
      path: `${folders[index % folders.length]}/module-${index}.ts`,
      status: "modified",
      before,
      after: lines.join("\n") + "\n",
      additions: 2,
      deletions: 1,
    }
  })
}
