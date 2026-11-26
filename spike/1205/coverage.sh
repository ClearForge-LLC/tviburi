#!/bin/bash
# spike/1205/coverage.sh ROADMAP CHECKLIST EXCLUSIONS — TVI-WO-1205 (3)'s acceptance. Every line of ROADMAP that names
# Aethelgard, the consumer or a Phase 2 seam is either cited by CHECKLIST (as R:<line>, or inside an R:<a>–<b> range) or listed
# in EXCLUSIONS (spike/1205/data/consumer-not.tsv: line, then the reason it is not a consumer obligation). Prints each line
# neither does; exit 1 if any. The paths are arguments, so this code names no document (the CI route's inventory).
set -euo pipefail
[ $# -eq 3 ] || { echo "usage: coverage.sh ROADMAP CHECKLIST EXCLUSIONS" >&2; exit 2; }
r=$1 c=$2 n=$3
cited=$(grep -o 'R:[0-9]*\(–[0-9]*\)\?' "$c" | sed 's/R://' | while IFS='–' read -r a b; do seq "$a" "${b:-$a}"; done | sort -un)
excl=$(awk -F'\t' '!/^#/ {print $1}' "$n" | sort -un)
miss=0
while IFS=: read -r l text; do
  if ! grep -qx "$l" <<<"$cited" && ! grep -qx "$l" <<<"$excl"; then echo "UNCOVERED R:$l ${text:0:120}"; miss=$((miss + 1)); fi
done < <(grep -n -i 'aethelgard\|consumer\|phase 2 seam' "$r")
echo "roadmap lines naming the consumer: $(grep -c -i 'aethelgard\|consumer\|phase 2 seam' "$r"); uncovered: $miss"
[ "$miss" = 0 ]
