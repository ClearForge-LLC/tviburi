#!/usr/bin/env bash
# tests/spike.sh [SRC] — the TVI-WO-0100/0100a acceptance suite, run on the host.
# SRC defaults to a `git archive HEAD` export of this repository, so what is tested is what is
# committed. Each run is one container in the pinned tools image, with no network and the source
# mounted read-only. Outputs go under ${TVIBURI_SCRATCH:-$HOME/.cache/tviburi}.
#
#   TVIBURI_TESTS="modf conditions ..."  run only these tests, or a group: "acceptance" (the default),
#                                        "adversarial" (tests/adversarial.sh) or "all".
#   TVIBURI_CI=1                         run containers with plain `podman run`. Only for a
#                                        disposable CI runner; elsewhere ~/bin/capped-run is required.
# Exit 0 only if every selected test passed.
set -euo pipefail

IMAGE=ghcr.io/clearforge-llc/aethelgard-tools@sha256:e5fc7ddbe36c050e78763ad1d203ec8d176408a3e534bed5563c424d8d9a1b58
ACCEPTANCE="unit modf modeled ardupilot generated field stable cfg2first iolock rmw forge stock-forge stock-modf nomark conditions fields load crash"
ADVERSARIAL="adv-removed adv-extra-arg adv-bad-variant adv-env adv-hook adv-loglevel adv-spray adv-noend adv-version adv-recorded"
ALL="$ACCEPTANCE $ADVERSARIAL"
here=$(cd "$(dirname "$0")/.." && pwd)
scratch=${TVIBURI_SCRATCH:-$HOME/.cache/tviburi}
mkdir -p "$scratch"
work=$(mktemp -d "$scratch/spike.XXXXXX")

if [ "${TVIBURI_CI:-}" = 1 ]; then
  container() { podman run --rm --network none "$@"; }
else
  [ -x "$HOME/bin/capped-run" ] || { echo "no ~/bin/capped-run; TVIBURI_CI=1 is for CI runners only" >&2; exit 2; }
  container() { nice -n 10 "$HOME/bin/capped-run" --mem 8G -- --rm --network none "$@"; }
fi

if [ $# -ge 1 ]; then
  src=$(cd "$1" && pwd)
else
  src=$work/src
  mkdir "$src"
  git -C "$here" archive HEAD | tar -x -C "$src"
fi

fails=0
check() { if eval "$2"; then echo "ok   $1"; else echo "FAIL $1"; fails=$((fails + 1)); fi; }

pod() { # pod <name> <tree> [podman args...] -- <command...>: one container; sets $rc; outputs in $work/<name>
  local name=$1 tree=$2; shift 2
  local extra=()
  while [ "$1" != -- ]; do extra+=("$1"); shift; done
  shift
  mkdir "$work/$name"
  rc=0
  container "${extra[@]}" -v "$tree:/repo:ro,Z" -v "$work/$name:/out:Z" \
    "$IMAGE" "$@" </dev/null >"$work/$name.stdout" 2>&1 || rc=$?
}

run() { # run <name> <variant> [tree]: the runner on <tree> (default: the source under test)
  pod "$1" "${3:-$src}" -- /repo/scripts/tviburi-spike "$2"
  echo "--- $1 ($2): exit $rc"; grep -E '^(VERDICT|CHECK|PROBLEM)' "$work/$1.stdout" || true
}

renode_in() { # renode_in <name> <resc> [podman args...]: Renode on a monitor script, then the check
  local name=$1 resc=$2; shift 2
  pod "$name" "$src" -v "$work/check-build:/check:ro,Z" "$@" -- sh -c "timeout 120 renode --disable-gui --console --plain $resc </dev/null >/out/renode.out 2>&1; \
    echo renode-exit=\$?; /check/debug/tviburi-check \$(/repo/tests/model-version /repo/model) /out/renode-log.txt spi1"
}

fixture() { # fixture <name> <file> <old> <new>: a copy of the source with one exact edit
  mkdir "$work/$1.tree"
  tar -C "$src" --exclude=.git -cf - . | tar -C "$work/$1.tree" -xf -
  python3 -I -c 'import sys; p,o,n=sys.argv[1:4]; s=open(p).read(); assert s.count(o)==1; open(p,"w").write(s.replace(o,n))' \
    "$work/$1.tree/$2" "$3" "$4"
}

prelude_line='machine LoadPlatformDescription @/repo/model/tviburi-nucleo-h753zi.repl'

records() { # records <stdout> <python expr over `recs`>: the check's REFUSAL lines, parsed
  python3 -I -c '
import json, sys
recs = [json.loads(l[8:]) for l in open(sys.argv[1]) if l.startswith("REFUSAL ")]
sys.exit(0 if eval(sys.argv[2]) else 1)
' "$1" "$2"
}

fields='(len(recs) == 1 and all(isinstance(recs[0].get(k), str) and recs[0][k].strip() for k in
  ("rule", "peripheral", "register", "bit", "condition", "model_version", "citation", "state"))
  and recs[0]["peripheral"] == "sysbus.spi1" and recs[0]["model_version"].startswith("sha256:")
  and len(recs[0]["model_version"]) == 71 and "RM0433 Rev 9" in recs[0]["citation"])'
