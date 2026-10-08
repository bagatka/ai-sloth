#!/bin/sh
set -eu
cd "/work/$1"
git add -A
if ! git diff --cached --quiet; then git commit --quiet -m "$2"; fi
if [ "$(git rev-parse HEAD)" = "$3" ]; then exit 3; fi
git bundle create - "$3..HEAD"
