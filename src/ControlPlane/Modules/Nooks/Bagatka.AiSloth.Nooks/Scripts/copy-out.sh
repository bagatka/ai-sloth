#!/bin/sh
set -eu
exec 3>&1 1>&2
named=$#
for path; do
  if [ -e "$path" ]; then set -- "$@" "${path#/}"; fi
done
shift "$named"
if [ $# -eq 0 ]; then set -- --files-from=/dev/null; fi
tar -cf - -C / --sort=name --mtime=@0 --owner=0 --group=0 --numeric-owner "$@" | gzip -n >&3