forged_in() { grep -q 'usart3: .*{"tviburi":"witness"' "$work/$1/renode-log.txt"; }

t_unit() {
  pod unit "$src" -- sh -c 'export PATH=/opt/cargo/bin:$PATH RUSTUP_HOME=/opt/rustup CARGO_HOME=/out/cargo-home;
    cargo test --offline --locked --manifest-path /repo/tools/tviburi-check/Cargo.toml --target-dir /out/target'
  check "reference check: unit tests pass" '[ $rc -eq 0 ]'
}

t_modf() {
  run modf modf
  check "SPI1 MODF path exits REFUSED (3)" '[ $rc -eq 3 ]'
  check "the refusal names rule, peripheral, register, bit, condition, model version, citation" "records '$work/modf.stdout' '$fields'"
  check "the refusal is the SSI=0 mode fault, after the witness (seq 1)" \
    "records '$work/modf.stdout' 'recs[0][\"rule\"] == \"TVI-SPI-MODF\" and recs[0][\"bit\"] == \"CR1.SSI (bit 12) = 0\" and recs[0][\"seq\"] == 1'"
  check "the trace still records the refused access" \
    "grep -q 'spi1: \[cpu: 0x[0-9A-F]*\] WriteUInt32 to 0x0 (Control1), value 0x1\$' '$work/modf/renode-log.txt'"
  check "the MODF scar is a predicted-fault, citing RM0433 Rev 9 p2184 (gate clause 4; RUL-18)" \
    "records '$work/modf.stdout' 'recs[0][\"kind\"] == \"predicted-fault\" and \"RM0433 Rev 9 §50.5.2 p2184\" in recs[0][\"citation\"]'"
}

t_modeled() {
  run modeled modeled
  check "modeled path (SSI=1 before CFG2) exits PASS (0)" '[ $rc -eq 0 ]'
  check "modeled path: no refusal record" "! grep -q '^REFUSAL' '$work/modeled.stdout'"
}

t_stable() {
  [ -d "$work/modf" ] || run modf modf
  run modf-2 modf
  check "same invocation, same refusal record" \
    "cmp -s <(grep '^REFUSAL' '$work/modf.stdout') <(grep '^REFUSAL' '$work/modf-2.stdout')"
}

t_ardupilot() {
  run ardupilot ardupilot
  check "ArduPilot's SPI start, exchange and stop, as measured, PASS (0) in enforcing mode" '[ $rc -eq 0 ] && ! grep -q "^REFUSAL" "$work/ardupilot.stdout"'
  check "...and its enable carries MASRX by read-modify-write, as on silicon (CR1 0x101)" \
    "grep -q 'spi1: \[cpu: 0x[0-9A-F]*\] WriteUInt32 to 0x0 (Control1), value 0x101\$' '$work/ardupilot/renode-log.txt'"
}

t_generated() {
  run generated generated
  check "the generated firmware's SPI1 sequence PASSes (0) in enforcing mode" '[ $rc -eq 0 ] && ! grep -q "^REFUSAL" "$work/generated.stdout"'
}

