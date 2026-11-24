#!/usr/bin/env bash
# tests/1201/test-budget.sh [--no-boots] [MAIN] — the memory budget's own tests (TVI-WO-1201 §1.2, §5). The judge's
# own cases (each refusal by name, the boundaries, a boot with no sample, fewer than 5) are tools/tviburi-budget's unit
# tests, which tests/1301/ci-memory.sh runs before every verdict.
# 1. A TVIBURI_MAIN_SHA that is not main's commit must stop the step before any boot.
# 2. Unless --no-boots: the planted branch. MAIN's tree (a directory, or a commit; default HEAD^1, main's tip in CI)
#    with tests/1201/fixtures/Planted64MiB.cs in its model/, booted against MAIN by the real step. The step must
#    refuse it by `budget` and by nothing else, with the medians 48–80 MiB apart (64 MiB, give or take the 16 MiB
#    that WO §7 allows main against main).
# Exit 0 only if every case gives what it should.
set -uo pipefail
here=$(cd "$(dirname "$0")/../.." && pwd)
step=$here/tests/1301/ci-memory.sh
scratch=${TVIBURI_SCRATCH:-$HOME/.cache/tviburi}
mkdir -p "$scratch"
work=$(mktemp -d "$scratch/budget-test.XXXXXX")
fails=0

# main bound to the pull request's base (WO §5): a base that is not MAIN's commit stops the step before any boot.
out=$(TVIBURI_MAIN_SHA=0000000000000000000000000000000000000000 "$step" HEAD HEAD 2>&1)
if [ $? = 1 ] && grep -q "^FAIL main is .*, but the pull request's base is 0000000000000000000000000000000000000000$" <<<"$out" \
  && ! grep -q "boot 1" <<<"$out"; then echo "ok   main-not-the-base: stopped before any boot"
else echo "FAIL main-not-the-base:"; sed 's/^/     /' <<<"$out"; fails=$((fails + 1)); fi

if [ "${1:-}" = --no-boots ]; then shift; else
  main=${1:-HEAD^1}
  plant=$work/plant
  mkdir "$plant"
  if [ -d "$main" ]; then cp -r "$main/." "$plant/"; else git -C "$here" archive "$main" | tar -x -C "$plant"; fi
  cp "$here/tests/1201/fixtures/Planted64MiB.cs" "$plant/model/"
  out=$(TVIBURI_MEMORY_LABEL="the planted 64 MiB branch against main (the step's own test; it must refuse by budget)" \
    "$step" "$main" "$plant" 2>&1)
  rc=$?
  echo "$out" | sed 's/^/     /'
  names=$(sed -n 's/^REFUSE \([a-z-]*\):.*/\1/p' <<<"$out" | sort -u | paste -sd' ')
  d=$(sed -n 's/^MEDIANS .* difference \(-\{0,1\}[0-9]*\) KiB.*/\1/p' <<<"$out")
  outs=$(sed -n 's/^outputs: //p' <<<"$out")
  ran=$(cat "$outs"/out-branch-*/2-accept.console.txt 2>/dev/null | grep -a -c '^TVIBURI-PLANTED 67108864 ' || true)
  boots=$(ls -d "$outs"/out-branch-* 2>/dev/null | wc -l)
  echo "     the plant ran in $ran of $boots branch boots"
  if [ $rc = 1 ] && [ "$names" = budget ] && [ "$boots" -ge 5 ] && [ "$ran" = "$boots" ] && [ -n "$d" ] && [ "$d" -ge $((48 * 1024)) ] && [ "$d" -le $((80 * 1024)) ]; then
    echo "ok   planted-64-MiB: refused by budget alone, the medians $d KiB apart"
  else
    echo "FAIL planted-64-MiB: exit $rc, refusals '$names', difference '${d:-none}' KiB, the plant in $ran of $boots boots (want exit 1, 'budget', 49152–81920, every boot)"
    fails=$((fails + 1))
  fi
fi
rm -rf "$work"
[ $fails = 0 ] && echo "ALL PASS" || { echo "$fails FAILED"; exit 1; }
