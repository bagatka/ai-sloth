#!/bin/sh
set -eu
for path; do rm -rf -- "$path"; done
tar -xzf - -C /