t_field() {
  run field field
  check "a planted field outside the allow-list (CFG2.CPOL=1) is REFUSED (3), TVI-SPI-FIELD, unmodeled" \
    "[ \$rc -eq 3 ] && records '$work/field.stdout' 'len(recs) == 1 and recs[0][\"rule\"] == \"TVI-SPI-FIELD\" and recs[0][\"kind\"] == \"unmodeled\" and recs[0][\"bit\"] == \"SPI_CFG2.CPOL (bit 25) = 1\"'"
}

t_cfg2first() {
  run cfg2first cfg2first
  check "CFG2 written before SSI exits REFUSED (3) on the master-state history (D1)" \
    "[ \$rc -eq 3 ] && records '$work/cfg2first.stdout' 'len(recs) == 1 and recs[0][\"bit\"].startswith(\"a master state other than the passing one\")'"
}

t_iolock() {
  run iolock iolock
  check "IOLOCK then CFG2 exits REFUSED (3) by TVI-SPI-IOLOCK" \
    "[ \$rc -eq 3 ] && records '$work/iolock.stdout' 'len(recs) == 1 and recs[0][\"rule\"] == \"TVI-SPI-IOLOCK\"'"
}

t_rmw() {
  run rmw rmw
  check "read-modify-write of CFG2 after SSIOP=1: the guard's copy follows silicon, REFUSED (3) on SSIOP" \
    "[ \$rc -eq 3 ] && records '$work/rmw.stdout' 'len(recs) == 1 and recs[0][\"bit\"] == \"CFG2.SSIOP (bit 28) = 1\"'"
}

t_forge() {
  run forge forge
  check "imitation records on USART3 reach the log (the test is not vacuous)" 'forged_in forge'
  check "a forged refusal on USART3 is ignored: the modeled path still PASSes (0)" '[ $rc -eq 0 ]'
}

t_stock-forge() {
  [ -d "$work/stock.tree" ] || fixture stock model/tviburi-nucleo-h753zi.repl 'Tviburi.GuardedSTM32H7_SPI' 'SPI.STM32H7_SPI'
  run stock-forge forge "$work/stock.tree"
  check "stock model + forged witness and end on USART3: the forgery reaches the log" 'forged_in stock-forge'
  check "stock model + forged witness and end on USART3 does not PASS (1, no witness)" \
    "[ \$rc -eq 1 ] && grep -q '^PROBLEM spi1: no load witness' '$work/stock-forge.stdout'"
}

t_stock-modf() {
  [ -d "$work/stock.tree" ] || fixture stock model/tviburi-nucleo-h753zi.repl 'Tviburi.GuardedSTM32H7_SPI' 'SPI.STM32H7_SPI'
  run stock-modf modf "$work/stock.tree"
  check "unguarded stock model at SPI1 does not PASS the MODF firmware (1)" '[ $rc -eq 1 ]'
}

t_nomark() {
  fixture nomark firmware/spi1.rs '        write_volatile(MARK_AFTER, 0xAF7E_0002);' ''
  run nomark modeled "$work/nomark.tree"
  check "modeled firmware without its end marker does not PASS (1)" '[ $rc -eq 1 ]'
}

need_check() { # build the reference check once, into $work/check-build, for the direct-Renode tests
  [ -d "$work/check-build" ] && return
  pod check-build "$src" -- sh -c 'export PATH=/opt/cargo/bin:$PATH RUSTUP_HOME=/opt/rustup CARGO_HOME=/out/cargo-home;
    cargo build --quiet --offline --locked --manifest-path /repo/tools/tviburi-check/Cargo.toml --target-dir /out'
}

