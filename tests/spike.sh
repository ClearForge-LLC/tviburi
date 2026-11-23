#!/usr/bin/env bash
# tests/spike.sh [SRC] — the acceptance suite (TVI-WO-0100 onward; the release, TVI-WO-1200), run on the host.
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

IMAGE=ghcr.io/clearforge-llc/aethelgard-tools@sha256:1e0ab105ec42bdba66b773464f0b6679d622e4afdafef30ec4931e4326f49c63
ACCEPTANCE="unit modf modeled ardupilot generated field stable cfg2first iolock rmw forge stock-forge stock-modf nomark conditions fields load crash identity verify reproducible recorded"
ADVERSARIAL="adv-removed adv-extra-arg adv-bad-variant adv-hook adv-loglevel adv-spray adv-noend adv-version adv-recorded adv-noname adv-release adv-cache"
# TVI-WO-1300: the RCC guard's cases live in tests/1300/rcc.sh; each is a test here, rcc-<case>, run as part of the
# acceptance group, so CI and tests/mutants.sh reach them.
RCC_CASES=$(sed -n 's/^CASES="\(.*\)"$/\1/p' "$(dirname "$0")/1300/rcc.sh")
ACCEPTANCE="$ACCEPTANCE $(for c in $RCC_CASES; do printf 'rcc-%s ' "$c"; done)"
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

renode_in() { # renode_in <name> <tree> <resc> [podman args...]: the release, Renode on a monitor script, the check
  local name=$1 tree=$2 resc=$3; shift 3
  pod "$name" "$tree" -v "$work/check-build:/check:ro,Z" "$@" -- sh -c "v=\$(/repo/scripts/build-release) || exit 9; \
    timeout 120 renode --disable-gui --console --plain $resc </dev/null >/out/renode.out 2>&1; \
    echo renode-exit=\$?; /check/debug/tviburi-check \$v /out/renode-log.txt spi1 rcc"
}

version_of() { # version_of <name>: the release hash of the release a run built, sha256:<hex>
  echo "sha256:$(sha256sum <"$work/$1/release/MANIFEST" | cut -c1-64)"
}

spi1_records() { # spi1_records <name> <python expr over `recs`>: every spi1 record in a run's log, in order
  python3 -I -c '
import json, re, sys
recs = [json.loads(m.group(1)) for m in (re.search(r"\] spi1: (\{\"tviburi\".*)$", l.rstrip("\r\n")) for l in open(sys.argv[1])) if m]
sys.exit(0 if eval(sys.argv[2]) else 1)
' "$work/$1/renode-log.txt" "$2"
}

fixture() { # fixture <name> <file> <old> <new>: a copy of the source with one exact edit
  mkdir "$work/$1.tree"
  tar -C "$src" --exclude=.git -cf - . | tar -C "$work/$1.tree" -xf -
  python3 -I -c 'import sys; p,o,n=sys.argv[1:4]; s=open(p).read(); assert s.count(o)==1; open(p,"w").write(s.replace(o,n))' \
    "$work/$1.tree/$2" "$3" "$4"
}

prelude_line='machine LoadPlatformDescription @/repo/model/tviburi-nucleo-h753zi.repl'
# The platform's guarded spi1, and the stock model in its place (which has no `mode` property).
guarded_spi1='spi1: Tviburi.GuardedSTM32H7_SPI @ {
        sysbus 0x40013000;
        tviburiRcc RegistrationPeripheralAlias.SPI1
    }
    mode: "enforcing"'
# spi1's mode line in the board: unique, because the RCC guard's own mode line follows its hseFrequency, not a brace
spi1_mode='    }
    mode: "enforcing"'
stock_spi1='spi1: SPI.STM32H7_SPI @ {
        sysbus 0x40013000;
        tviburiRcc RegistrationPeripheralAlias.SPI1
    }'

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
    cargo test --offline --locked --manifest-path /repo/tools/tviburi-check/Cargo.toml --target-dir /out/target &&
    cargo test --offline --locked --manifest-path /repo/tools/tviburi-release/Cargo.toml --target-dir /out/target'
  check "reference check and release program: unit tests pass" '[ $rc -eq 0 ]'
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
  check "ArduPilot's SPI start, exchange and stop, as measured, PASS (0) in enforcing mode" '[ $rc -eq 0 ]'
  check "...and its enable carries MASRX by read-modify-write, as on silicon (CR1 0x101)" \
    "grep -q 'spi1: \[cpu: 0x[0-9A-F]*\] WriteUInt32 to 0x0 (Control1), value 0x101\$' '$work/ardupilot/renode-log.txt'"
}

t_generated() {
  run generated generated
  check "the generated firmware's SPI1 sequence PASSes (0) in enforcing mode" '[ $rc -eq 0 ]'
}

