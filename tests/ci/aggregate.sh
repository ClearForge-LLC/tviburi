#!/usr/bin/env bash
# tests/ci/aggregate.sh RAN... — TVI-WO-1202's aggregate: every case of this tree's lists (tests/spike.sh's, and
# tests/mutants.tsv's mutants) ran exactly once across the shards' records, and nothing else ran. The expected set
# is the tree's, never tests/ci/times.tsv's. Exit 0 only if so.
set -euo pipefail
[ $# -ge 1 ] || { echo "usage: tests/ci/aggregate.sh RAN..." >&2; exit 2; }
. "$(dirname "$0")/common.sh"
cargo_route build
cases_list
args=() i=0
for r in "$@"; do i=$((i + 1)); cp "$r" "$ci/ran-$i.txt"; args+=("/w/ran-$i.txt"); done
route aggregate /w/cases.txt tests/mutants.tsv "${args[@]}"
