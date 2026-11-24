#!/usr/bin/env bash
# tests/1201/test-budget.sh [--no-boots] [MAIN] — the memory budget's own tests (TVI-WO-1201 §1.2, §5). The judge's
# own cases (each refusal by name, the boundaries, a boot with no sample, fewer than 5) are tools/tviburi-budget's unit
# tests, which tests/1301/ci-memory.sh runs before every verdict.
# 1. A BRANCH that is not the pull request's merge (TVIBURI_HEAD_SHA not its second parent) must stop the step
#    before any container.
# 2. Unless --no-boots: the planted branch. MAIN's tree (a directory, or a commit; default HEAD^1, main's tip in CI)
#    with tests/1201/fixtures/Planted64MiB.cs in its model/, booted against MAIN by the real step. The step must
#    refuse it by `budget` and by nothing else, with the medians more than 32 MiB and at most 128 MiB apart.
#    Why that window (ruled 2026-11-24T02:22Z, on TVI-WO-1201 FEEDBACK F4): the test proves the budget refuses a
#    real increase, so the bound that matters is the lower one, above the 32 MiB budget. The upper bound, twice the
#    plant, only catches a sampler that counts twice. The first window, 48–80 MiB (64 ± 16), was breached on Furnace
#    at +88.4 MiB (one process, RssAnon about 90 MiB over main's: no double count), while CI measured +54.0 to
#    +64.3 MiB (runs 45285950556, 45286469273, 45294287114, 45302732365). The plant costs what the GC around it
#    makes it cost, not exactly 64 MiB.
# Exit 0 only if every case gives what it should.
set -uo pipefail
here=$(cd "$(dirname "$0")/../.." && pwd)
step=$here/tests/1301/ci-memory.sh
scratch=${TVIBURI_SCRATCH:-$HOME/.cache/tviburi}
mkdir -p "$scratch"
work=$(mktemp -d "$scratch/budget-test.XXXXXX")
fails=0
args=("$@")

# The branch must be this pull request's merge (ruled 2026-11-24T02:24Z): a BRANCH whose second parent is not the
# pull request's head stops the step before any boot, whether it is a merge of something else or no merge at all.
for c in "zeros HEAD 0000000000000000000000000000000000000000" "not-a-merge HEAD^1 $(git -C "$here" rev-parse HEAD)"; do
  set -- $c
  out=$(TVIBURI_HEAD_SHA=$3 "$step" "$2^1" "$2" 2>&1)
  if [ $? = 1 ] && grep -q "^FAIL $2 is not this pull request's merge: .* the pull request's head is $3$" <<<"$out" \
    && ! grep -q "release\|boot 1" <<<"$out"; then echo "ok   not-this-prs-merge ($1): stopped before any container"
  else echo "FAIL not-this-prs-merge ($1):"; sed 's/^/     /' <<<"$out"; fails=$((fails + 1)); fi
done
set -- "${args[@]}"

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
  if [ $rc = 1 ] && [ "$names" = budget ] && [ "$boots" -ge 5 ] && [ "$ran" = "$boots" ] && [ -n "$d" ] && [ "$d" -gt $((32 * 1024)) ] && [ "$d" -le $((128 * 1024)) ]; then
    echo "ok   planted-64-MiB: refused by budget alone, the medians $d KiB apart"
  else
    echo "FAIL planted-64-MiB: exit $rc, refusals '$names', difference '${d:-none}' KiB, the plant in $ran of $boots boots (want exit 1, 'budget', over 32768 and at most 131072, every boot)"
    fails=$((fails + 1))
  fi
fi
rm -rf "$work"
[ $fails = 0 ] && echo "ALL PASS" || { echo "$fails FAILED"; exit 1; }