t_field() {
  run field field
  check "a planted field outside the allow-list (CFG2.CPOL=1) is REFUSED (3), TVI-SPI-FIELD, unmodeled" \
    "[ \$rc -eq 3 ] && records '$work/field.stdout' 'recs[0][\"seq\"] == 1 and recs[0][\"rule\"] == \"TVI-SPI-FIELD\" and recs[0][\"kind\"] == \"unmodeled\" and recs[0][\"bit\"] == \"SPI_CFG2.CPOL (bit 25) = 1\"'"
  # The halt lands at the end of the translation block (RUL-08), so the firmware's later SPI writes
  # also refuse; the first refusal is the verdict.
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
  [ -d "$work/stock.tree" ] || fixture stock model/tviburi-nucleo-h753zi.repl "$guarded_spi1" "$stock_spi1"
  run stock-forge forge "$work/stock.tree"
  check "stock model + forged witness and end on USART3: the forgery reaches the log" 'forged_in stock-forge'
  check "stock model + forged witness and end on USART3 does not PASS (1, no witness)" \
    "[ \$rc -eq 1 ] && grep -q '^PROBLEM spi1: no load witness' '$work/stock-forge.stdout'"
}

t_stock-modf() {
  [ -d "$work/stock.tree" ] || fixture stock model/tviburi-nucleo-h753zi.repl "$guarded_spi1" "$stock_spi1"
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
  renode_in conditions "$src" /repo/tests/conditions.resc
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
 (\"TVI-SPI-IOLOCK\", \"unmodeled\", \"CR1.IOLOCK (bit 16) = 1\", \"0xC\", 32),
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
 (\"TVI-SPI-ORDER\", \"unmodeled\", \"CR1.SPE (bit 0) = 0, TXDR written\", \"0x20\", 8),
 (\"TVI-SPI-ORDER\", \"unmodeled\", \"CR1.CSTART (bit 9) = 1 with SPE = 0\", \"0x0\", 32),
 (\"TVI-SPI-ORDER\", \"unmodeled\", \"CR1.CSTART (bit 9) = 1 with SPE = 0\", \"0x0\", 32),
 (\"TVI-SPI-ORDER\", \"unmodeled\", \"CR1.SPE (bit 0) = 1, CR2 written\", \"0x4\", 32),
 (\"TVI-SPI-MODF\", \"unmodeled\", \"CFG2.MASTER unknown: IFCR.MODFC written after a master state other than the passing one, and CFG2 not written since\", \"0x0\", 32),
 (\"TVI-SPI-READ\", \"unmodeled\", \"no register at offset 0x1C\", \"0x1C\", 32),
 (\"TVI-SPI-READ\", \"unmodeled\", \"write-only register\", \"0x18\", 32),
 (\"TVI-SPI-READ\", \"unmodeled\", \"write-only register\", \"0x20\", 8),
 (\"TVI-SPI-READ\", \"unmodeled\", \"SR.RXP (bit 0) = 0, RXDR read\", \"0x30\", 8),
 (\"TVI-SPI-READ\", \"unmodeled\", \"SR.EOT (bit 3) set by the stock model at CSUSP\", \"0x14\", 32),
 (\"TVI-SPI-ORDER\", \"unmodeled\", \"CR1.CSUSP (bit 10) = 1 with SPE = 0\", \"0x0\", 32)]' \
      && grep -qx 'CHECK REFUSED' '$work/conditions.stdout'"
  check "every predicted-fault the conditions produce has a bench row in the release's ledger, and every bench row is produced (RUL-18)" \
    "python3 -I - '$work/conditions.stdout' '$work/conditions/release/LEDGER.jsonl' <<'EOF'
import json, sys
faults = {(r['rule'], r['bit']) for r in (json.loads(l[8:]) for l in open(sys.argv[1]) if l.startswith('REFUSAL ')) if r['kind'] == 'predicted-fault'}
bench = {(r['rule'], r['bit']) for r in (json.loads(l) for l in list(open(sys.argv[2]))[1:]) if r['row'] == 'bench'}
print('predicted-fault', sorted(faults), 'bench', sorted(bench))
sys.exit(0 if faults and faults == bench else 1)
EOF
"
  local console; console=$("$src/scripts/monitor-output" "$work/conditions/renode.out")
  check "the conditions script reached its end" 'grep -qx TVIBURI-END <<<"$console"'
  check "reads return silicon's value: CR1.SSI from the copy, CFG2 from the copy" \
    "[ \"\$(grep -A1 -x READ-CR1 <<<\"\$console\" | tail -n1)\" = 0x00001000 ] && [ \"\$(grep -A1 -x READ-CFG2 <<<\"\$console\" | tail -n1)\" = 0x10000000 ]"
  local r; r() { grep -A1 -x "$1" <<<"$console" | tail -n1; }
  check "reads from the copies: IOLOCK from the latch (L2), CFG1 and CRCPOLY at reset, MASRX, IER.OVRIE (C2, C4)" \
    '[ "$(r READ-CR1-L2)" = 0x00001001 ] && [ "$(r READ-CFG1)" = 0x00070007 ] && [ "$(r READ-CRCPOLY)" = 0x00000107 ] && [ "$(r READ-CR1-MASRX)" = 0x00000100 ] && [ "$(r READ-IER)" = 0x00000041 ] && [ "$(r READ-IER-RESET)" = 0x00000000 ]'
}

t_fields() {
  # Gate clause 6 (RUL-17), with the cases generated from the release's ledger, not from a second list: each
  # field row's disallowed value refuses TVI-SPI-FIELD, unmodeled, naming the field; each allowed value passes
  # the allow-list; every offset with no register refuses. MASKED lists the cases where another rule, evaluated
  # before the allow-list, answers first; each must occur, so the list cannot go stale.
  need_check
  pod fields-cases "$src" -- sh -c '/repo/scripts/build-release >/dev/null &&
    /out/release.target/debug/tviburi-release cases /out/release/LEDGER.jsonl Tviburi.GuardedSTM32H7_SPI 0x40013000 0x400 >/out/cases.txt'
  { echo "include @/repo/scripts/prelude.resc"; cat "$work/fields-cases/cases.txt"; echo 'echo "TVIBURI-END"'; echo quit; } >"$work/fields.resc"
  renode_in fields "$src" /cases/fields.resc -v "$work/fields.resc:/cases/fields.resc:ro,Z"
  check "every ledger row: the disallowed value refuses by name, unmodeled; allowed values pass; no-register offsets refuse (gate clause 6)" \
    "python3 -I - '$work/fields.resc' '$work/fields.stdout' <<'EOF' &&
import json, sys
cases = {}
for l in open(sys.argv[1]):
    if l.startswith('# CASE '):
        kind, name, off, val = l.split()[2:6]
        cases[(int(off, 16), int(val, 16))] = (kind, name)
recs = [json.loads(l[8:]) for l in open(sys.argv[2]) if l.startswith('REFUSAL ')]
MASKED = {('SPI_CR1.CSUSP', 'TVI-SPI-ORDER'), ('SPI_CR1.CSTART', 'TVI-SPI-ORDER'), ('SPI_CR1.SPE', 'TVI-SPI-MODF'),
          ('SPI_TXDR.baseline', 'TVI-SPI-WIDTH'), ('SPI_TXDR.TXDR', 'TVI-SPI-WIDTH'), ('SPI_RXDR.baseline', 'TVI-SPI-WIDTH'),
          ('SPI_RXDR.RXDR', 'TVI-SPI-WIDTH'), ('SPI_I2SCFGR.I2SMOD', 'TVI-SPI-I2SMOD')}
seen, masked, bad = {}, set(), []
for r in recs:
    k = (int(r['access']['offset'], 16), int(r['access']['value'], 16))
    if k not in cases or k in seen:
        bad.append(('not one record per case', r['bit'])); continue
    seen[k] = r
    kind, name = cases[k]
    if (name, r['rule']) in MASKED:
        masked.add((name, r['rule']))
    elif kind == 'pass' or r['rule'] != 'TVI-SPI-FIELD' or r['kind'] != 'unmodeled':
        bad.append((kind, name, r['rule'], r['bit']))
    elif not (r['bit'].startswith(name + ' (') if name != 'none' else r['bit'] == 'no register at offset 0x%X' % k[0]):
        bad.append(('wrong field', name, r['bit']))
missing = [v for k, v in cases.items() if v[0] == 'refuse' and k not in seen]
print('fields: %d cases, %d refuse, %d records, %d masked' % (len(cases), sum(v[0] == 'refuse' for v in cases.values()), len(recs), len(masked)))
ok = not bad and not missing and masked == MASKED and len(cases) > 300
print(bad[:5], missing[:5], MASKED - masked) if not ok else None
sys.exit(0 if ok else 1)
EOF
grep -qx 'CHECK REFUSED' '$work/fields.stdout'"
  local console; console=$("$src/scripts/monitor-output" "$work/fields/renode.out")
  check "the field sweep reached its end" 'grep -qx TVIBURI-END <<<"$console"'
}

t_load() {
  need_check
  fixture badmode model/tviburi-nucleo-h753zi.repl "$spi1_mode" "${spi1_mode/enforcing/Recorded}"
  renode_in load "$work/badmode.tree" /repo/tests/conditions.resc
  local accesses; accesses=$(grep -cE '^sysbus (Write|Read)[A-Za-z]* 0x4001' "$src/tests/conditions.resc")
  check "mode \"Recorded\" in the platform: a TVI-LOAD refusal at seq 0, no witness, every SPI1 access refused (3) (gate clause 7)" \
    "grep -qx 'CHECK REFUSED' '$work/load.stdout' && ! grep -q 'spi1: {\"tviburi\":\"witness' '$work/load/renode-log.txt' \
     && records '$work/load.stdout' 'recs[0][\"seq\"] == 0 and recs[0][\"bit\"] == \"none\" and len(recs) == $((accesses + 1)) and all(r[\"rule\"] == \"TVI-LOAD\" for r in recs)'"
}

t_crash() {
  need_check
  pod crash "$src" -v "$work/check-build:/check:ro,Z" -- sh -c '
    v=$(/repo/scripts/build-release) || exit 9
    printf "include @/repo/scripts/prelude.resc\nemulation RunFor \"100\"\nquit\n" >/out/long.resc
    renode --disable-gui --console --plain /out/long.resc </dev/null >/out/renode.out 2>&1 & pid=$!
    for i in $(seq 600); do grep -q "spi1: {\"tviburi\":\"witness\"" /out/renode-log.txt 2>/dev/null && break; sleep 0.1; done
    kill -9 $pid; wait $pid; echo renode-exit=$?
    /check/debug/tviburi-check $v /out/renode-log.txt spi1 rcc'
  check "a simulator killed after its witness does not PASS (1, no end record, the witness seen)" \
    "grep -qx 'renode-exit=137' '$work/crash.stdout' && grep -qx 'CHECK FAIL' '$work/crash.stdout' \
     && grep -q '^PROBLEM spi1: no end record' '$work/crash.stdout' && ! grep -q '^PROBLEM spi1: no load witness' '$work/crash.stdout'"
}

t_identity() {
  # RUL-10, gate clauses 2 and 3: the witness reports the release hash, whatever the environment says, and one
  # changed byte in any model file or in the ledger's data changes it.
  [ -d "$work/modeled" ] || run modeled modeled
  local v; v=$(version_of modeled)
  check "the modeled run's witness and end report the release hash of the release it built" \
    "spi1_records modeled '[r[\"tviburi\"] for r in recs] == [\"witness\", \"end\"] and all(r[\"model_version\"] == \"$v\" for r in recs)'"
  pod id-otherdir "$src" -e TVIBURI_MODEL_DIR=/tmp -- /repo/scripts/tviburi-spike modeled
  need_check
  pod id-empty "$src" -v "$work/check-build:/check:ro,Z" -- sh -c 'v=$(/repo/scripts/build-release) || exit 9
    printf "include @/repo/scripts/prelude.resc\nquit\n" >/out/empty.resc; cd /out
    env -i /opt/renode/renode --disable-gui --console --plain /out/empty.resc </dev/null >/out/renode.out 2>&1
    /check/debug/tviburi-check $v /out/renode-log.txt spi1 rcc'
  check "the witness's version is the same with TVIBURI_MODEL_DIR at another directory (and the run PASSes), and with an empty environment" \
    "grep -qx 'VERDICT PASS' '$work/id-otherdir.stdout' && [ \$(version_of id-otherdir) = $v ] && spi1_records id-otherdir 'recs[0][\"model_version\"] == \"$v\"' \
     && [ \$(version_of id-empty) = $v ] && spi1_records id-empty 'recs[0][\"tviburi\"] == \"witness\" and recs[0][\"model_version\"] == \"$v\"'"
  fixture id-records model/Refusal.cs '// Tviburi records runtime' '// Tviburi records runtime.'
  fixture id-guard model/GuardedSTM32H7_SPI.cs "// Renode's STM32H7_SPI" "// Renode's STM32H7_SPI."
  local f
  for f in id-records id-guard; do
    run "$f" modeled "$work/$f.tree"
    check "one byte changed in a model file ($f): PASS (0) with a different release hash, which its witness reports" \
      "[ \$rc -eq 0 ] && [ \$(version_of $f) != $v ] && spi1_records $f 'recs[0][\"model_version\"] == \"'\$(version_of $f)'\"'"
  done
  fixture id-bench model/bench-checks.tsv 'read SR, CFG2, CR1' 'read SR, CFG2 and CR1'
  pod id-bench "$work/id-bench.tree" -- /repo/scripts/build-release
  fixture id-platform model/tviburi-nucleo-h753zi.repl '// Tviburi overlay' '// Tviburi'"'"'s overlay'
  pod id-platform "$work/id-platform.tree" -- /repo/scripts/build-release
  check "a source the release reads but does not ship (the platform's comment) is a source line: the release hash changes (as ruled 13:45Z)" \
    "[ \$(version_of id-platform) != $v ] && [ \"\$(grep '^overlay ' '$work/id-platform/release/MANIFEST')\" = \"\$(grep '^overlay ' '$work/modeled/release/MANIFEST')\" ]"
  check "one byte changed in the bench data (no C#): a different release hash, and different ledger rows" \
    "[ \$(version_of id-bench) != $v ] && ! cmp -s <(tail -n +2 '$work/id-bench/release/LEDGER.jsonl') <(tail -n +2 '$work/modeled/release/LEDGER.jsonl')"
}

t_verify() {
  # The consumer's binding check (RUL-10 (f)) on TVI-WO-1150 (iii)'s cases, for the one-file overlay. V2 is a
  # release whose model differs by one byte.
  [ -d "$work/id-records.tree" ] || fixture id-records model/Refusal.cs '// Tviburi records runtime' '// Tviburi records runtime.'
  pod verify "$src" -v "$work/id-records.tree:/repo2:ro,Z" -- sh -c '
    a=$(/repo/scripts/build-release) && b=$(/repo/scripts/build-release /repo2 /out/v2) || exit 9
    r=/out/release.target/debug/tviburi-release; ha=${a#sha256:}; hb=${b#sha256:}
    cp -r /out/release /out/A; $r verify /out/A $a >/out/A.txt; echo A=$?
    cp -r /out/release /out/B; sed -i "0,/records runtime/s//records runtimE/" /out/B/Tviburi.cs; $r verify /out/B $a >/out/B.txt; echo B=$?
    cp -r /out/release /out/C; sed -i "s/sha256:$ha\";/sha256:$hb\";/" /out/C/Tviburi.cs; $r verify /out/C $a >/out/C.txt; echo C=$?
    cp -r /out/v2 /out/E; sed -i "s/sha256:$hb\";/sha256:$ha\";/" /out/E/Tviburi.cs; $r verify /out/E $a >/out/E.txt; echo E=$?
    $r verify /out/E $b >/out/E2.txt; echo E2=$?
    cp -r /out/release /out/D; rm /out/D/Tviburi.cs; $r verify /out/D $a >/out/D.txt; echo D=$?
    cp -r /out/release /out/F; rm /out/F/tviburi-release.tar; sed -i "s/\"state\":\"inherited-unaudited\"/\"state\":\"guard\"/" /out/F/LEDGER.jsonl
    $r verify /out/F $a >/out/F.txt; echo F=$?
    grep -c "sha256:$hb\";" /out/C/Tviburi.cs /out/E/Tviburi.cs; grep -c "records runtimE" /out/B/Tviburi.cs'
  check "verify passes A and fails B (an edited model byte), C (a swapped constant), E (a stale manifest, either pin), D (no overlay), F (a ledger row edited, no tarball)" \
    "grep -qx A=0 '$work/verify.stdout' && grep -qx B=1 '$work/verify.stdout' && grep -qx C=1 '$work/verify.stdout' \
     && grep -qx E=1 '$work/verify.stdout' && grep -qx E2=1 '$work/verify.stdout' && grep -qx D=1 '$work/verify.stdout' \
     && grep -qx '/out/C/Tviburi.cs:1' '$work/verify.stdout' && grep -qx '/out/E/Tviburi.cs:0' '$work/verify.stdout' && grep -qx 1 '$work/verify.stdout' \
     && grep -q 'FAIL Tviburi.cs is not' '$work/verify/B.txt' && grep -q 'FAIL Tviburi.cs is not' '$work/verify/C.txt' \
     && grep -q 'FAIL MANIFEST sha256' '$work/verify/E.txt' && grep -q 'FAIL Tviburi.cs is not' '$work/verify/E2.txt' && grep -q 'FAIL Tviburi.cs' '$work/verify/D.txt' \
     && grep -qx F=1 '$work/verify.stdout' && grep -q \"FAIL LEDGER.jsonl's rows are not the manifest's\" '$work/verify/F.txt'"
}

t_reproducible() {
  # Two release builds from one commit, in two checkouts with different mtimes and umasks: byte-identical.
  local t
  for t in r1:022:200001010000 r2:077:203012312359; do
    IFS=: read -r name mask stamp <<<"$t"
    mkdir "$work/$name.tree"
    (umask "$mask" && tar -C "$src" --exclude=.git -cf - . | tar -C "$work/$name.tree" -xf - -m --no-same-permissions)
    find "$work/$name.tree" -exec touch -h -t "$stamp" {} +
    pod "$name" "$work/$name.tree" -- /repo/scripts/build-release
  done
  check "two release builds, two checkouts (umask 022 and 077, mtimes 2000 and 2030): tarball, manifest, overlay, ledger identical" \
    "for f in tviburi-release.tar MANIFEST Tviburi.cs LEDGER.jsonl; do cmp -s '$work/r1/release/'\$f '$work/r2/release/'\$f || exit 1; done"
}

t_recorded() {
  # RUL-19, gate clause 7: a recorded run logs would-refuse records, goes on, and is never a pass.
  [ -d "$work/recorded.tree" ] || fixture recorded model/tviburi-nucleo-h753zi.repl "$spi1_mode" "${spi1_mode/enforcing/recorded}"
  run recorded modf "$work/recorded.tree"
  check "modf, recorded: a recorded witness, would-refuse TVI-SPI-MODF predicted-fault first, the after-marker, an end; not a pass (1)" \
    "[ \$rc -eq 1 ] && grep -q 'after-marker=0xAF7E0002' '$work/recorded.stdout' && grep -qx 'CHECK FAIL' '$work/recorded.stdout' \
     && spi1_records recorded '(recs[0][\"tviburi\"] == \"witness\" and recs[0][\"mode\"] == \"recorded\" and recs[1][\"tviburi\"] == \"would-refuse\"
          and recs[1][\"rule\"] == \"TVI-SPI-MODF\" and recs[1][\"kind\"] == \"predicted-fault\" and recs[-1][\"tviburi\"] == \"end\"
          and not any(r[\"tviburi\"] == \"refusal\" for r in recs))'"
  need_check
  printf '%s\n' 'include @/repo/scripts/prelude.resc' 'sysbus WriteDoubleWord 0x4001300C 0x02000000' 'echo "READ-CFG2"' \
    'sysbus ReadDoubleWord 0x4001300C' 'sysbus WriteDoubleWord 0x40013020 0x41' 'sysbus WriteDoubleWord 0x40013000 0x400' \
    'echo "READ-SR"' 'sysbus ReadDoubleWord 0x40013014' 'echo "TVIBURI-END"' 'quit' >"$work/recorded-monitor.resc"
  renode_in recorded-monitor "$work/recorded.tree" /cases/r.resc -v "$work/recorded-monitor.resc:/cases/r.resc:ro,Z"
  local console; console=$("$src/scripts/monitor-output" "$work/recorded-monitor/renode.out")
  # SR: CSUSP with SPE = 0 (TVI-SPI-ORDER) goes on, so EOT is the suspend's, and the SR read refuses (TVI-SPI-READ);
  # recorded, it is served by the stock model, whose TXP is always 1, so it is not 0.
  check "recorded: refused accesses go on (CFG2's copy reads 0x02000000; the refused SR read is the stock value); one record per access" \
    "[ \"\$(grep -A1 -x READ-CFG2 <<<\"\$console\" | tail -n1)\" = 0x02000000 ] && grep -A1 -x READ-SR <<<\"\$console\" | tail -n1 | grep -qv '^0x00000000\$' \
     && spi1_records recorded-monitor '[(r[\"tviburi\"], r.get(\"rule\")) for r in recs] == [(\"witness\", None), (\"would-refuse\", \"TVI-SPI-FIELD\"), (\"would-refuse\", \"TVI-SPI-WIDTH\"), (\"would-refuse\", \"TVI-SPI-ORDER\"), (\"would-refuse\", \"TVI-SPI-READ\"), (\"end\", None)]'"
}

# --- Adversarial (WO §5): a refusal or a missing witness that still passes; a firmware-written
# line taken as a record; a route around the guard. Each must not PASS.

t_adv-removed() {
  fixture rm-guard scripts/prelude.resc 'include @/out/release/Tviburi.cs' ''
  fixture rm-runtime model/Refusal.cs 'public sealed class Records' 'public sealed class RecordsGone'
  fixture renamed model/tviburi-nucleo-h753zi.repl 'Tviburi.GuardedSTM32H7_SPI' 'Tviburi.Missing'
  fixture no-overlay model/tviburi-nucleo-h753zi.repl "$guarded_spi1" ''
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

t_adv-hook() {
  fixture hook scripts/prelude.resc "$prelude_line" "$prelude_line
sysbus SetHookBeforePeripheralWrite spi1 \"value = value | 0x100\""
  run adv-hook modf "$work/hook.tree"
  # The hook ORs 0x100 into every spi1 write; in CFG2 that is a reserved bit, so the first refusal is the
  # allow-list's, at the CFG2 write, carrying the hooked value.
  check "a monitor write hook on spi1 is installed (it changed the value) and does not displace the guard: REFUSED (3)" \
    "[ \$rc -eq 3 ] && records '$work/adv-hook.stdout' 'recs[0][\"rule\"] == \"TVI-SPI-FIELD\" and recs[0][\"access\"][\"value\"] == \"0x4400100\"'"
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
    v=sha256:$(sha256sum </run/release/MANIFEST | cut -c1-64); w=sha256:$(printf %064d 0)
    /check/debug/tviburi-check "$v" /run/renode-log.txt spi1 rcc >/dev/null; echo right=$?
    /check/debug/tviburi-check "$w" /run/renode-log.txt spi1 rcc >/dev/null; echo wrong=$?
    /check/debug/tviburi-check /run/renode-log.txt spi1 rcc >/dev/null; echo none=$?
    /check/debug/tviburi-check "$v" /run/renode-log.txt spi2 >/run-unnamed.txt; echo unnamed=$?; grep -c "not named" /run-unnamed.txt'
  check "the check passes the modeled run only with its expected version: wrong (1) and none (1) fail (gate clause 7)" \
    "grep -qx right=0 '$work/adv-version.stdout' && grep -qx wrong=1 '$work/adv-version.stdout' && grep -qx none=1 '$work/adv-version.stdout'"
  check "spi1's records, with spi2 named instead, are records from an unnamed source: FAIL (1)" \
    "grep -qx unnamed=1 '$work/adv-version.stdout' && grep -qxE '[1-9][0-9]*' '$work/adv-version.stdout'"
}

t_adv-recorded() {
  [ -d "$work/recorded.tree" ] || fixture recorded model/tviburi-nucleo-h753zi.repl "$spi1_mode" "${spi1_mode/enforcing/recorded}"
  run adv-recorded modeled "$work/recorded.tree"
  check "a recorded-mode witness is never a pass: the modeled path FAILs (1) (gate clause 7; RUL-19)" \
    "[ \$rc -eq 1 ] && grep -q '^PROBLEM .*not in enforcing mode' '$work/adv-recorded.stdout'"
}

t_adv-noend() {
  # The script quits cleanly without its end marker: Renode disposes (an end record), so the check
  # passes, and only the runner's own end-of-script evidence can fail the run.
  fixture noend scripts/tviburi-spike 'echo "TVIBURI-END"
quit' 'quit'
  run adv-noend modeled "$work/noend.tree"
  check "a script that ends before its end marker (Renode disposes cleanly) does not PASS (1)" \
    "[ \$rc -eq 1 ] && grep -qx 'CHECK PASS' '$work/adv-noend.stdout'"
  # A monitor error stops the script; Renode then exits on EOF, sometimes before it disposes (measured,
  # TVI-WO-0100b FEEDBACK), so the check may pass or fail; the run must not PASS either way.
  fixture noend-error scripts/tviburi-spike 'echo "TVIBURI-END"
quit' 'no_such_monitor_command
echo "TVIBURI-END"
quit'
  run adv-noend-error modeled "$work/noend-error.tree"
  check "a script stopped by a monitor error does not PASS (1)" '[ $rc -eq 1 ]'
}

t_adv-noname() {
  # RUL-10 (c): no model, script or test reads an environment variable, a path or the assembly's location as the
  # model's identity. The only TVIBURI_ name left is in the tests that set it to show it is ignored.
  check "no Environment, Assembly.Location or TVIBURI_MODEL_DIR in model/ or scripts/" \
    "! grep -rnE 'Environment\\.|Assembly\\.Location|GetExecutingAssembly|TVIBURI_MODEL_DIR' '$src/model' '$src/scripts'"
  check "the runner's only identity input is the release it builds (no model-version script)" "[ ! -e '$src/tests/model-version' ]"
}

t_adv-release() {
  # The release refuses, by name, what it would otherwise miss or misread (WO §5: a release that differs from what
  # compiled; a ledger that lies). Each tree is the source with one change; none may build.
  mk() { mkdir "$work/$1.tree"; tar -C "$src" --exclude=.git -cf - . | tar -C "$work/$1.tree" -xf -; }
  mk rel-symlink; ln -s GuardedSTM32H7_SPI.cs "$work/rel-symlink.tree/model/Linked.cs"
  mk rel-subdir; mkdir "$work/rel-subdir.tree/model/more"; cp "$src/model/Refusal.cs" "$work/rel-subdir.tree/model/more/"
  mk rel-case; printf 'namespace X { }\n' >"$work/rel-case.tree/model/Extra.CS"
  fixture rel-alias model/Refusal.cs 'using System.Text;' 'using Text = System.Text;'
  fixture rel-late model/Refusal.cs 'namespace Antmicro.Renode.Peripherals.Tviburi
{' 'namespace Antmicro.Renode.Peripherals.Tviburi
{
    using System.Linq;'
  fixture rel-row model/GuardedSTM32H7_SPI.cs '            R("SPI_CR1", 0x00, "§50.11.1", "HDDIR", 11, 11, Zero, Tag, "0", 2215),
' ''
  fixture rel-class model/GuardedSTM32H7_SPI.cs '"SSOM", 30, 30, Zero, Tag,' '"SSOM", 30, 30, Zero, "modelled",'
  fixture rel-form model/GuardedSTM32H7_SPI.cs '"MSSI", 3, 0, Zero, Tag, "0000 (no delay)", 2222),' '"MSSI", 3, 0, Zero, Tag, "0000 (no delay)", 2222), // a comment'
  fixture rel-pin tools/tviburi-release/pins.tsv 'b85cb4af7a7e' 'b85cb4af7a7f'
  fixture rel-template tools/tviburi-release/TviburiRelease.cs.template 'public static readonly string' 'public const string'
  fixture rel-bench model/bench-checks.tsv 'Tviburi.GuardedSTM32H7_SPI	B4' 'Tviburi.GuardedSTM32H7_RCC	B4'
  local f why
  for f in rel-symlink:'not a regular file' rel-subdir:'not a regular file' rel-case:'another case' rel-alias:'alias' \
      rel-late:'after a declaration' rel-row:'in no row' rel-class:'a stop' rel-form:'not one' rel-pin:'not the pinned' \
      rel-template:'exactly once' rel-bench:'not a guard'; do
    why=${f#*:}; f=${f%%:*}
    pod "$f" "$work/$f.tree" -- /repo/scripts/build-release
    check "the release refuses ($f): $why" "[ \$rc -ne 0 ] && grep -q 'REFUSED: .*$why' '$work/$f.stdout' && [ ! -e '$work/$f/release/MANIFEST' ]"
  done
  fixture rel-build tools/tviburi-release/pins.tsv 'aet1605' 'aet1606'
  run adv-rel-build modeled "$work/rel-build.tree"
  check "a release naming another Renode build than the image's does not run: FAIL (1)" \
    "[ \$rc -eq 1 ] && grep -q 'VERDICT FAIL (Renode is not the build the release names' '$work/adv-rel-build.stdout'"
}

t_adv-cache() {
  # RUL-10 (g): with the compiler cache ON (the consumer keeps it off), a second boot of a changed release must
  # not report the first release's constant: it reports its own, or it fails closed with no witness.
  [ -d "$work/id-records.tree" ] || fixture id-records model/Refusal.cs '// Tviburi records runtime' '// Tviburi records runtime.'
  need_check
  pod adv-cache "$src" -v "$work/check-build:/check:ro,Z" -v "$work/id-records.tree:/repo2:ro,Z" -- sh -c '
    a=$(/repo/scripts/build-release /repo /out/a) && b=$(/repo/scripts/build-release /repo2 /out/b) || exit 9
    mkdir -p /out/home/.config/renode
    printf "[general]\ncompiler-cache-enabled = True\n" >/out/home/.config/renode/config
    for r in a b; do
      rm -rf /out/release; cp -r /out/$r /out/release
      printf "include @/repo/scripts/prelude.resc\nquit\n" >/out/boot.resc
      HOME=/out/home renode --disable-gui --console --plain /out/boot.resc </dev/null >/out/$r.out 2>&1; echo boot-$r=$?
      mv /out/renode-log.txt /out/$r-log.txt
    done
    echo a=$a; echo b=$b; echo cached=$(find /out/home/.config/renode/compiler-cache -type f | wc -l)'
  check "cache on, second boot of a changed release: its witness reports its own hash, or there is no witness" \
    "b=\$(sed -n 's/^b=//p' '$work/adv-cache.stdout'); a=\$(sed -n 's/^a=//p' '$work/adv-cache.stdout'); [ -n \"\$b\" ] && [ \"\$a\" != \"\$b\" ] \
     && grep -q \"spi1: {.tviburi.:.witness.*\$a\" '$work/adv-cache/a-log.txt' && grep -qE '^cached=[1-9]' '$work/adv-cache.stdout' \
     && ! grep -q \"spi1: {.tviburi.:.witness.*\$a\" '$work/adv-cache/b-log.txt'"
  # Measured, and stronger than fail-closed: one file means a new constant is a new compilation unit, so the
  # cache holds nothing to reuse, and the second boot reports its own release.
  check "...and it in fact reports its own release" "grep -q \"spi1: {.tviburi.:.witness.*\$(sed -n 's/^b=//p' '$work/adv-cache.stdout')\" '$work/adv-cache/b-log.txt'"
}

t_rcc() { # t_rcc <case>: one case of tests/1300/rcc.sh on this source, its ok/FAIL lines counted here
  local out rrc=0
  out=$(TVIBURI_TESTS="$1" TVIBURI_SCRATCH="$work" "$src/tests/1300/rcc.sh" "$src" 2>&1) || rrc=$?
  grep -E '^(ok|FAIL) ' <<<"$out" || true
  local f; f=$(grep -c '^FAIL ' <<<"$out" || true)
  if [ "$rrc" -ne 0 ] && [ "$f" -eq 0 ]; then
    echo "FAIL rcc-$1: tests/1300/rcc.sh exited $rrc with no verdict"; f=1
  fi
  fails=$((fails + f))
}
for c in $RCC_CASES; do eval "t_rcc-$c() { t_rcc $c; }"; done

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
