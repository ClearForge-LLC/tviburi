#!/bin/bash
# rmpage.sh REGEX : each match in RM0433 Rev 9's text with its printed page (content after footer N/3329 is page N+1)
awk -v re="$1" '{ if (match($0, /([0-9]+)\/3329/, m)) { last = m[1] } if ($0 ~ re) printf "p%d L%d: %s\n", last + 1 - ($0 ~ /[0-9]+\/3329/ ? 1 : 0), NR, substr($0, 1, 170) }' ${RM0433_TXT:-$HOME/.cache/aet-held/rm0433-rev9.txt}
