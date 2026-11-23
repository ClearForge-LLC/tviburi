#!/usr/bin/env bash
# tests/1201/test-budget.sh [--judge-only] [MAIN] — the memory budget's own tests (TVI-WO-1201 §1.2, §5).
# 1. The judge (tests/1301/ci-memory.sh --judge) on canned tables, no container: each refusal by its name, nothing
#    else, and the boundaries (32 MiB exactly passes; 1,572,864 KiB exactly passes).
# 2. Unless --judge-only: the planted branch. MAIN's tree (a directory, or a commit; default HEAD^1, main's tip in CI)
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

# case NAME WANT TABLE: WANT is PASS or the refusal names, sorted, space-separated. TABLE rows are
# "side peak status", one per boot; the run number is the row's place on its side.
case_() {
  local name=$1 want=$2 f=$work/$1.tsv got names
  awk '{n[$1]++; printf "%s\t%d\t%s\t1.0\t%s\n", $1, n[$1], $2, ($3 == "" ? "ok" : $3)}' <<<"$3" > "$f"
  got=$("$step" --judge "$f"); local rc=$?
  case $rc in 0) names=PASS ;; 1) names=$(sed -n 's/^REFUSE \([a-z-]*\):.*/\1/p' <<<"$got" | sort -u | paste -sd' ') ;; *) names="exit $rc" ;; esac
  if [ "$names" = "$want" ]; then echo "ok   $name: $names"; else echo "FAIL $name: got '$names', want '$want'"; sed 's/^/     /' <<<"$got"; fails=$((fails + 1)); fi
}
rows() { local s=$1 p=$2 k=$3; for _ in $(seq 1 "$k"); do echo "$s $p"; done; }

case_ pass-at-32-MiB PASS "$(rows main 1000000 5; rows branch 1032768 5)"
case_ budget-1-KiB-over budget "$(rows main 1000000 5; rows branch 1032769 5)"
case_ budget-by-median budget "$(rows main 1000000 5; rows branch 1000000 2; rows branch 1040000 3)"
case_ two-high-boots-not-median PASS "$(rows main 1000000 5; rows branch 1000000 3; rows branch 1100000 2)"
case_ ceiling-exact PASS "$(rows main 1572864 5; rows branch 1572864 5)"
case_ ceiling-1-KiB-over ceiling "$(rows main 1550000 5; rows branch 1550000 4; rows branch 1572865 1)"
case_ failed-boot-low-peak failed-boot "$(rows main 1000000 5; rows branch 1020000 4; echo 'branch 600000 boot_end_0;')"
case_ failed-boot-on-main failed-boot "$(rows main 1000000 4; echo 'main 0 no_peak_sampled;'; rows branch 1000000 5)"
case_ wrong-release failed-boot "$(rows main 1000000 5; rows branch 1000000 4; echo 'branch 1000000 no_witness_of_sha256:00;')"
case_ four-boots too-few-boots "$(rows main 1000000 4; rows branch 1000000 4)"
case_ no-branch too-few-boots "$(rows main 1000000 5)"
case_ every-branch-boot-failed "failed-boot too-few-boots" "$(rows main 1000000 5; rows branch 1000000 5 | sed 's/$/ boot_end_0;/')"

if [ "${1:-}" = --judge-only ]; then shift; else
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
