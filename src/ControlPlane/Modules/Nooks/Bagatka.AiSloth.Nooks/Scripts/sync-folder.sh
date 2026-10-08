#!/bin/sh
# Syncs a folder of this nook with a folder AiSloth keeps outside every nook, so nooks that sync the
# same one share its files. What changed on either side since this nook's last sync comes together:
# a text file both changed keeps the lines of both, so nothing either wrote is lost, and any other
# file both changed keeps this nook's version.
# $1: the folder, such as /root/.claude/memory. $2: the name of this nook's copy of the kept folder's
# history. $3: the kept folder's latest commit, empty for none yet. $4: "full" for a bundle of the
# whole history instead of what the kept folder lacks.
# Standard input: a tar of the kept folder's bundles, 0.bundle, 1.bundle, and on, oldest first.
# Standard output: a tar of `head`, the folder's commit after the sync, and `bundle`, when the kept
# folder lacks history: the commits since its latest, or all of them.
set -eu
exec 3>&1 1>&2
folder=$1 kept=$3 full=$4
mkdir -p /var/lib/aisloth/folders "$folder"

# Kept folders hold what agents write for later, such as notes; the control plane reads a sync's
# output whole, so a larger folder isn't synced.
if [ "$(du -sk "$folder" | cut -f1)" -gt 16384 ]; then
  echo "The folder holds more than 16 MiB, the most a kept folder holds." >&2
  exit 1
fi

in=$(mktemp -d /var/lib/aisloth/folder.XXXXXX)
trap 'rm -rf "$in"' EXIT
mkdir "$in/out"
cat > "$in/bundles.tar"
if [ -s "$in/bundles.tar" ]; then tar -xf "$in/bundles.tar" -C "$in"; fi
repo="/var/lib/aisloth/folders/$2.git"
if [ ! -d "$repo" ]; then
  git init -q --bare --initial-branch=main "$repo"
fi
mkdir -p "$repo/info"
echo '* merge=union' > "$repo/info/attributes"
export GIT_DIR="$repo" GIT_WORK_TREE="$folder"
export GIT_AUTHOR_NAME=AiSloth GIT_AUTHOR_EMAIL=folders@aisloth.invalid
export GIT_COMMITTER_NAME=AiSloth GIT_COMMITTER_EMAIL=folders@aisloth.invalid
cd "$folder"

# The folder as it is now, on this nook's own history.
git add -A .
if [ -n "$(git status --porcelain)" ]; then
  git commit -q -m "The folder in a nook"
fi

# The kept folder's history, merged in. Not handled: a file one side changed and the other deleted;
# whatever the merge left in the folder is kept.
n=0
while [ -f "$in/$n.bundle" ]; do
  git fetch -q "$in/$n.bundle" "+refs/heads/main:refs/kept/main"
  n=$((n + 1))
done
if [ -n "$kept" ]; then
  git merge -q --no-edit -X ours --allow-unrelated-histories -m "The folder kept" "$kept" || {
    git add -A .
    git commit -q --no-edit
  }
fi

head=$(git rev-parse -q --verify HEAD || true)
printf '%s' "$head" > "$in/out/head"
if [ -n "$head" ] && [ "$head" != "$kept" ]; then
  if [ "$full" = full ] || [ -z "$kept" ]; then
    git bundle create -q "$in/out/bundle" main
  else
    git bundle create -q "$in/out/bundle" main "^$kept"
  fi
fi
tar -cf - -C "$in/out" . >&3
