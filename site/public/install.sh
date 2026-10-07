#!/bin/sh
# Installs sloth, or updates it to the newest release:
#   curl -fsSL https://aisloth.dev/install.sh | sh
# The steps of sloth update, for a computer without sloth: the build for this computer from the newest
# release on GitHub, kept only when its SHA-256 is the one GitHub keeps for it, saved beside its place
# and moved there whole. Its place is the sloth already on the PATH, or else ~/.local/bin.
set -eu

repository=bagatka/ai-sloth

fail() {
  printf 'sloth: %s\n' "$1" >&2
  exit 1
}

system=$(uname -s)
machine=$(uname -m)
# A shell under Rosetta on a Mac with Apple silicon says x86_64; the Mac's own build is the one to run.
if [ "$system-$machine" = Darwin-x86_64 ] && [ "$(sysctl -n sysctl.proc_translated 2>/dev/null)" = 1 ]; then
  machine=arm64
fi
case "$system-$machine" in
  Linux-x86_64 | Linux-amd64) platform=linux-x64 ;;
  Linux-aarch64 | Linux-arm64) platform=linux-arm64 ;;
  Darwin-arm64) platform=osx-arm64 ;;
  *) fail "There's no sloth for $system on $machine: it runs on Linux, on x64 or arm64, and on Macs with Apple silicon. On Windows, install it in WSL." ;;
esac

# GitHub's JSON for the newest release, one field per line once commas end lines. Each build's digest
# follows its name.
release=$(curl -fsSL -H "Accept: application/vnd.github+json" "https://api.github.com/repos/$repository/releases/latest") \
  || fail "GitHub didn't answer with sloth's newest release; try again later."
fields=$(printf '%s\n' "$release" | tr ',' '\n')
version=$(printf '%s\n' "$fields" | sed -n 's/^ *"tag_name": *"sloth-v\([0-9]*\.[0-9]*\.[0-9]*\)".*/\1/p')
digest=$(printf '%s\n' "$fields" | awk -v name="\"sloth-$platform\"" '
  index($0, "\"name\"") && index($0, name) { found = 1 }
  found && index($0, "\"digest\"") { sub(/.*"sha256:/, ""); sub(/".*/, ""); print; exit }')
[ -n "$version" ] || fail "sloth has no releases yet."
[ -n "$digest" ] || fail "sloth $version has no build for $platform."

target=$(command -v sloth || true)
current=""
if [ -n "$target" ]; then
  current=$("$target" version 2>/dev/null | sed -n 's/^sloth \([0-9.]*\)$/\1/p')
  if [ "$current" = "$version" ]; then
    printf 'You have sloth %s, the newest.\n' "$version"
    exit 0
  fi
else
  target="$HOME/.local/bin/sloth"
  mkdir -p "$HOME/.local/bin"
fi

directory=$(dirname "$target")
[ -w "$directory" ] || fail "Can't write to $directory. Update sloth there with sudo: curl -fsSL https://aisloth.dev/install.sh | sudo sh"
partial="$target.new"
trap 'rm -f "$partial"' EXIT
curl -fsSL "https://github.com/$repository/releases/download/sloth-v$version/sloth-$platform" -o "$partial" \
  || fail "The download of sloth-$platform didn't finish; try again."
if command -v sha256sum >/dev/null; then
  actual=$(sha256sum "$partial" | cut -d ' ' -f 1)
else
  actual=$(shasum -a 256 "$partial" | cut -d ' ' -f 1)
fi
[ "$actual" = "$digest" ] || fail "The download of sloth-$platform isn't the file GitHub has, so it wasn't installed; try again."
chmod 755 "$partial"
mv -f "$partial" "$target"

if [ -n "$current" ]; then
  printf 'Updated sloth from %s to %s.\n' "$current" "$version"
else
  printf 'Installed sloth %s in %s.\n' "$version" "$directory"
fi
case ":$PATH:" in
  *":$directory:"*) printf 'Get started: sloth help\n' ;;
  *) printf "%s is not on your PATH. Add it in your shell's startup file, then: sloth help\n  export PATH=\"%s:\$PATH\"\n" "$directory" "$directory" ;;
esac
