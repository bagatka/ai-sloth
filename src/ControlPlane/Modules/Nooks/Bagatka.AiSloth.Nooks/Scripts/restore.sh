#!/bin/sh
set -eu
exec 3>&1 1>&2
# $1: what to archive of /work as it was, such as "." or a source's folder; empty to put the files
# back in this nook instead. Then each place: its path, its commit, and how many bundles bring it.
# Standard input: a tar of the bundles, <place>-<n>.bundle, oldest first.
archive=$1
shift
mkdir -p /var/lib/aisloth
in=$(mktemp -d /var/lib/aisloth/restore.XXXXXX)
trap 'rm -rf "$in"' EXIT
tar -xf - -C "$in"
if [ -n "$archive" ]; then
  root="$in/root"
else
  # Nothing ran in the nook before its files come back, apart from an earlier try at this, or
  # the setup of the ready copy it started from: of the repositories coming back, what git
  # ignores stays, such as installed dependencies; everything else goes.
  root=""
  coming_back() {
    wanted=$1
    shift
    while [ $# -ge 3 ]; do
      if [ "$1" = "$wanted" ]; then return 0; fi
      shift 3
    done
    return 1
  }
  for entry in /work/* /work/.[!.]* /work/..?*; do
    [ -e "$entry" ] || continue
    if [ -d "$entry/.git" ] && coming_back "$entry" "$@"; then
      (cd "$entry" && git clean -fdq && git ls-files -z | xargs -0 -r rm -f -- && rm -rf .git)
    else
      rm -rf "$entry"
    fi
  done
  rm -rf /var/lib/aisloth/work.git /var/lib/aisloth/kept.git
fi
place=0
while [ $# -gt 0 ]; do
  path=$1 commit=$2 count=$3
  shift 3
  git init -q --bare "$in/git"
  n=0
  while [ "$n" -lt "$count" ]; do
    git --git-dir="$in/git" fetch -q "$in/$place-$n.bundle" +refs/aisloth/checkpoint:refs/aisloth/checkpoint
    n=$((n + 1))
  done
  git --git-dir="$in/git" update-ref -d refs/aisloth/checkpoint
  if git --git-dir="$in/git" cat-file commit "$commit" | grep -q '^HEAD '; then
    # A repository: its branches, remotes, and HEAD back, and its changes uncommitted.
    mkdir -p "$root$path"
    mv "$in/git" "$root$path/.git"
    (
      cd "$root$path"
      git config core.bare false
      git cat-file commit "$commit" | sed '1,/^$/d' | while read -r first second third; do
        case "$first" in
          HEAD) if [ "$second" = "ref:" ]; then git symbolic-ref HEAD "$third"; else git update-ref --no-deref HEAD "$second"; fi ;;
          remote) git remote add "$second" "$third" ;;
          AiSloth | '') ;;
          *) git update-ref "$second" "$first" ;;
        esac
      done
      git read-tree -u --reset "$commit"
      if git rev-parse -q --verify HEAD > /dev/null; then git read-tree HEAD; else git read-tree --empty; fi
    )
  else
    # Files without a repository of their own: the rest of /work, or the kept paths under /.
    if [ "$path" = / ]; then gitdir="$root/var/lib/aisloth/kept.git"; else gitdir="$root/var/lib/aisloth/work.git"; fi
    mkdir -p "$(dirname "$gitdir")" "$root$path"
    mv "$in/git" "$gitdir"
    GIT_DIR="$gitdir" GIT_WORK_TREE="$root$path" git read-tree -u --reset "$commit"
  fi
  place=$((place + 1))
done
if [ -n "$archive" ]; then
  tar -czf - -C "$root/work" "$archive" >&3
fi
