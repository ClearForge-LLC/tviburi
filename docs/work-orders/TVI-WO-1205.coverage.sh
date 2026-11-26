#!/bin/bash
# docs/work-orders/TVI-WO-1205.coverage.sh — TVI-WO-1205 (3)'s acceptance: every roadmap line that names Aethelgard, the consumer or
# a Phase 2 seam is either cited by docs/work-orders/TVI-WO-1205.consumer.md (as R:<line>, or inside an R:<a>–<b> range) or
# listed in spike/1205/data/consumer-not.tsv with the reason it is not a consumer obligation. Prints each line neither does
# and exits 1 if any. Run from the repository root on the tree whose roadmap the checklist cites.
set -euo pipefail
c=docs/work-orders/TVI-WO-1205.consumer.md; n=spike/1205/data/consumer-not.tsv
cited=$(grep -o 'R:[0-9]*\(–[0-9]*\)\?' "$c" | sed 's/R://' | while IFS='–' read -r a b; do seq "$a" "${b:-$a}"; done | sort -un)
excl=$(awk -F'\t' '!/^#/ {print $1}' "$n" | sort -un)
miss=0
while IFS=: read -r l text; do
  if ! grep -qx "$l" <<<"$cited" && ! grep -qx "$l" <<<"$excl"; then echo "UNCOVERED R:$l ${text:0:120}"; miss=$((miss + 1)); fi
done < <(grep -n -i 'aethelgard\|consumer\|phase 2 seam' docs/roadmap.md)
echo "roadmap lines naming the consumer: $(grep -c -i 'aethelgard\|consumer\|phase 2 seam' docs/roadmap.md); uncovered: $miss"
[ "$miss" = 0 ]
