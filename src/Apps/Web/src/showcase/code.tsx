import * as React from "react"

import {
  CodeFile,
  Diff,
  DiffList,
  FileTree,
  inTreeOrder,
} from "@/components/code"
import { Button } from "@/components/ui/button"
import {
  changes,
  largeChange,
  manyChanges,
  repositoryPaths,
  type SampleChange,
} from "./sample-code"
import { Section, Specimen } from "./section"

const [fixture, checkout] = changes
const treeChanges = changes.map(({ path, status }) => ({ path, status }))
const listed = inTreeOrder(changes)

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
      <Specimen name="Diff list: every change in one scroll" wide>
        <div className="h-96 w-full overflow-hidden rounded-xl bg-card shadow-raised">
          <DiffList files={listed} />
        </div>
      </Specimen>
      <Specimen name="Diff list, split" wide>
        <div className="h-96 w-full overflow-hidden rounded-xl bg-card shadow-raised">
          <DiffList files={listed} layout="split" />
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
      <Specimen name="A big change: 61 files, one of 3,000 lines" wide>
        <BigChange />
      </Specimen>
    </Section>
  )
}

function BigChange() {
  const [files, setFiles] = React.useState<SampleChange[] | null>(null)

  if (!files) {
    return (
      <Button
        variant="outline"
        onClick={() =>
          setFiles(inTreeOrder([largeChange(3000), ...manyChanges(60)]))
        }
      >
        Open the big change
      </Button>
    )
  }

  return (
    <div className="h-[28rem] w-full overflow-hidden rounded-xl bg-card shadow-raised">
      <DiffList files={files} />
    </div>
  )
}
