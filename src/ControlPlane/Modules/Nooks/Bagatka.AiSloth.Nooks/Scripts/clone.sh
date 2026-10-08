#!/bin/sh
set -eu
incoming="/work/.aisloth-incoming-$1"
rm -rf "$incoming" "$incoming.bundle"
mkdir -p /work
cat > "$incoming.bundle"
branch=$(git bundle list-heads "$incoming.bundle" | sed -n 's#^[0-9a-f]* refs/heads/##p' | head -n 1)
if [ -d "/work/$1/.git" ]; then
    cd "/work/$1"
    git fetch --quiet --force --prune "$incoming.bundle" "+refs/heads/*:refs/remotes/origin/*"
    git checkout --quiet --force -B "$branch" "refs/remotes/origin/$branch"
    git branch --quiet --set-upstream-to "origin/$branch"
    git clean -fdq
    git for-each-ref --format='%(refname:short)' refs/heads | grep -vxF "$branch" | xargs -r git branch -q -D
    rm -f "$incoming.bundle"
    exit 0
fi
git clone --quiet --branch "$branch" "$incoming.bundle" "$incoming"
rm -f "$incoming.bundle"
rm -rf "/work/$1"
mv "$incoming" "/work/$1"
