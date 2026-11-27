#!/bin/bash
# spike/1206/page.sh N [N2] — print RM0433 Rev 9's printed pages N..N2 from the held text, with the convention of
# tests/1300/rmpage.sh: a page's content ends at its footer "N/3329". RM0433_TXT overrides the held text's path.
set -euo pipefail
a=$1 b=${2:-$1}
awk -v a="$a" -v b="$b" '{ p = cur + 1; if (p >= a && p <= b) print; if (match($0, /([0-9]+)\/3329/, m)) cur = m[1] }' \
  "${RM0433_TXT:-$HOME/.cache/aet-held/rm0433-rev9.txt}"
