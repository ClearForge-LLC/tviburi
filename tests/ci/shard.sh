#!/usr/bin/env bash
# tests/ci/shard.sh K N — shard K of N (TVI-WO-1202): the cases (tests/spike.sh) and mutants (tests/mutants.sh) that
# tools/tviburi-route plan gives it, measured times from tests/ci/times.tsv, one case at a time in the suite's order
# (two at once changed verdicts, 1202 measure §1.5). The cases run, then the mutants, whatever the cases gave.
# Writes its record, `case <id>` and `mutant <id>` for each it ran, to TVIBURI_RAN (default: its scratch), for the
# aggregate. Exit 0 only if every case passed and every mutant was killed.
set -uo pipefail
[ $# -eq 2 ] || { echo "usage: tests/ci/shard.sh K N" >&2; exit 2; }
. "$(dirname "$0")/common.sh"
ran=${TVIBURI_RAN:-$ci/ran.txt}
: >"$ran"

cargo_route build || exit 2
TVIBURI_TESTS=list "$here/tests/spike.sh" >"$ci/cases.txt" || exit 2
route plan "$2" "$1" tests/ci/times.tsv /w/cases.txt tests/mutants.tsv >"$ci/plan.txt" || exit 2
cases=$(sed -n 's/^case //p' "$ci/plan.txt" | paste -sd' ')
mutants=$(sed -n 's/^mutant //p' "$ci/plan.txt" | paste -sd' ')
echo "shard $1 of $2: $(wc -w <<<"$cases") cases, $(wc -w <<<"$mutants") mutants"

crc=0 mrc=0
if [ -n "$cases" ]; then
  TVIBURI_TESTS="$cases" "$here/tests/spike.sh" | tee "$ci/cases.log"; crc=${PIPESTATUS[0]}
  sed -n 's/^RAN \([^ ]*\)$/case \1/p' "$ci/cases.log" >>"$ran"
fi
if [ -n "$mutants" ]; then
  TVIBURI_MUTANTS="$mutants" "$here/tests/mutants.sh" | tee "$ci/mutants.log"; mrc=${PIPESTATUS[0]}
  sed -nE 's/^(killed|ALIVE) +([^ ]+).*/mutant \2/p' "$ci/mutants.log" >>"$ran"
fi
echo "shard $1 of $2: cases exit $crc, mutants exit $mrc; recorded $(wc -l <"$ran") of $(wc -l <"$ci/plan.txt") planned"
[ $crc -eq 0 ] && [ $mrc -eq 0 ]
