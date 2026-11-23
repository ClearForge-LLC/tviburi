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
ACCEPTANCE="unit modf modeled stable cfg2first iolock forge stock-forge stock-modf nomark conditions load crash"
ADVERSARIAL="adv-removed adv-extra-arg adv-env adv-hook adv-loglevel adv-spray adv-noend"
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
    echo renode-exit=\$?; /check/debug/tviburi-check /out/renode-log.txt spi1"
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
  check "every rule branch refuses as declared, in order, and the pass cases do not" "records '$work/conditions.stdout' '
[(r[\"rule\"], r[\"bit\"], r[\"access\"][\"offset\"], r[\"access\"][\"width\"]) for r in recs] == [
 (\"TVI-SPI-MODF\", \"CFG2.MASTER (bit 22) = 0\", \"0x0\", 32),
 (\"TVI-SPI-MODF\", \"CFG2.SP[2:0] (bits 21:19) != 000\", \"0x0\", 32),
 (\"TVI-SPI-MODF\", \"CFG2.SSM (bit 26) = 0, CFG2.SSOE (bit 29) = 0\", \"0x0\", 32),
 (\"TVI-SPI-MODF\", \"CFG2.SSM (bit 26) = 0, CFG2.SSOE (bit 29) = 1\", \"0x0\", 32),
 (\"TVI-SPI-MODF\", \"CFG2.SSIOP (bit 28) = 1\", \"0x0\", 32),
 (\"TVI-SPI-MODF\", \"CR1.SSI (bit 12) = 0\", \"0x0\", 32),
 (\"TVI-SPI-MODF\", \"a master state other than the passing one, since reset: CR1=0x00000000, CFG2=0x04400000\", \"0x0\", 32),
 (\"TVI-SPI-CFG2-SPE\", \"CR1.SPE (bit 0) = 1\", \"0xC\", 32),
 (\"TVI-SPI-IOLOCK\", \"CR1.IOLOCK (bit 16) = 1\", \"0xC\", 32),
 (\"TVI-SPI-I2SMOD\", \"I2SCFGR.I2SMOD (bit 0) = 1\", \"0x50\", 32),
 (\"TVI-SPI-WIDTH\", \"sub-word access\", \"0x2\", 16),
 (\"TVI-SPI-WIDTH\", \"sub-word access\", \"0xE\", 8),
 (\"TVI-SPI-WIDTH\", \"sub-word access\", \"0x51\", 8),
 (\"TVI-SPI-MODF\", \"CFG2.MASTER (bit 22) = 0\", \"0x0\", 32)]' \
      && grep -qx 'CHECK REFUSED' '$work/conditions.stdout' && tr -d '\r' <'$work/conditions/renode.out' | grep -qx TVIBURI-END"
}

t_load() {
  need_check
  renode_in load /repo/tests/conditions.resc
  local writes; writes=$(grep -c '^sysbus Write' "$src/tests/conditions.resc")
  check "no model version at load: a TVI-LOAD refusal at seq 0, no witness, every write refused (3)" \
    "grep -qx 'CHECK REFUSED' '$work/load.stdout' && ! grep -q 'tviburi\":\"witness' '$work/load/renode-log.txt' \
     && records '$work/load.stdout' 'recs[0][\"seq\"] == 0 and recs[0][\"bit\"] == \"none\" and len(recs) == $((writes + 1)) and all(r[\"rule\"] == \"TVI-LOAD\" for r in recs)'"
}

t_crash() {
  need_check
  pod crash "$src" -v "$work/check-build:/check:ro,Z" -e TVIBURI_MODEL_DIR=/repo/model -- sh -c '
    printf "include @/repo/scripts/prelude.resc\nemulation RunFor \"100\"\nquit\n" >/out/long.resc
    renode --disable-gui --console --plain /out/long.resc </dev/null >/out/renode.out 2>&1 & pid=$!
    for i in $(seq 600); do grep -q "spi1: {\"tviburi\":\"witness\"" /out/renode-log.txt 2>/dev/null && break; sleep 0.1; done
    kill -9 $pid; wait $pid; echo renode-exit=$?
    /check/debug/tviburi-check /out/renode-log.txt spi1'
  check "a simulator killed after its witness does not PASS (1, no end record)" \
    "grep -qx 'renode-exit=137' '$work/crash.stdout' && grep -qx 'CHECK FAIL' '$work/crash.stdout' \
     && grep -q '^PROBLEM spi1: no end record' '$work/crash.stdout'"
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

t_adv-env() {
  pod adv-env "$src" -e TVIBURI_MODEL_DIR=/nonexistent -- /repo/scripts/tviburi-spike modf
  check "TVIBURI_MODEL_DIR from the caller is overridden: MODF still REFUSED (3)" '[ $rc -eq 3 ]'
}

t_adv-hook() {
  fixture hook scripts/prelude.resc "$prelude_line" "$prelude_line
sysbus SetHookBeforePeripheralWrite spi1 \"value = value\""
  run adv-hook modf "$work/hook.tree"
  check "a monitor write hook on spi1 does not displace the guard: REFUSED (3)" '[ $rc -eq 3 ]'
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
    "grep -q 'spi1: \[cpu: 0x[0-9A-F]*\] WriteUInt32 to 0x20 (TransmitData), value 0x7674227B' '$work/adv-spray/renode-log.txt'"
  check "...and none is taken as a record: the modeled path PASSes (0) with no PROBLEM" \
    "[ \$rc -eq 0 ] && ! grep -q '^PROBLEM' '$work/adv-spray.stdout'"
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
