import { expect, test } from "bun:test"
import { parseFiles } from "../src/session/internal/parse-diff"

const patch = `diff --git a/first.txt b/first.txt
index df967b9..e019be0 100644
--- a/first.txt
+++ b/first.txt
@@ -1 +1 @@
-base
+first
diff --git a/second.txt b/second.txt
new file mode 100644
index 0000000..e019be0
--- /dev/null
+++ b/second.txt
@@ -0,0 +1 @@
+second
`

test("parses every file in an aggregate session patch", () => {
  expect(parseFiles(patch).map((file) => file.name)).toEqual([
    "first.txt",
    "second.txt",
  ])
})