t_conditions() {
  need_check
  renode_in conditions /repo/tests/conditions.resc -e TVIBURI_MODEL_DIR=/repo/model
  check "every rule branch refuses as declared, with its kind, in order, and the pass cases do not" "records '$work/conditions.stdout' '
[(r[\"rule\"], r[\"kind\"], r[\"bit\"], r[\"access\"][\"offset\"], r[\"access\"][\"width\"]) for r in recs] == [
 (\"TVI-SPI-MODF\", \"unmodeled\", \"CFG2.MASTER (bit 22) = 0\", \"0x0\", 32),
 (\"TVI-SPI-MODF\", \"unmodeled\", \"CFG2.SP[2:0] (bits 21:19) != 000\", \"0x0\", 32),
 (\"TVI-SPI-MODF\", \"unmodeled\", \"CFG2.SSM (bit 26) = 0, CFG2.SSOE (bit 29) = 0\", \"0x0\", 32),
 (\"TVI-SPI-MODF\", \"unmodeled\", \"CFG2.SSIOP (bit 28) = 1\", \"0x0\", 32),
 (\"TVI-SPI-MODF\", \"predicted-fault\", \"CR1.SSI (bit 12) = 0\", \"0x0\", 32),
 (\"TVI-SPI-MODF\", \"unmodeled\", \"CFG2 read after a master state other than the passing one, since reset: CR1=0x00000000, CFG2=0x04400000\", \"0xC\", 32),
 (\"TVI-SPI-MODF\", \"unmodeled\", \"a master state other than the passing one, since reset: CR1=0x00000000, CFG2=0x04400000\", \"0x0\", 32),
 (\"TVI-SPI-CFG2-SPE\", \"predicted-fault\", \"CR1.SPE (bit 0) = 1\", \"0xC\", 32),
 (\"TVI-SPI-IOLOCK\", \"predicted-fault\", \"CR1.IOLOCK (bit 16) = 1\", \"0xC\", 32),
 (\"TVI-SPI-IOLOCK\", \"unmodeled\", \"CR1.IOLOCK (bit 16), read\", \"0x0\", 32),
 (\"TVI-SPI-I2SMOD\", \"unmodeled\", \"I2SCFGR.I2SMOD (bit 0) = 1\", \"0x50\", 32),
 (\"TVI-SPI-WIDTH\", \"unmodeled\", \"sub-word or unaligned write\", \"0x2\", 16),
 (\"TVI-SPI-WIDTH\", \"unmodeled\", \"sub-word or unaligned write\", \"0xE\", 8),
 (\"TVI-SPI-WIDTH\", \"unmodeled\", \"sub-word or unaligned write\", \"0x51\", 8),
 (\"TVI-SPI-WIDTH\", \"unmodeled\", \"sub-word or unaligned write\", \"0xD\", 32),
 (\"TVI-SPI-WIDTH\", \"unmodeled\", \"sub-word or unaligned read\", \"0x0\", 16),
 (\"TVI-SPI-WIDTH\", \"unmodeled\", \"sub-word or unaligned read\", \"0xD\", 32),
 (\"TVI-SPI-MODF\", \"unmodeled\", \"CFG2.MASTER (bit 22) = 0\", \"0x0\", 32),
 (\"TVI-SPI-MODF\", \"unmodeled\", \"CFG2.MASTER (bit 22) = 0\", \"0x0\", 32),
 (\"TVI-SPI-IOLOCK\", \"unmodeled\", \"CR1.IOLOCK (bit 16) = 1\", \"0xC\", 32),
 (\"TVI-SPI-IOLOCK\", \"unmodeled\", \"CR1.IOLOCK (bit 16) = 1\", \"0xC\", 32),
 (\"TVI-SPI-MODF\", \"unmodeled\", \"CFG2 read after IFCR.MODFC written after such a state, and CFG2 not written since\", \"0xC\", 32),
 (\"TVI-SPI-MODF\", \"unmodeled\", \"CFG2.MASTER unknown: IFCR.MODFC written after a master state other than the passing one, and CFG2 not written since\", \"0x0\", 32),
 (\"TVI-SPI-MODF\", \"unmodeled\", \"a master state other than the passing one, since reset: CR1=0x00000000, CFG2=0x04400000\", \"0x0\", 32),
 (\"TVI-SPI-CFG1-SPE\", \"unmodeled\", \"CR1.SPE (bit 0) = 1\", \"0x8\", 32),
 (\"TVI-SPI-CFG1-SPE\", \"predicted-fault\", \"CR1.SPE (bit 0) = 1\", \"0x8\", 32),
 (\"TVI-SPI-FIELD\", \"unmodeled\", \"SPI_CFG1.DSIZE (4:0) = 00000 at the enable\", \"0x0\", 32),
 (\"TVI-SPI-WIDTH\", \"unmodeled\", \"32-bit write, frame 8 bits\", \"0x20\", 32),
 (\"TVI-SPI-WIDTH\", \"unmodeled\", \"16-bit read, frame 8 bits\", \"0x30\", 16),
 (\"TVI-SPI-WIDTH\", \"unmodeled\", \"sub-word or unaligned write\", \"0x4\", 16),
 (\"TVI-SPI-FIELD\", \"unmodeled\", \"SPI_CRCPOLY.CRCPOLY (31:0) = 0x7\", \"0x40\", 32),
 (\"TVI-SPI-FIELD\", \"unmodeled\", \"no register at offset 0x1C\", \"0x1C\", 32),
 (\"TVI-SPI-ORDER\", \"unmodeled\", \"CR1.SPE (bit 0) = 0, TXDR written\", \"0x20\", 8),
 (\"TVI-SPI-ORDER\", \"unmodeled\", \"CR1.CSTART (bit 9) = 1 with SPE = 0\", \"0x0\", 32),
 (\"TVI-SPI-ORDER\", \"unmodeled\", \"CR1.CSTART (bit 9) = 1 with SPE = 0\", \"0x0\", 32),
 (\"TVI-SPI-ORDER\", \"unmodeled\", \"CR1.SPE (bit 0) = 1, CR2 written\", \"0x4\", 32)]' \
      && grep -qx 'CHECK REFUSED' '$work/conditions.stdout'"
  local console; console=$("$src/scripts/monitor-output" "$work/conditions/renode.out")
  check "the conditions script reached its end" 'grep -qx TVIBURI-END <<<"$console"'
  check "reads return silicon's value: CR1.SSI from the copy, CFG2 from the copy" \
    "[ \"\$(grep -A1 -x READ-CR1 <<<\"\$console\" | tail -n1)\" = 0x00001000 ] && [ \"\$(grep -A1 -x READ-CFG2 <<<\"\$console\" | tail -n1)\" = 0x10000000 ]"
  local r; r() { grep -A1 -x "$1" <<<"$console" | tail -n1; }
  check "reads from the copies: IOLOCK from the latch (L2), CFG1 and CRCPOLY at reset, MASRX, IER.OVRIE (C2, C4)" \
    '[ "$(r READ-CR1-L2)" = 0x00001001 ] && [ "$(r READ-CFG1)" = 0x00070007 ] && [ "$(r READ-CRCPOLY)" = 0x00000107 ] && [ "$(r READ-CR1-MASRX)" = 0x00000100 ] && [ "$(r READ-IER)" = 0x00000041 ]'
}

t_fields() {
  need_check
  renode_in fields /repo/tests/fields.resc -e TVIBURI_MODEL_DIR=/repo/model
  check "every field outside the allow-list refuses by name, TVI-SPI-FIELD, kind unmodeled (gate clause 6)" "records '$work/fields.stdout' '
[(r[\"rule\"], r[\"kind\"], r[\"bit\"].split(\" (\")[0]) for r in recs] == [(\"TVI-SPI-FIELD\", \"unmodeled\", f if not f.startswith(\"SPI offset\") else \"no register at offset \" + f.split()[-1])
  for f in [l[8:].strip() for l in open(\"$src/tests/fields.resc\") if l.startswith(\"# FIELD \")]] and len(recs) == $(grep -c '^# FIELD ' "$src/tests/fields.resc")' \
      && grep -qx 'CHECK REFUSED' '$work/fields.stdout'"
  local console; console=$("$src/scripts/monitor-output" "$work/fields/renode.out")
  check "the field sweep reached its end" 'grep -qx TVIBURI-END <<<"$console"'
}

t_load() {
  need_check
  renode_in load /repo/tests/conditions.resc
  local accesses; accesses=$(grep -cE '^sysbus (Write|Read)[A-Za-z]* 0x4001' "$src/tests/conditions.resc")
  check "no model version at load: a TVI-LOAD refusal at seq 0, no witness, every SPI1 access refused (3)" \
    "grep -qx 'CHECK REFUSED' '$work/load.stdout' && ! grep -q 'tviburi\":\"witness' '$work/load/renode-log.txt' \
     && records '$work/load.stdout' 'recs[0][\"seq\"] == 0 and recs[0][\"bit\"] == \"none\" and len(recs) == $((accesses + 1)) and all(r[\"rule\"] == \"TVI-LOAD\" for r in recs)'"
}

t_crash() {
  need_check
  pod crash "$src" -v "$work/check-build:/check:ro,Z" -e TVIBURI_MODEL_DIR=/repo/model -- sh -c '
    printf "include @/repo/scripts/prelude.resc\nemulation RunFor \"100\"\nquit\n" >/out/long.resc
    renode --disable-gui --console --plain /out/long.resc </dev/null >/out/renode.out 2>&1 & pid=$!
    for i in $(seq 600); do grep -q "spi1: {\"tviburi\":\"witness\"" /out/renode-log.txt 2>/dev/null && break; sleep 0.1; done
    kill -9 $pid; wait $pid; echo renode-exit=$?
    /check/debug/tviburi-check $(/repo/tests/model-version /repo/model) /out/renode-log.txt spi1'
  check "a simulator killed after its witness does not PASS (1, no end record, the witness seen)" \
    "grep -qx 'renode-exit=137' '$work/crash.stdout' && grep -qx 'CHECK FAIL' '$work/crash.stdout' \
     && grep -q '^PROBLEM spi1: no end record' '$work/crash.stdout' && ! grep -q '^PROBLEM spi1: no load witness' '$work/crash.stdout'"
}

# --- Adversarial (WO §5): a refusal or a missing witness that still passes; a firmware-written
# line taken as a record; a route around the guard. Each must not PASS.

t_adv-removed() {
  fixture rm-guard scripts/prelude.resc 'include @/repo/model/GuardedSTM32H7_SPI.cs' ''
  fixture rm-runtime scripts/prelude.resc 'include @/repo/model/Refusal.cs' ''
  fixture renamed model/tviburi-nucleo-h753zi.repl 'Tviburi.GuardedSTM32H7_SPI' 'Tviburi.Missing'
  fixture no-overlay model/tviburi-nucleo-h753zi.repl 'spi1: Tviburi.GuardedSTM32H7_SPI @ {
        sysbus 0x40013000;
        rcc RegistrationPeripheralAlias.SPI1
    }' ''
  local f
  for f in rm-guard rm-runtime renamed no-overlay; do
    run "adv-$f" modeled "$work/$f.tree"
    check "guard removed ($f): the modeled path does not PASS (1)" '[ $rc -eq 1 ]'
  done
}

t_adv-extra-arg() {
  pod adv-extra-arg "$src" -- /repo/scripts/tviburi-spike modf --no-refuse
  check "an extra runner argument fails (1)" '[ $rc -eq 1 ]'
}

t_adv-bad-variant() {
  pod adv-bad-variant "$src" -- /repo/scripts/tviburi-spike "modf cfg2first"
  check "a variant that names no firmware path fails (1)" '[ $rc -eq 1 ]'
}

t_adv-env() {
  pod adv-env "$src" -e TVIBURI_MODEL_DIR=/nonexistent -- /repo/scripts/tviburi-spike modf
  check "TVIBURI_MODEL_DIR from the caller is overridden: MODF still REFUSED (3) by TVI-SPI-MODF" \
    "[ \$rc -eq 3 ] && records '$work/adv-env.stdout' 'len(recs) == 1 and recs[0][\"rule\"] == \"TVI-SPI-MODF\"'"
}

t_adv-hook() {
  fixture hook scripts/prelude.resc "$prelude_line" "$prelude_line
sysbus SetHookBeforePeripheralWrite spi1 \"value = value | 0x100\""
  run adv-hook modf "$work/hook.tree"
  check "a monitor write hook on spi1 is installed (it changed the value) and does not displace the guard: REFUSED (3)" \
    "[ \$rc -eq 3 ] && records '$work/adv-hook.stdout' 'len(recs) == 1 and recs[0][\"access\"][\"value\"] == \"0x101\"'"
}

t_adv-loglevel() {
  fixture loglevel-after scripts/prelude.resc "$prelude_line" "$prelude_line
logLevel 3"
  run adv-loglevel-modf modf "$work/loglevel-after.tree"
  check "logLevel 3 after load cannot hide a refusal (Error): REFUSED (3)" '[ $rc -eq 3 ]'
  run adv-loglevel-modeled modeled "$work/loglevel-after.tree"
  check "logLevel 3 after load hides the end record: the modeled path does not PASS (1)" '[ $rc -eq 1 ]'
  fixture loglevel-before scripts/prelude.resc 'emulation SetSeed 1' 'logLevel 3
emulation SetSeed 1'
  run adv-loglevel-before modeled "$work/loglevel-before.tree"
  check "logLevel 3 before load hides the witness: the modeled path does not PASS (1)" \
    "[ \$rc -eq 1 ] && grep -q '^PROBLEM spi1: no load witness' '$work/adv-loglevel-before.stdout'"
}

t_adv-spray() {
  run adv-spray spray
  check "record bytes written to SPI1 reach the log under spi1 (the test is not vacuous)" \
    "grep -q 'spi1: \[cpu: 0x[0-9A-F]*\] WriteUInt32 to 0x18 (InterruptStatusFlagsClear), value 0x7674227B' '$work/adv-spray/renode-log.txt'"
  check "...and none is taken as a record: the modeled path PASSes (0) with no PROBLEM" \
    "[ \$rc -eq 0 ] && ! grep -q '^PROBLEM' '$work/adv-spray.stdout'"
}

t_adv-version() {
  [ -d "$work/modeled" ] || run modeled modeled
  need_check
  pod adv-version "$src" -v "$work/check-build:/check:ro,Z" -v "$work/modeled:/run:ro,Z" -- sh -c '
    v=$(/repo/tests/model-version /repo/model); w=sha256:$(printf %064d 0)
    /check/debug/tviburi-check "$v" /run/renode-log.txt spi1 >/dev/null; echo right=$?
    /check/debug/tviburi-check "$w" /run/renode-log.txt spi1 >/dev/null; echo wrong=$?
    /check/debug/tviburi-check /run/renode-log.txt spi1 >/dev/null; echo none=$?'
  check "the check passes the modeled run only with its expected version: wrong (1) and none (1) fail (gate clause 7)" \
    "grep -qx right=0 '$work/adv-version.stdout' && grep -qx wrong=1 '$work/adv-version.stdout' && grep -qx none=1 '$work/adv-version.stdout'"
}

t_adv-recorded() {
  fixture recorded model/Refusal.cs 'private const string Mode = "enforcing";' 'private const string Mode = "recorded";'
  run adv-recorded modeled "$work/recorded.tree"
  check "a recorded-mode witness is never a pass: the modeled path FAILs (1) (gate clause 7; RUL-19)" \
    "[ \$rc -eq 1 ] && grep -q '^PROBLEM .*not in enforcing mode' '$work/adv-recorded.stdout'"
}

t_adv-noend() {
  fixture noend scripts/tviburi-spike 'echo "TVIBURI-END"
quit' 'no_such_monitor_command
echo "TVIBURI-END"
quit'
  run adv-noend modeled "$work/noend.tree"
  check "a script that stops before its end (Renode still disposes cleanly) does not PASS (1)" \
    "[ \$rc -eq 1 ] && grep -qx 'CHECK PASS' '$work/adv-noend.stdout'"
}

for t in ${TVIBURI_TESTS:-acceptance}; do
  case "$t" in
    acceptance) for u in $ACCEPTANCE; do "t_$u"; done ;;
    adversarial) for u in $ADVERSARIAL; do "t_$u"; done ;;
    all) for u in $ALL; do "t_$u"; done ;;
    *) case " $ALL " in *" $t "*) "t_$t" ;; *) echo "unknown test: $t" >&2; exit 2 ;; esac ;;
  esac
done

echo "outputs: $work"
[ $fails -eq 0 ] && echo "ALL PASS" || { echo "$fails FAILED"; exit 1; }
