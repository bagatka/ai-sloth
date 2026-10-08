#!/bin/sh
set -eu
cd "/work/$1"
if [ -n "$2" ]; then git remote set-url origin "$2"; fi
git config user.name "$3" && git config user.email "$4"
git config author.name "$3" && git config author.email "$4"
git config committer.name "$5" && git config committer.email "$6"
git config aisloth.coauthor "$7"
hook="$(git rev-parse --git-path hooks)/prepare-commit-msg"
mkdir -p "$(dirname "$hook")"
cat > "$hook" <<'HOOK'
#!/bin/sh
trailer=$(git config aisloth.coauthor) || exit 0
[ -n "$trailer" ] || exit 0
exec git interpret-trailers --in-place --if-exists addIfDifferent --trailer "$trailer" "$1"
HOOK
chmod +x "$hook"
