#!/bin/bash
# spike/1206/rebuild.sh DOC — TVI-WO-1206a: the readings rebuild from the repository alone, byte for byte. From the repository
# root, on a clean tree: (1) spike/1206/readings.py, from the committed readings (spike/1206/data/readings/) and work lists, gives
# spike/1206/data/readings.tsv exactly; (2) its --table gives DOC's §3 table exactly (DOC: the readings' document, a path
# argument, so this code names no document); (3) spike/1206/apply.py puts those readings into the guards' tables and the
# generators with no byte changed in model/ or tests/. Exit 0 only if all three hold.
set -euo pipefail
[ $# -eq 1 ] || { echo "usage: rebuild.sh DOC" >&2; exit 2; }
doc=$1 fail=0
git diff --quiet -- model tests || { echo "rebuild: model/ or tests/ is not clean; run on a clean tree" >&2; exit 2; }
if python3 -I spike/1206/readings.py | cmp -s - spike/1206/data/readings.tsv; then echo "ok   readings.tsv rebuilds byte for byte"; else echo "FAIL readings.tsv differs"; fail=1; fi
if python3 -I spike/1206/readings.py --table | cmp -s - <(python3 -I -c 'import sys; s=open(sys.argv[1]).read(); i=s.index("## 3. The acknowledgements\n\n")+len("## 3. The acknowledgements\n\n"); print(s[i:s.index("\n## 4.")], end="")' "$doc"); then echo "ok   §3's table rebuilds byte for byte"; else echo "FAIL §3's table differs"; fail=1; fi
python3 -I spike/1206/apply.py spike/1206/data/readings.tsv >/dev/null
if git diff --quiet -- model tests; then echo "ok   the guards' tables and generators rebuild with no byte changed"; else echo "FAIL apply.py changed:"; git diff --stat -- model tests; fail=1; fi
exit $fail
