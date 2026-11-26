#!/bin/bash
# tests/1301/witnessed.sh CONSOLE MANIFEST — TVI-WO-1204 (6): a witness per restated instance. Every instance the release puts
# under a guard (the MANIFEST's `restate` and `retype` lines) must have a witness record on the boot's console under its own name:
# named, not counted, so 30 witnesses that are not the 30 restated instances fail. The console's records are read as RUL-11
# frames them: a stamp, `[LEVEL] `, then `<source>: {"tviburi":"witness"`. Prints the instances with no witness; exit 0 only if
# there are none. Run by tests/1301/ci-memory.sh on each branch boot of the consumer's fixture, whose platform declares both
# retyped instances.
set -euo pipefail
[ $# -eq 2 ] || { echo "usage: witnessed.sh CONSOLE MANIFEST" >&2; exit 2; }
want=$(awk '$1 == "restate" || $1 == "retype" {print $3}' "$2" | sort -u)
[ -n "$want" ] || { echo "witnessed: the manifest puts no instance under a guard"; exit 1; }
got=$(tr -d '\r' <"$1" | sed -n 's/^\[[0-9:.]*\]  *\[[A-Z]*\] \([A-Za-z0-9_]*\): {"tviburi":"witness".*/\1/p' | sort -u)
missing=$(comm -23 <(printf '%s\n' "$want") <(printf '%s\n' "$got") | paste -sd' ' -)
if [ -n "$missing" ]; then
  echo "witnessed: no witness for $missing (of $(wc -l <<<"$want") instances under a guard)"
  exit 1
fi
echo "witnessed: every one of the $(wc -l <<<"$want") instances under a guard has a witness"
