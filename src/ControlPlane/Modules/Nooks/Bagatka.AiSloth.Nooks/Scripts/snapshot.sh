#!/bin/sh
set -eu
exec 3>&1 1>&2
mkdir -p /var/lib/aisloth
out=$(mktemp -d /var/lib/aisloth/checkpoint.XXXXXX)
trap 'rm -rf "$out"' EXIT
mkdir "$out/bundles"
cat > "$out/previous"
: > "$out/manifest"
export GIT_AUTHOR_NAME=AiSloth GIT_AUTHOR_EMAIL=checkpoints@aisloth.invalid GIT_AUTHOR_DATE=1970-01-01T00:00:00Z
export GIT_COMMITTER_NAME=AiSloth GIT_COMMITTER_EMAIL=checkpoints@aisloth.invalid GIT_COMMITTER_DATE=1970-01-01T00:00:00Z

# keep PATH GIT_DIR WORK_TREE PATHSPEC...: commits the files at the pathspecs, as they are, with the
# repository's branches and HEAD as parents and named in the message, without touching its index or
# refs. The same commit as last time means nothing changed: no bundle. Otherwise the bundle holds
# what the previous commit lacks, or everything when the nook no longer has it.
keep() (
  path=$1
  export GIT_DIR="$2" GIT_WORK_TREE="$3" GIT_INDEX_FILE="$out/index"
  shift 3
  cd "$GIT_WORK_TREE"
  rm -f "$GIT_INDEX_FILE"
  if [ -f "$GIT_DIR/index" ]; then cp "$GIT_DIR/index" "$GIT_INDEX_FILE"; fi
  git add -A -- "$@"
  {
    printf 'AiSloth checkpoint\n\n'
    if [ "$GIT_DIR" = "$GIT_WORK_TREE/.git" ]; then
      if head=$(git symbolic-ref -q HEAD); then echo "HEAD ref: $head"; else echo "HEAD $(git rev-parse HEAD)"; fi
      git for-each-ref --format='%(objectname) %(refname)' refs/heads refs/remotes | grep -v '/HEAD$' || true
      git config --get-regexp '^remote\..*\.url$' | sed 's/^remote\.\(.*\)\.url /remote \1 /' || true
    fi
  } > "$out/message"
  parents=$({ git rev-parse -q --verify HEAD || true; git for-each-ref --format='%(objectname)' refs/heads refs/remotes; } | sort -u | sed 's/^/-p /')
  # $parents is "-p <commit>" for each parent, split into arguments on purpose.
  # shellcheck disable=SC2086
  commit=$(git commit-tree $parents "$(git write-tree)" < "$out/message")
  previous=$(awk -F '\t' -v path="$path" '$1 == path { print $2 }' "$out/previous")
  if [ "$commit" = "$previous" ]; then
    printf -- '-\t%s\t\t%s\n' "$commit" "$path" >> "$out/manifest"
    return
  fi
  git cat-file -e "$previous^{commit}" 2>/dev/null || previous=""
  bundle=$(find "$out/bundles" -name "*.bundle" | wc -l)
  git update-ref refs/aisloth/checkpoint "$commit"
  git bundle create -q "$out/bundles/$bundle.bundle" refs/aisloth/checkpoint ${previous:+"^$previous"}
  git update-ref -d refs/aisloth/checkpoint
  printf '%s\t%s\t%s\t%s\n' "$bundle" "$commit" "$previous" "$path" >> "$out/manifest"
)

# The kept paths that exist, relative to /.
kept=$#
for path; do
  if [ -e "$path" ]; then set -- "$@" "${path#/}"; fi
done
shift "$kept"
if [ $# -gt 0 ]; then
  [ -d /var/lib/aisloth/kept.git ] || git init -q --bare /var/lib/aisloth/kept.git
  keep / /var/lib/aisloth/kept.git / "$@"
fi

# Each repository directly in /work, then the rest of /work.
set -- .
for folder in /work/*/; do
  folder=${folder%/}
  if [ -d "$folder/.git" ]; then
    keep "$folder" "$folder/.git" "$folder" .
    set -- "$@" ":(exclude,literal)${folder#/work/}"
  fi
done
if [ -d /work/.git ]; then
  keep /work /work/.git /work "$@"
else
  [ -d /var/lib/aisloth/work.git ] || git init -q --bare /var/lib/aisloth/work.git
  keep /work /var/lib/aisloth/work.git /work "$@"
fi
tar -cf - -C "$out" manifest bundles >&3
