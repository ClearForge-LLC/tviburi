#!/usr/bin/env bash
# tests/mutants.sh [SRC] — the diff-sized mutation run: for each row of tests/mutants.tsv, a copy of
# the source with that one edit, then the named tests of tests/spike.sh on it. A mutant is killed
# only when those tests report a FAIL (exit 1); a setup error (exit 2) counts as not killed.
# Exit 0 only if every mutant was killed. Same environment as tests/spike.sh (TVIBURI_CI, scratch).
set -euo pipefail

here=$(cd "$(dirname "$0")/.." && pwd)
scratch=${TVIBURI_SCRATCH:-$HOME/.cache/tviburi}
mkdir -p "$scratch"
work=$(mktemp -d "$scratch/mutants.XXXXXX")
if [ $# -ge 1 ]; then
  src=$(cd "$1" && pwd)
else
  src=$work/src
  mkdir "$src"
  git -C "$here" archive HEAD | tar -x -C "$src"
fi

alive=0
while IFS=$'\t' read -r id file old new tests; do
  case "$id" in ''|'#'*) continue ;; esac
  [ -n "$tests" ] && [ -z "${tests##*[a-z]*}" ] || { echo "bad row $id: five non-empty fields required" >&2; exit 2; }
  tree=$work/$id
  mkdir "$tree"
  tar -C "$src" --exclude=.git -cf - . | tar -C "$tree" -xf -
  python3 -I -c '
import sys
unescape = lambda t: "" if t == "(empty)" else t.encode().decode("unicode_escape")
p, o, n = sys.argv[1], unescape(sys.argv[2]), unescape(sys.argv[3])
s = open(p).read()
assert s.count(o) == 1, "mutant text not found exactly once"
open(p, "w").write(s.replace(o, n))' "$tree/$file" "$old" "$new"
  rc=0
  TVIBURI_TESTS="$tests" TVIBURI_SCRATCH="$work" "$here/tests/spike.sh" "$tree" >"$work/$id.log" 2>&1 || rc=$?
  if [ $rc -eq 1 ]; then
    echo "killed $id by: $(grep '^FAIL ' "$work/$id.log" | head -1 | cut -c6-)"
  else
    echo "ALIVE  $id (tests: $tests; spike.sh exit $rc; log $work/$id.log)"
    alive=$((alive + 1))
  fi
  rm -rf "${work:?}/$id" "$work"/spike.*
done <"$src/tests/mutants.tsv"

echo "outputs: $work"
[ $alive -eq 0 ] && echo "ALL MUTANTS KILLED" || { echo "$alive ALIVE"; exit 1; }
