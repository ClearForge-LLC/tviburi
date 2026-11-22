#!/usr/bin/env bash
# tests/spike.sh [SRC] — TVI-WO-0100 acceptance, run on the host (Furnace).
# SRC defaults to a `git archive HEAD` export of this repository, so what is tested is what is
# committed. Each run is one container: no network, repo mounted read-only, capped by
# ~/bin/capped-run, at nice 10. Outputs go under ${TVIBURI_SCRATCH:-$HOME/.cache/tviburi}.
set -euo pipefail

IMAGE=ghcr.io/clearforge-llc/aethelgard-tools@sha256:e5fc7ddbe36c050e78763ad1d203ec8d176408a3e534bed5563c424d8d9a1b58
here=$(cd "$(dirname "$0")/.." && pwd)
scratch=${TVIBURI_SCRATCH:-$HOME/.cache/tviburi}
mkdir -p "$scratch"
work=$(mktemp -d "$scratch/spike.XXXXXX")

if [ $# -ge 1 ]; then
  src=$(cd "$1" && pwd)
else
  src=$work/src
  mkdir "$src"
  git -C "$here" archive HEAD | tar -x -C "$src"
fi

fails=0
check() { if eval "$2"; then echo "ok   $1"; else echo "FAIL $1"; fails=$((fails + 1)); fi; }

run() { # run <name> <variant>: sets $rc; outputs in $work/<name>
  mkdir "$work/$1"
  set +e
  nice -n 10 "$HOME/bin/capped-run" --mem 8G -- --rm --network none \
    -v "$src:/repo:ro,Z" -v "$work/$1:/out:Z" "$IMAGE" /repo/scripts/tviburi-spike "$2" >"$work/$1.stdout" 2>&1
  rc=$?
  set -e
  echo "--- $1 ($2): exit $rc"; grep -E '^(VERDICT|REFUSAL)' "$work/$1.stdout" || true
}

fields() { # fields <refusals.jsonl>: one record, the four invariant-5 fields non-empty, our rule
  python3 -I -c '
import json, sys
recs = [json.loads(l) for l in open(sys.argv[1])]
assert len(recs) == 1, recs
r = recs[0]
for k in ("peripheral", "register", "bit", "condition", "model_version"):
    assert isinstance(r.get(k), str) and r[k].strip(), k
assert r["rule"] == "TVI-SPI-MODF" and r["peripheral"] == "sysbus.spi1", r
assert r["model_version"].startswith("sha256:") and len(r["model_version"]) == 71, r
' "$1"
}

run modf-1 modf
check "unmodeled path (SPI1 MODF) exits REFUSED (3)" '[ $rc -eq 3 ]'
check "refusal names peripheral, register/bit, condition, model version" 'fields "$work/modf-1/refusals.jsonl"'
check "trace still records the refused access" \
  "grep -q 'spi1: \[cpu: 0x[0-9A-F]*\] WriteUInt32 to 0x0 (Control1), value 0x1\$' '$work/modf-1/renode.log'"

run modeled modeled
check "modeled path (SSI=1) exits PASS (0)" '[ $rc -eq 0 ]'
check "modeled path wrote no refusal" '[ ! -e "$work/modeled/refusals.jsonl" ]'

run modf-2 modf
check "same invocation, same refusal record (model version stable)" \
  'cmp -s "$work/modf-1/refusals.jsonl" "$work/modf-2/refusals.jsonl"'

echo "outputs: $work"
[ $fails -eq 0 ] && echo "ALL PASS" || { echo "$fails FAILED"; exit 1; }
