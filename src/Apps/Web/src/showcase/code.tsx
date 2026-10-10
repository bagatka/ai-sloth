import * as React from "react"

import { CodeFile, Diff, FileTree } from "@/components/code"
import { Button } from "@/components/ui/button"
import {
  changes,
  largeChange,
  repositoryPaths,
  type SampleChange,
} from "./sample-code"
import { Section, Specimen } from "./section"

const [fixture, checkout] = changes
const treeChanges = changes.map(({ path, status }) => ({ path, status }))

export function Code() {
  return (
    <Section id="code" title="Code">
      <Specimen name="Inline diff: an agent's edit" wide>
        <Diff
          path={fixture.path}
          before={fixture.before}
          after={fixture.after}
          className="w-full"
        />
      </Specimen>
      <Specimen name="Inline diff, folded until asked" wide>
        <Diff
          path={checkout.path}
          before={checkout.before}
          after={checkout.after}
          className="w-full"
        />
      </Specimen>
      <Specimen name="Panel diff, unified" wide>
        <div className="h-96 w-full overflow-hidden rounded-xl bg-card shadow-raised">
          <Diff
            variant="panel"
            path={checkout.path}
            before={checkout.before}
            after={checkout.after}
          />
        </div>
      </Specimen>
      <Specimen name="Panel diff, split" wide>
        <div className="h-96 w-full overflow-hidden rounded-xl bg-card shadow-raised">
          <Diff
            variant="panel"
            layout="split"
            path={checkout.path}
            before={checkout.before}
            after={checkout.after}
          />
        </div>
      </Specimen>
      <Specimen name="File">
        <div className="h-80 w-full overflow-hidden rounded-xl bg-card shadow-raised">
          <CodeFile path={fixture.path} contents={fixture.after} />
        </div>
      </Specimen>
      <Specimen name="File tree with changes">
        <div className="h-80 w-full overflow-hidden rounded-xl bg-card py-2 shadow-raised">
          <FileTree
            paths={repositoryPaths}
            changes={treeChanges}
            selected={checkout.path}
            className="h-full"
          />
        </div>
      </Specimen>
      <Specimen name="Large diff: 3,000 lines" wide>
        <LargeDiff />
      </Specimen>
    </Section>
  )
}

function LargeDiff() {
  const [change, setChange] = React.useState<SampleChange | null>(null)

  if (!change) {
    return (
      <Button variant="outline" onClick={() => setChange(largeChange(3000))}>
        Open a 3,000-line diff
      </Button>
    )
  }

  return (
    <div className="h-[28rem] w-full overflow-hidden rounded-xl bg-card shadow-raised">
      <Diff
        variant="panel"
        path={change.path}
        before={change.before}
        after={change.after}
      />
    </div>
  )
}
