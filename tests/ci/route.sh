#!/usr/bin/env bash
# tests/ci/route.sh [--all | BASE] — TVI-WO-1202's route. tools/tviburi-route's own tests (each planted path it must
# refuse), its inventory of this checkout, then the class of the change: the files between BASE (default: HEAD^1, the
# base of a pull request's merge commit, whose second parent must be TVIBURI_HEAD_SHA when that is set) and HEAD,
# renames as two paths. --all (a push to main): class=code and budget=yes, whatever changed.
# Prints `class=docs|code` and `budget=yes|no`, last. Exit 0 only if the tests and the inventory pass.
set -euo pipefail
. "$(dirname "$0")/common.sh"

cargo_route test >"$ci/test.log" 2>&1 || { cat "$ci/test.log"; echo "FAIL tools/tviburi-route's tests"; exit 1; }
grep '^test result' "$ci/test.log"
cargo_route build
git -C "$here" ls-files >"$ci/ls.txt"
route inventory /w/ls.txt tests/ci/citations.tsv

if [ "${1:-}" = --all ]; then
  echo "route: a push to main runs everything"
  printf 'class=code\nbudget=yes\n'
  exit 0
fi
base=${1:-HEAD^1}
if [ -z "${1:-}" ] && [ -n "${TVIBURI_HEAD_SHA:-}" ]; then
  p2=$(git -C "$here" rev-parse --verify -q HEAD^2 || true)
  [ "$p2" = "$TVIBURI_HEAD_SHA" ] || { echo "FAIL HEAD is not this pull request's merge: its second parent is '${p2:-none}', the head is $TVIBURI_HEAD_SHA"; exit 1; }
fi
git -C "$here" diff --no-renames --name-only "$base" HEAD >"$ci/changed.txt"
echo "route: $(wc -l <"$ci/changed.txt") paths changed against $(git -C "$here" rev-parse --short "$base")"
sed 's/^/  /' "$ci/changed.txt"
route classify /w/changed.txt
