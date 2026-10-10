#!/bin/sh
# $1: the source, at /work/$1; $2: the commit it was copied in at; $3: the most bytes to print.
# Prints what the source changed since $2 as git prints a diff, its commits since and what isn't
# committed, new files included, and one byte more when there's more than $3. It stages in an index
# of its own, so the source's own index stays as it is. The source's git settings can't change how
# the diff looks: no external diff or text conversion, standard prefixes, and paths as they are.
set -eu
cd "/work/$1"
index=$(mktemp -d)
trap 'rm -rf "$index"' EXIT
cp "$(git rev-parse --git-path index)" "$index/index" 2>/dev/null || true
export GIT_INDEX_FILE="$index/index"
git add -A
git -c core.quotePath=false diff --cached --no-color --no-ext-diff --no-textconv --find-renames \
    --src-prefix=a/ --dst-prefix=b/ "$2" | head -c "$(($3 + 1))"
