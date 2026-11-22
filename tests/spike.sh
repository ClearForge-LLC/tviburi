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

pod() { # pod <name> <tree> <command...>: one capped container; sets $rc; outputs in $work/<name>
  local name=$1 tree=$2; shift 2
  mkdir "$work/$name"
  rc=0
  nice -n 10 "$HOME/bin/capped-run" --mem 8G -- --rm --network none \
    -v "$tree:/repo:ro,Z" -v "$work/$name:/out:Z" "$IMAGE" "$@" </dev/null >"$work/$name.stdout" 2>&1 || rc=$?
}

run() { # run <name> <variant> [tree]: the spike runner on <tree> (default: the source under test)
  pod "$1" "${3:-$src}" /repo/scripts/tviburi-spike "$2"
  echo "--- $1 ($2): exit $rc"; grep -E '^(VERDICT|REFUSAL)' "$work/$1.stdout" || true
}

fixture() { # fixture <name> <file> <old> <new>: a copy of the source with one exact edit
  mkdir "$work/$1.tree"
  tar -C "$src" --exclude=.git -cf - . | tar -C "$work/$1.tree" -xf -
  python3 -I -c 'import sys; p,o,n=sys.argv[1:4]; s=open(p).read(); assert s.count(o)==1; open(p,"w").write(s.replace(o,n))' \
    "$work/$1.tree/$2" "$3" "$4"
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

# Fail-closed evidence: the stock (unguarded) model at SPI1 runs the MODF firmware to its end with
# no refusal, which is Renode's silent pass; without the guard's armed record it must not PASS.
fixture stock model/tviburi-nucleo-h753zi.repl 'Tviburi.GuardedSTM32H7_SPI' 'SPI.STM32H7_SPI'
run stock modf "$work/stock.tree"
check "unguarded stock model at SPI1 does not PASS the MODF firmware (exits 1)" '[ $rc -eq 1 ]'
# ... and firmware that never reaches its after-the-guarded-write marker must not PASS either.
fixture nomark firmware/spi1.rs '        write_volatile(MARK_AFTER, 0xAF7E_0002);' ''
run nomark modeled "$work/nomark.tree"
check "modeled firmware without its end marker does not PASS (exits 1)" '[ $rc -eq 1 ]'

# Every branch of the rule, driven from the monitor: exactly these refusals, in this order.
pod conditions "$src" env TVIBURI_OUT=/out TVIBURI_MODEL_DIR=/repo/model \
  timeout 120 renode --disable-gui --console --plain /repo/tests/conditions.resc
check "every rule branch refuses as declared, and the two pass cases do not" 'python3 -I -c "
import json, sys
got = [(r[\"bit\"], r[\"access\"][\"offset\"], r[\"access\"][\"width\"]) for r in map(json.loads, open(sys.argv[1]))]
want = [(\"CFG2.MASTER (bit 22) = 0\", \"0x0\", 32), (\"CFG2.SSM (bit 26) = 0\", \"0x0\", 32),
        (\"CFG2.SSIOP (bit 28) = 1\", \"0x0\", 32), (\"CR1.SSI (bit 12) = 0 with CR1.SPE (bit 0) = 1\", \"0x0\", 32),
        (\"sub-word access\", \"0x2\", 16), (\"sub-word access\", \"0xE\", 8)]
assert got == want, got
" "$work/conditions/refusals.jsonl" && tr -d "\r" <"$work/conditions.stdout" | grep -qx TVIBURI-END'

echo "outputs: $work"
[ $fails -eq 0 ] && echo "ALL PASS" || { echo "$fails FAILED"; exit 1; }
