#!/usr/bin/env bash
# tests/spike.sh [SRC] — the acceptance suite (TVI-WO-0100 onward; the release, TVI-WO-1200), run on the host.
# SRC defaults to a `git archive HEAD` export of this repository, so what is tested is what is
# committed. Each run is one container in the pinned tools image, with no network and the source
# mounted read-only. Outputs go under ${TVIBURI_SCRATCH:-$HOME/.cache/tviburi}.
#
#   TVIBURI_TESTS="modf conditions ..."  run only these tests, or a group: "acceptance" (the default),
#                                        "adversarial" (tests/adversarial.sh) or "all". After each test returns,
#                                        a line `RAN <test>` (TVI-WO-1202: a CI shard's record of what it ran).
#   TVIBURI_TESTS=list                   print every test of "all", one per line, in the suite's order, and run
#                                        nothing (TVI-WO-1202: the tree's own list, for the shard plan and the aggregate).
#   TVIBURI_CI=1                         run containers with plain `podman run`. Only for a
#                                        disposable CI runner; elsewhere ~/bin/capped-run is required.
# Exit 0 only if every selected test passed.
set -euo pipefail

IMAGE=ghcr.io/clearforge-llc/aethelgard-tools@sha256:1e0ab105ec42bdba66b773464f0b6679d622e4afdafef30ec4931e4326f49c63
ACCEPTANCE="unit modf modeled ardupilot generated field stable cfg2first iolock rmw forge stock-forge stock-modf modf-page removed-console crash-console crash-runner nomark conditions fields load crash identity verify reproducible manifest lognames guards teardown witnessed recorded allowlist"
ADVERSARIAL="adv-removed adv-extra-arg adv-bad-variant adv-hook adv-loglevel adv-spray adv-noend adv-version adv-recorded adv-noname adv-release adv-cache"
# TVI-WO-1300: the RCC guard's cases live in tests/1300/rcc.sh; each is a test here, rcc-<case>, run as part of the
# acceptance group, so CI and tests/mutants.sh reach them.
RCC_CASES=$(sed -n 's/^CASES="\(.*\)"$/\1/p' "$(dirname "$0")/1300/rcc.sh")
ACCEPTANCE="$ACCEPTANCE $(for c in $RCC_CASES; do printf 'rcc-%s ' "$c"; done)"
# TVI-WO-1301: DS12117 Rev 10's AF table against ST's open pin data, every disagreement ruled (tools/tviburi-afcheck).
ACCEPTANCE="$ACCEPTANCE afcheck"
# TVI-WO-1301: the GPIO guard's cases live in tests/1301/gpio.sh; each is a test here, gpio-<case>, in the acceptance group.
GPIO_CASES=$(sed -n 's/^CASES="\(.*\)"$/\1/p' "$(dirname "$0")/1301/gpio.sh")
ACCEPTANCE="$ACCEPTANCE $(for c in $GPIO_CASES; do printf 'gpio-%s ' "$c"; done)"
# TVI-WO-1302: the timer guard's cases live in tests/1302/timer.sh; each is a test here, timer-<case>, in the acceptance group.
TIMER_CASES=$(sed -n 's/^CASES="\(.*\)"$/\1/p' "$(dirname "$0")/1302/timer.sh")
ACCEPTANCE="$ACCEPTANCE $(for c in $TIMER_CASES; do printf 'timer-%s ' "$c"; done)"
# TVI-WO-1303: the DMA guard's cases live in tests/1303/dma.sh; each is a test here, dma-<case>, in the acceptance group.
DMA_CASES=$(sed -n 's/^CASES="\(.*\)"$/\1/p' "$(dirname "$0")/1303/dma.sh")
ACCEPTANCE="$ACCEPTANCE $(for c in $DMA_CASES; do printf 'dma-%s ' "$c"; done)"
# TVI-WO-1304: the flash guard's cases live in tests/1304/flash.sh; each is a test here, flash-<case>, in the acceptance group.
FLASH_CASES=$(sed -n 's/^CASES="\(.*\)"$/\1/p' "$(dirname "$0")/1304/flash.sh")
ACCEPTANCE="$ACCEPTANCE $(for c in $FLASH_CASES; do printf 'flash-%s ' "$c"; done)"
# TVI-WO-1305: the USART guard's cases live in tests/1305/usart.sh; each is a test here, usart-<case>, in the acceptance group.
USART_CASES=$(sed -n 's/^CASES="\(.*\)"$/\1/p' "$(dirname "$0")/1305/usart.sh")
ACCEPTANCE="$ACCEPTANCE $(for c in $USART_CASES; do printf 'usart-%s ' "$c"; done)"
# TVI-WO-1306: the I2C guard's cases live in tests/1306/i2c.sh; each is a test here, i2c-<case>, in the acceptance group.
I2C_CASES=$(sed -n 's/^CASES="\(.*\)"$/\1/p' "$(dirname "$0")/1306/i2c.sh")
ACCEPTANCE="$ACCEPTANCE $(for c in $I2C_CASES; do printf 'i2c-%s ' "$c"; done)"
# TVI-WO-1307: the ADC guard's cases live in tests/1307/adc.sh; each is a test here, adc-<case>, in the acceptance group.
ADC_CASES=$(sed -n 's/^CASES="\(.*\)"$/\1/p' "$(dirname "$0")/1307/adc.sh")
ACCEPTANCE="$ACCEPTANCE $(for c in $ADC_CASES; do printf 'adc-%s ' "$c"; done)"
# TVI-WO-1308: the DMAMUX1 guard's cases live in tests/1308/dmamux.sh; each is a test here, dmamux-<case>, in the acceptance group.
DMAMUX_CASES=$(sed -n 's/^CASES="\(.*\)"$/\1/p' "$(dirname "$0")/1308/dmamux.sh")
ACCEPTANCE="$ACCEPTANCE $(for c in $DMAMUX_CASES; do printf 'dmamux-%s ' "$c"; done)"
# TVI-WO-1308: the PWR guard's cases live in tests/1308/pwr.sh; each is a test here, pwr-<case>, in the acceptance group.
PWR_CASES=$(sed -n 's/^CASES="\(.*\)"$/\1/p' "$(dirname "$0")/1308/pwr.sh")
ACCEPTANCE="$ACCEPTANCE $(for c in $PWR_CASES; do printf 'pwr-%s ' "$c"; done)"
ALL="$ACCEPTANCE $ADVERSARIAL"
if [ "${TVIBURI_TESTS:-}" = list ]; then printf '%s\n' $ALL; exit 0; fi
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
    echo renode-exit=\$?; /check/debug/tviburi-check \$v /out/renode-log.txt \$(/repo/scripts/guards)"
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

t_afcheck() { # TVI-WO-1301 (1): its unit tests (each a planted change it must catch), then the committed data
  pod afcheck "$src" -- sh -c 'export PATH=/opt/cargo/bin:$PATH RUSTUP_HOME=/opt/rustup CARGO_HOME=/out/cargo-home;
    cargo test --offline --locked --manifest-path /repo/tools/tviburi-afcheck/Cargo.toml --target-dir /out/target &&
    cargo run --quiet --offline --locked --manifest-path /repo/tools/tviburi-afcheck/Cargo.toml --target-dir /out/target -- \
      /repo/model/ds12117-rev10-table9.tsv /repo/tests/1301/fixtures/st-open-pin-data/GPIO-STM32H747_gpio_v1_0_Modes.xml \
      /repo/tests/1301/st-aliases.tsv /repo/tests/1301/st-ruled.tsv'
  check "afcheck: the AF table agrees with ST's open pin data but for the ruled disagreements" '[ $rc -eq 0 ]'
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
  # Gate clause 4's stock half, by what it claims (TVI-WO-1203 (3)): stock Renode runs the MODF firmware past the faulting
  # write to its end (the after-the-last-write marker, Renode exit 0), and the run fails only because no guard witnessed it.
  check "stock Renode passes the MODF path: its two writes are the MODF firmware's (CFG2 = MASTER|SSM, then CR1 = SPE with SSI = 0, p2184's trigger), the firmware reaches its end marker 0xAF7E0002 with Renode exit 0, and the run's only problems are spi1's missing witness and end (gate clause 4)" \
    "grep -qx 'VERDICT FAIL (check exit 1, renode exit 0, after-marker=0xAF7E0002)' '$work/stock-modf.stdout' \
     && [ \"\$(grep '^PROBLEM' '$work/stock-modf.stdout' | cut -d: -f1-2 | paste -sd'|')\" = 'PROBLEM spi1: no load witness|PROBLEM spi1: no end record' ] \
     && [ \"\$(grep -a 'spi1: \[cpu: [^]]*\] Write' '$work/stock-modf/renode-log.txt' | sed 's/.*Write[A-Za-z0-9]* to \(0x[0-9A-F]*\) .*value \(0x[0-9A-F]*\)\$/\1=\2/' | paste -sd' ')\" = '0xC=0x4400000 0x0=0x1' ]"
}

t_modf-page() {
  # Gate clause 4's page match, by a test (TVI-WO-1203 (3)): the MODF refusal's claims, each against the held RM0433 Rev 9 text's
  # own lines on p2184 (tests/1203/rm0433-rev9-p2184.txt, regenerated and compared with the held text by
  # tests/1203/rm0433-p2184.sh): its section, its trigger, and the three effects its condition names.
  run modf-page modf
  check "the MODF refusal's citation, trigger and three effects each match RM0433 Rev 9 p2184's own text, its section heading and the page's footer (gate clause 4)" \
    "[ \$rc -eq 3 ] && python3 -I - '$work/modf-page.stdout' '$src/tests/1203/rm0433-rev9-p2184.txt' <<'EOF'
import json, re, sys
r = [json.loads(l[8:]) for l in open(sys.argv[1]) if l.startswith('REFUSAL ')][0]
lines = [l for l in open(sys.argv[2], encoding='utf-8').read().splitlines() if not l.startswith('#')]
page = ' '.join(' '.join(lines).split())
pairs = [(r['citation'], 'RM0433 Rev 9 §50.5.2 p2184', page, '50.5.2 SPI error flags'),
         (r['bit'], 'CR1.SSI (bit 12) = 0', page, 'SSI bit in slave select software mode) pulled low'),
         (r['condition'], 'SSM=1, SSI=0', page, 'Mode fault occurs when the master device'),
         (r['condition'], 'sets SR.MODF', page, 'The MODF bit is set'),
         (r['condition'], 'forces CR1.SPE to 0', page, 'The SPE bit is forced to zero'),
         (r['condition'], 'clears CFG2.MASTER', page, 'The MASTER bit is cleared')]
bad = [(c, p) for rec, c, pg, p in pairs if c not in rec or p not in pg]
foot = re.fullmatch(r'\s*2184/3329\s+RM0433 Rev 9\s*', lines[-1])
print('modf-page:', r['rule'], r['kind'], 'unmatched', bad, 'footer', bool(foot))
sys.exit(0 if r['rule'] == 'TVI-SPI-MODF' and r['kind'] == 'predicted-fault' and not bad and foot else 1)
EOF"
}

# Gate clause 3's cells in Aethelgard's console framing (TVI-WO-1203 (2)): the firmware on a tree's board under Aethelgard's
# committed renode.config (the fixture's, RUL-11 (a): the virtual stamp, one space), the console captured, and the records-only
# check run on that console, never on the log file.
aet_cfg=$(ls -d "$src"/tests/1301/fixtures/aet-1c32c8ea-ardupilot/varlib/*/tool)
aet_console() { # aet_console <name> <tree> <variant> [kill]: kill: SIGKILL Renode once spi1's witness is on the console
  pod "$1" "$2" -v "$aet_cfg/renode.config:/cfg/renode.config:ro,Z" -e VARIANT="$3" -e KILL="${4:-}" -- bash -c '
    export PATH=/opt/cargo/bin:$PATH RUSTUP_HOME=/opt/rustup CARGO_HOME=/out/cargo-home
    v=$(/repo/scripts/build-release) || exit 9
    cargo build --quiet --offline --locked --manifest-path /repo/tools/tviburi-check/Cargo.toml --target-dir /out/target || exit 8
    vs="modeled modf cfg2first iolock forge spray rmw ardupilot generated field"
    rustc --edition 2021 --target thumbv7em-none-eabihf -C opt-level=s -C panic=abort -C debuginfo=0 --cfg "path=\"$VARIANT\"" \
      --check-cfg "cfg(path, values(\"${vs// /\",\"}\"))" -C link-arg=-T/repo/firmware/link.x -o /out/fw.elf /repo/firmware/spi1.rs || exit 7
    mkdir -p /tmp/aet && cp /cfg/renode.config /tmp/aet/renode.config
    sed -i "s|^history-path = .*|history-path = /tmp/aet/history|" /tmp/aet/renode.config
    printf "include @/repo/scripts/prelude.resc\nsysbus LoadELF @/out/fw.elf\ncpu VectorTableOffset 0x08000000\nemulation RunFor \"0.001\"\necho \"TVIBURI-END\"\nquit\n" >/out/run.resc
    if [ -n "$KILL" ]; then
      # R3 (ruled 10:15Z): the killed run never reaches quit first. The firmware spins after its last write (firmware/spi1.rs
      # ends in a loop), and 100 s of virtual time takes far longer than the 0.1 s the poll needs to see the witness.
      printf "include @/repo/scripts/prelude.resc\nsysbus LoadELF @/out/fw.elf\ncpu VectorTableOffset 0x08000000\nemulation RunFor \"100\"\n" >/out/run.resc
      renode --config /tmp/aet/renode.config --disable-gui --console --plain /out/run.resc </dev/null >/out/renode.out 2>&1 & pid=$!
      for i in $(seq 1200); do grep -aq "^.\?\[[0-9:.]*\] \[INFO\] spi1: {\"tviburi\":\"witness\".*}.\?$" /out/renode.out && { echo witness-seen-before-kill; break; }; sleep 0.1; done
      kill -9 $pid; wait $pid; echo "renode-exit=$?"
    else
      timeout 120 renode --config /tmp/aet/renode.config --disable-gui --console --plain /out/run.resc </dev/null >/out/renode.out 2>&1
      echo "renode-exit=$?"
    fi
    /out/target/debug/tviburi-check "$v" /out/renode.out $(/repo/scripts/guards)'
}
aet_framed() { # aet_framed <name> <source>: a witness of <source> on the console in Aethelgard's framing (the check read that framing)
  grep -aq "^.\?\[[0-9][0-9]:[0-9][0-9]:[0-9][0-9]\.[0-9]\{9\}\] \[INFO\] $2: {\"tviburi\":\"witness\"" "$work/$1/renode.out"
}

t_removed-console() {
  # A removed guard (spi1 stock, as in stock-forge), at the runner and at the records-only check reading Aethelgard's console.
  [ -d "$work/stock.tree" ] || fixture stock model/tviburi-nucleo-h753zi.repl "$guarded_spi1" "$stock_spi1"
  run removed-runner modeled "$work/stock.tree"
  check "removed guard, at Tviburi's runner: the modeled path does not PASS (1), no load witness for spi1" \
    "[ \$rc -eq 1 ] && grep -q '^PROBLEM spi1: no load witness' '$work/removed-runner.stdout'"
  aet_console removed-console "$work/stock.tree" modeled
  check "removed guard, at the records-only check on Aethelgard's console (virtual stamp, one space): CHECK FAIL, and its only problems are spi1's missing witness and end, so every other guard's witness and end were read from that framing; Renode exit 0 (gate clause 3)" \
    "grep -qx 'renode-exit=0' '$work/removed-console.stdout' && grep -qx 'CHECK FAIL' '$work/removed-console.stdout' \
     && [ \"\$(grep '^PROBLEM' '$work/removed-console.stdout' | cut -d: -f1-2 | paste -sd'|')\" = 'PROBLEM spi1: no load witness|PROBLEM spi1: no end record' ] \
     && aet_framed removed-console rcc && ! grep -aq 'spi1: {\"tviburi\"' '$work/removed-console/renode.out'"
}

t_crash-console() {
  # A crash: Renode killed (SIGKILL, exit 137) once spi1's witness is on Aethelgard's console, and the check on that console.
  aet_console crash-console "$src" modeled kill
  check "crash, at the records-only check on Aethelgard's console: Renode killed (137) by the case once spi1's witness was on the console (seen before the kill, not after 120 s), which the check reads; CHECK FAIL, no end record (gate clause 3)" \
    "grep -qx 'witness-seen-before-kill' '$work/crash-console.stdout' && grep -qx 'renode-exit=137' '$work/crash-console.stdout' && aet_framed crash-console spi1 && grep -qx 'CHECK FAIL' '$work/crash-console.stdout' \
     && grep -q '^PROBLEM spi1: no end record' '$work/crash-console.stdout' && ! grep -q '^PROBLEM spi1: no load witness' '$work/crash-console.stdout'"
}

t_crash-runner() {
  # A crash at Tviburi's own runner, unchanged: the board's prelude hooks spi1's first write to kill Renode (SIGKILL) there,
  # after every witness and before any end record. The hook flushes the logger first: the log file is written in blocks, and a
  # kill with the witnesses still buffered would fail the run on "no load witness", a removed guard's reason, not a crash's.
  # The runner must not PASS, and must report Renode's 137, not a timeout's 124.
  fixture crash-runner scripts/prelude.resc "$prelude_line" "$prelude_line
sysbus SetHookBeforePeripheralWrite spi1 \"(__import__('Antmicro.Renode.Logging').Renode.Logging.Logger.Flush(), __import__('os').kill(__import__('os').getpid(), 9))\""
  run crash-runner modeled "$work/crash-runner.tree"
  check "crash, at Tviburi's runner: Renode killed at spi1's first write (exit 137, not a timeout), after its witness: the run does not PASS (1); the check on its log finds no end record (gate clause 3)" \
    "[ \$rc -eq 1 ] && grep -qx 'VERDICT FAIL (check exit 1, renode exit 137, after-marker=none)' '$work/crash-runner.stdout' \
     && grep -q '^PROBLEM spi1: no end record' '$work/crash-runner.stdout' && ! grep -q '^PROBLEM spi1: no load witness' '$work/crash-runner.stdout'"
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
 (\"TVI-SPI-ORDER\", \"unmodeled\", \"CR1.CSUSP (bit 10) = 1 with SPE = 0\", \"0x0\", 32),
 (\"TVI-SPI-ENABLE\", \"predicted-fault\", \"RCC_APB2ENR.SPI1EN = 0\", \"0xC\", 32),
 (\"TVI-SPI-ENABLE\", \"predicted-fault\", \"RCC_APB2ENR.SPI1EN = 0\", \"0xC\", 32),
 (\"TVI-SPI-KERNEL\", \"predicted-fault\", \"RCC_D2CCIP1R.SPI123SEL = 000\", \"0x0\", 32),
 (\"TVI-SPI-KERNEL\", \"predicted-fault\", \"RCC_D2CCIP1R.SPI123SEL = 100\", \"0x0\", 32)]' \
      && grep -qx 'CHECK REFUSED' '$work/conditions.stdout'"
  check "every predicted-fault the conditions produce has a bench row in the release's ledger, and every bench row is produced (RUL-18)" \
    "python3 -I - '$work/conditions.stdout' '$work/conditions/release/LEDGER.jsonl' <<'EOF'
import json, sys
faults = {(r['rule'], r['bit']) for r in (json.loads(l[8:]) for l in open(sys.argv[1]) if l.startswith('REFUSAL ')) if r['kind'] == 'predicted-fault'}
bench = {(r['rule'], r['bit']) for r in (json.loads(l) for l in list(open(sys.argv[2]))[1:]) if r['row'] == 'bench' and r['peripheral'] == 'Tviburi.GuardedSTM32H7_SPI'}
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

t_allowlist() { # TVI-WO-1300a (6): the RCC allow-list and its field sweep regenerate from the committed tables, byte for byte
  local out; rc=0; out=$(python3 -I "$src/tests/1300/allowlist.py" check 2>&1) || rc=$?
  echo "$out" | tail -n 3
  check "the RCC allow-list (the guard's Fields) and tests/1300/fields.resc regenerate from tests/1300/data, byte for byte" '[ $rc -eq 0 ]'
}

t_fields() {
  # Gate clause 6 (RUL-17), with the cases generated from the release's ledger, not from a second list: each
  # field row's disallowed value refuses TVI-SPI-FIELD, unmodeled, naming the field; each allowed value passes
  # the allow-list; every offset with no register refuses. MASKED lists the cases where another rule, evaluated
  # before the allow-list, answers first; each must occur, so the list cannot go stale.
  need_check
  pod fields-cases "$src" -- sh -c '/repo/scripts/build-release >/dev/null &&
    /out/release.target/debug/tviburi-release cases /out/release/LEDGER.jsonl Tviburi.GuardedSTM32H7_SPI 0x40013000 0x400 0x580244F0 0x1000 >/out/cases.txt'
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
    # the whole witness line, its closing brace included: the log is flushed in blocks, and a kill inside the line leaves it
    # malformed (TVI-WO-1304: after the DMA and flash witnesses, spi1'"'"'s line straddled a block on every run)
    for i in $(seq 600); do grep -q "spi1: {\"tviburi\":\"witness\".*}\$" /out/renode-log.txt 2>/dev/null && break; sleep 0.1; done
    kill -9 $pid; wait $pid; echo renode-exit=$?
    /check/debug/tviburi-check $v /out/renode-log.txt $(/repo/scripts/guards)'
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
    /check/debug/tviburi-check $v /out/renode-log.txt $(/repo/scripts/guards)'
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

t_manifest() { # TVI-WO-1203 (1): the MANIFEST restates or retypes onto a guard only an instance a committed replay runs under it
  # The release program is the check (RUL-14; R1, ruled 2026-11-26T10:15Z): it refuses a restated instance no committed
  # model/stm32h7-*-restate.repl declares under its guard, a restatement the committed replay does not load, and a retyped
  # instance the consumer's committed platform does not declare or the replay does not re-type. Here: it passes on this tree,
  # and refuses each planted break by name.
  pod manifest "$src" -- sh -c '/repo/scripts/build-release >/dev/null'
  check "the release builds, restating and retyping onto guards (each instance checked by the release program itself)" \
    "[ \$rc = 0 ] && grep -q '^restate ' '$work/manifest/release/MANIFEST' && grep -q '^retype ' '$work/manifest/release/MANIFEST'"
  # TVI-WO-2100 Q25: each build line with its assembly's sha256.
  check "the MANIFEST (format 2) carries each build line with its string and sha256" \
    "grep -qx 'tviburi-release-manifest 2' '$work/manifest/release/MANIFEST' \
     && [ \"\$(grep '^build ' '$work/manifest/release/MANIFEST')\" = \"\$(grep '^build' '$src/tools/tviburi-release/pins.tsv' | tr '\t' ' ')\" ] \
     && [ \$(grep -cE '^build [a-z]+ [^ ]+ [0-9a-f]{64}\$' '$work/manifest/release/MANIFEST') = 2 ]"
  local row=$'Tviburi.GuardedSTM32_Timer\ttimer1 timer8\t-' spi=$'Tviburi.GuardedSTM32H7_SPI\t-\tspi1 spi2'
  planted() { # planted <name> <file> <old> <new> <expected message>: a tree with one edit, whose release must refuse by that message
    fixture "manifest-$1" "$2" "$3" "$4"
    pod "manifest-$1" "$work/manifest-$1.tree" -- sh -c '/repo/scripts/build-release'
    [ "$rc" != 0 ] && grep -qF -- "$5" "$work/manifest-$1.stdout"
  }
  check "planted: the class row names timer5, which no committed restatement runs guarded: the release refuses, naming it" \
    "planted timer5 tools/tviburi-release/pins.tsv \"\$row\" $'Tviburi.GuardedSTM32_Timer\ttimer1 timer5 timer8\t-' 'restates timer5, which no committed restatement'"
  # timer5 again, with every form of a restatement that Renode would not load as the guard: commented out (block and one line), its
  # `as` in a trailing comment, an entry cancelled after, and a variable that is not one identifier (adversarial pass, 2a).
  fixture manifest-commented tools/tviburi-release/pins.tsv "$row" $'Tviburi.GuardedSTM32_Timer\ttimer1 timer5 timer8\t-'
  printf '%s\n' '' '// tviburiTimer5: Tviburi.GuardedSTM32_Timer @ {' '//         sysbus 0x40000C00;' '//     } as "timer5"' \
    '// tviburiTimer5: Tviburi.GuardedSTM32_Timer @ sysbus 0x40000C00 as "timer5"' \
    'tviburiTimer5: Tviburi.GuardedSTM32_Timer @ sysbus 0x40000C00 // as "timer5"' \
    'tviburiTim5: Tviburi.GuardedSTM32_Timer @ sysbus 0x40000C00 as "timer5"' 'tviburiTim5: @none' \
    'tviburi Timer5: Tviburi.GuardedSTM32_Timer @ sysbus 0x40000C00 as "timer5"' \
    >>"$work/manifest-commented.tree/model/stm32h7-timer-restate.repl"
  pod manifest-commented "$work/manifest-commented.tree" -- sh -c '/repo/scripts/build-release'
  check "planted: a restatement of timer5 commented out (block, one line), with its name only in a comment, cancelled after, or under a variable that is not one identifier, does not qualify it: the release refuses, naming it" \
    "[ \$rc != 0 ] && grep -q 'restates timer5, which no committed restatement' '$work/manifest-commented.stdout'"
  fixture manifest-block tools/tviburi-release/pins.tsv "$row" $'Tviburi.GuardedSTM32_Timer\ttimer1 timer5 timer8\t-'
  printf '%s\n' '/*' 'tviburiTimer5: Tviburi.GuardedSTM32_Timer @ sysbus 0x40000C00 as "timer5"' '*/' \
    >>"$work/manifest-block.tree/model/stm32h7-timer-restate.repl"
  pod manifest-block "$work/manifest-block.tree" -- sh -c '/repo/scripts/build-release'
  check "planted: a restatement inside a block comment: the release refuses the file by name" \
    "[ \$rc != 0 ] && grep -q 'stm32h7-timer-restate.repl: a block comment' '$work/manifest-block.stdout'"
  check "planted: a name the pinned chain does not declare (timer05) in the stock column: the release refuses it (adversarial pass, 2d)" \
    "planted typo tools/tviburi-release/pins.tsv \"\$row\" $'Tviburi.GuardedSTM32_Timer\ttimer1 timer05 timer8\t-' 'names timer05, which the pinned stock chain does not declare'"
  check "planted: a class row naming timer1 twice: the release refuses it" \
    "planted dup tools/tviburi-release/pins.tsv \"\$row\" $'Tviburi.GuardedSTM32_Timer\ttimer1 timer8 timer1\t-' 'class row names timer1 twice'"
  # R1: a consumer instance qualifies by the consumer's committed platform and the replay's re-typing rule, case-sensitively.
  check "planted: spi3 as the consumer's, which the committed fixture does not declare: the release refuses, naming it (R1)" \
    "planted spi3 tools/tviburi-release/pins.tsv \"\$spi\" $'Tviburi.GuardedSTM32H7_SPI\t-\tspi1 spi2 spi3' 'retypes spi3, which no committed replay runs'"
  check "planted: Spi1 as the consumer's, which differs from the fixture's spi1 only by case: the release refuses, naming it (R1)" \
    "planted Spi1 tools/tviburi-release/pins.tsv \"\$spi\" $'Tviburi.GuardedSTM32H7_SPI\t-\tspi1 spi2 Spi1' 'retypes Spi1, which no committed replay runs'"
  # A restatement the committed replay does not load: the DMA guard's `cat`, commented out.
  check "planted: the replay's cat of model/stm32h7-dma-restate.repl commented out: the release refuses the restatement by name" \
    "planted unloaded tests/1301/replay-1301.sh '    { echo; cat /repo/model/stm32h7-dma-restate.repl; echo' '#    { echo; cat /repo/model/stm32h7-dma-restate.repl; echo' 'model/stm32h7-dma-restate.repl: no committed replay loads it'"
  # Every restatement is its generator's output from the pinned stm32h7.repl, byte for byte: so its addresses, registrations
  # and names are the stock entry's, not a hand edit's (adversarial pass, 2b).
  pod manifest-regen "$src" -- sh -c 'f=/opt/renode/platforms/cpus/stm32h7.repl; for g in /repo/model/stm32h7-*-restate.awk; do
      n=${g##*/}; awk -v sha=$(sha256sum $f | cut -c1-64) -f "$g" $f > "/out/${n%.awk}.repl" || exit 1; done'
  check "every model/stm32h7-*-restate.repl regenerates from its model/stm32h7-*-restate.awk and the pinned stm32h7.repl, byte for byte, and none lacks a generator" \
    '[ $rc = 0 ] && ( for f in "$src"/model/stm32h7-*-restate.repl; do cmp -s "$f" "$work/manifest-regen/${f##*/}" || exit 1; done )'
}

t_lognames() {
  # TVI-WO-1203 (5): the access log names every register of the RCC and DMA guards, as it does a stock model's, never
  # `unknown`. Every offset of each guard's Fields table (DMA's stream rows at all eight streams) is read by the monitor
  # with LogPeripheralAccess on and DMA1 clocked (locked, a read is the bus's warning, not an access line).
  need_check
  python3 -I - "$src" >"$work/lognames.reads" <<'EOF'
import re, sys
for path, base, inst in (('model/GuardedSTM32H7_RCC.cs', 0x58024400, 'rcc'), ('model/GuardedSTM32DMA.cs', 0x40020000, 'dma1')):
    text = open(sys.argv[1] + '/' + path).read()
    table = text[text.index('private static readonly Row[] Fields ='):]
    offs = sorted({int(m.group(1), 16) for m in re.finditer(r'R\("\w+", (0x[0-9A-F]+),', table)})
    if inst == 'dma1':
        offs = sorted({o + 0x18 * k for o in offs for k in range(8 if 0x10 <= o < 0x28 else 1)})
    names = {int(v, 16): n for n, v in re.findall(r'^\s+(\w+) = (0x[0-9A-F]+),$', text[text.index('private enum Registers'):text.index('private enum Registers') + 6000].split('}')[0], re.M)}
    offs = sorted(set(offs) | set(names))   # and every offset the enum names (the RCC's C1_ aliases, adversarial pass 1f)
    for o in offs:
        print('%s 0x%X %s 0x%08X' % (inst, o, names.get(o, '-'), base + o))
EOF
  { echo "include @/repo/scripts/prelude.resc"; echo "sysbus WriteDoubleWord 0x580244D8 0x1"
    echo "sysbus LogPeripheralAccess sysbus.rcc true"; echo "sysbus LogPeripheralAccess sysbus.dma1 true"
    awk '{print "sysbus ReadDoubleWord " $4}' "$work/lognames.reads"; echo 'echo "TVIBURI-END"'; echo quit; } >"$work/lognames.resc"
  renode_in lognames "$src" /cases/lognames.resc -v "$work/lognames.resc:/cases/lognames.resc:ro,Z"
  check "every RCC and DMA guard register is named in the access log, the name its enum gives it, never unknown: each Fields offset read once by the monitor (TVI-WO-1203 (5))" \
    "python3 -I - '$work/lognames.reads' '$work/lognames/renode-log.txt' <<'EOF'
import re, sys
want = [l.split() for l in open(sys.argv[1])]
got = {}
for l in open(sys.argv[2], errors='replace'):
    m = re.search(r'\] (rcc|dma1): (?:\[cpu: [^\]]*\] )?Read\w* from (0x[0-9A-F]+) \(([^)]*)\)', l)
    if m: got.setdefault((m.group(1), int(m.group(2), 16)), m.group(3))
bad = [(i, o, n, got.get((i, int(o, 16)))) for i, o, n, a in want if n == '-' or got.get((i, int(o, 16))) != n]
unknown = [k for k, v in got.items() if v == 'unknown']
print('lognames:', len(want), 'offsets,', len(got), 'logged,', 'mismatched', bad[:5], 'unknown', unknown[:5])
sys.exit(0 if len(want) > 120 and not bad and not unknown else 1)
EOF"
}

t_guards() { # TVI-WO-1204 (6): scripts/guards is the one list, held to the committed board; no file keeps a copy
  board_guards() { # the instances a tree's board runs under a guard, sorted: the board and each `using "./…"` file it includes
    for f in $(awk '/^using "\.\// { s = $0; sub(/^using "\.\//, "", s); sub(/".*$/, "", s); print s }' "$1/model/tviburi-nucleo-h753zi.repl") \
      tviburi-nucleo-h753zi.repl; do awk -f "$src/tests/1204/board-guards.awk" "$1/model/$f"; done | sort | paste -sd' ' -
  }
  listed() { "$1/scripts/guards" | tr ' ' '\n' | sort | paste -sd' ' -; }
  check "scripts/guards, the one list, names exactly the instances the committed board runs under a guard ($(listed "$src" | wc -w))" \
    '[ "$(listed "$src")" = "$(board_guards "$src")" ] && [ "$(listed "$src" | wc -w)" -ge 31 ]'
  # Planted: a guard declared on the board and not in the list (as a work order adding a guard would leave it): the hold fails.
  fixture guards-extra model/tviburi-nucleo-h753zi.repl 'using "./stm32h7-adc-restate.repl"' 'using "./stm32h7-adc-restate.repl"'
  printf '\nspi2: Tviburi.GuardedSTM32H7_SPI @ sysbus 0x40003800\n' >>"$work/guards-extra.tree/model/tviburi-nucleo-h753zi.repl"
  check "planted: a guard on the board (spi2) that the list does not name is caught" \
    '[ "$(listed "$work/guards-extra.tree")" != "$(board_guards "$work/guards-extra.tree")" ]'
  # No other file keeps the list, in any order: no runner, harness or CI line names ten or more guards (the replay's own USART
  # restatement loop names eight, for another purpose: their properties).
  check "no file but scripts/guards keeps a copy of the list (no line elsewhere names ten or more guards)" \
    '! grep -rIlE "($(scripts_guards_alt))( [A-Za-z0-9]+)*( ($(scripts_guards_alt))){9}" "$src/scripts" "$src/tests" "$src/.github" 2>/dev/null | grep -v "^$src/scripts/guards\$" | grep -q .'
}
scripts_guards_alt() { "$src/scripts/guards" | tr ' ' '|'; }

t_teardown() {
  # TVI-WO-1204 (2): the teardown race, fixed at its cause (measure §1: a clean quit tears down the old emulation's asynchronous
  # logger with its queue undrained), and made deterministic (architect 15:48Z): the runner's own run, then three quits with
  # tests/1204/TeardownHold.cs holding the logging thread until the logger stops it, each required to write every guard's witness
  # and end record (tests/1204/teardown.sh). Without the flush in Records.End() every held quit loses them all; no load is used.
  pod teardown "$src" -- bash /repo/tests/1204/teardown.sh
  check "the runner's quit and three held quits each write every guard's witness and end record, in the log file and on the console (TVI-WO-1204 (2))" \
    "[ \$rc -eq 0 ] && grep -q '^teardown: 0 of 4 clean quits lost' '$work/teardown.stdout' && [ \"\$(awk 'NR > 1' '$work/teardown/teardown.tsv' | wc -l)\" = 3 ]"
}

t_witnessed() {
  # TVI-WO-1204 (6): tests/1301/witnessed.sh, which the budget job runs on each branch boot of the consumer's fixture, names the
  # instances, never counts them. Consoles in RUL-11's framing, made from this tree's own MANIFEST: one witness per instance
  # passes; one missing fails naming it; as many witnesses under other names fails; a planted restatement with no witness fails.
  pod witnessed "$src" -- sh -c '/repo/scripts/build-release >/dev/null'
  local m=$work/witnessed/release/MANIFEST d=$work/witnessed
  awk '$1 == "restate" || $1 == "retype" {printf "[00:00:00.000000000] [INFO] %s: {\"tviburi\":\"witness\",\"seq\":0}\n", $3}' "$m" >"$d/all.txt"
  grep -v '\] timer8: ' "$d/all.txt" >"$d/less.txt"
  sed 's/\] \([A-Za-z0-9_]*\): /] x\1: /' "$d/all.txt" >"$d/renamed.txt"
  { cat "$m"; echo 'restate Timers.STM32_Timer timer5 platforms/cpus/stm32h7.repl'; } >"$d/planted.MANIFEST"
  check "witnessed: a witness for each instance the MANIFEST restates or retypes passes; one missing (timer8), the same count under other names, or a planted restatement (timer5) with no witness each fails, naming what lacks one" \
    "[ \$rc = 0 ] && [ \$(wc -l <'$d/all.txt') -ge 30 ] && bash '$src/tests/1301/witnessed.sh' '$d/all.txt' '$m' >/dev/null \
     && ! bash '$src/tests/1301/witnessed.sh' '$d/less.txt' '$m' >'$d/less.out' && grep -q 'no witness for timer8 (of ' '$d/less.out' \
     && ! bash '$src/tests/1301/witnessed.sh' '$d/renamed.txt' '$m' >/dev/null \
     && ! bash '$src/tests/1301/witnessed.sh' '$d/all.txt' '$d/planted.MANIFEST' >'$d/planted.out' && grep -q 'no witness for timer5 (of ' '$d/planted.out'"
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
  printf '%s\n' 'include @/repo/scripts/prelude.resc' 'sysbus WriteDoubleWord 0x580244F0 0x1000' 'sysbus WriteDoubleWord 0x4001300C 0x02000000' 'echo "READ-CFG2"' \
    'sysbus ReadDoubleWord 0x4001300C' 'sysbus WriteDoubleWord 0x40013020 0x41' 'sysbus WriteDoubleWord 0x40013000 0x400' \
    'echo "READ-SR"' 'sysbus ReadDoubleWord 0x40013014' 'echo "TVIBURI-END"' 'quit' >"$work/recorded-monitor.resc"
  renode_in recorded-monitor "$work/recorded.tree" /cases/r.resc -v "$work/recorded-monitor.resc:/cases/r.resc:ro,Z"
  local console; console=$("$src/scripts/monitor-output" "$work/recorded-monitor/renode.out")
  # SR: CSUSP with SPE = 0 (TVI-SPI-ORDER) goes on, so EOT is the suspend's, and the SR read refuses (TVI-SPI-READ);
  # recorded, it is served by the stock model, whose TXP is always 1, so it is not 0.
  # Every rule per access (model/Refusal.cs's contract; TVI-WO-1204 (3), ruled 12:21Z, which moved this check from "one record per
  # access"): the 32-bit TXDR write, with 8-bit frames, breaks two rules, TVI-SPI-WIDTH and TVI-SPI-ORDER, and records both.
  check "recorded: refused accesses go on (CFG2's copy reads 0x02000000; the refused SR read is the stock value); every rule per access: the 32-bit TXDR write (8-bit frames) records TVI-SPI-WIDTH and TVI-SPI-ORDER" \
    "[ \"\$(grep -A1 -x READ-CFG2 <<<\"\$console\" | tail -n1)\" = 0x02000000 ] && grep -A1 -x READ-SR <<<\"\$console\" | tail -n1 | grep -qv '^0x00000000\$' \
     && spi1_records recorded-monitor '[(r[\"tviburi\"], r.get(\"rule\"), r.get(\"access\", {}).get(\"offset\")) for r in recs] == [(\"witness\", None, None), (\"would-refuse\", \"TVI-SPI-FIELD\", \"0xC\"), (\"would-refuse\", \"TVI-SPI-WIDTH\", \"0x20\"), (\"would-refuse\", \"TVI-SPI-ORDER\", \"0x20\"), (\"would-refuse\", \"TVI-SPI-ORDER\", \"0x0\"), (\"would-refuse\", \"TVI-SPI-READ\", \"0x14\"), (\"end\", None, None)]'"
  # Every rule inside the write chain too (model/Refusal.cs; TVI-WO-1204 adversarial pass): SPE set on the reset SPI, a slave with
  # SPI123SEL on pll1_q_ck and PLL1 off, breaks the enable's truth table (TVI-SPI-MODF) and has no kernel clock (TVI-SPI-KERNEL).
  printf '%s\n' 'include @/repo/scripts/prelude.resc' 'sysbus WriteDoubleWord 0x580244F0 0x1000' 'sysbus WriteDoubleWord 0x40013000 0x1' 'echo "TVIBURI-END"' 'quit' \
    >"$work/recorded-write.resc"
  renode_in recorded-write "$work/recorded.tree" /cases/r.resc -v "$work/recorded-write.resc:/cases/r.resc:ro,Z"
  check "recorded: SPE set on the reset SPI (a slave; no kernel clock) records TVI-SPI-MODF and TVI-SPI-KERNEL, both at that CR1 write" \
    "spi1_records recorded-write '[(r[\"tviburi\"], r.get(\"rule\"), r.get(\"access\", {}).get(\"offset\")) for r in recs] == [(\"witness\", None, None), (\"would-refuse\", \"TVI-SPI-MODF\", \"0x0\"), (\"would-refuse\", \"TVI-SPI-KERNEL\", \"0x0\"), (\"end\", None, None)]'"
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
    /check/debug/tviburi-check "$v" /run/renode-log.txt $(/repo/scripts/guards) >/dev/null; echo right=$?
    /check/debug/tviburi-check "$w" /run/renode-log.txt $(/repo/scripts/guards) >/dev/null; echo wrong=$?
    /check/debug/tviburi-check /run/renode-log.txt $(/repo/scripts/guards) >/dev/null; echo none=$?
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
  fixture rel-renode tools/tviburi-release/pins.tsv '5734eb0cd95a' '5734eb0cd95b'
  fixture rel-infra tools/tviburi-release/pins.tsv 'ddb19d7bf722' 'ddb19d7bf723'
  fixture rel-template tools/tviburi-release/TviburiRelease.cs.template 'public static readonly string' 'public const string'
  fixture rel-bench model/bench-checks.tsv 'Tviburi.GuardedSTM32H7_SPI	B4' 'Tviburi.GuardedSTM32H7_TIM	B4'
  local f why
  for f in rel-symlink:'not a regular file' rel-subdir:'not a regular file' rel-case:'another case' rel-alias:'alias' \
      rel-late:'after a declaration' rel-row:'in no row' rel-class:'a stop' rel-form:'not one' rel-pin:'not the pinned' \
      rel-renode:'Renode.dll is sha256 5734eb0cd95a[0-9a-f]*, not the pinned 5734eb0cd95b' rel-infra:'Infrastructure.dll is sha256 ddb19d7bf722[0-9a-f]*, not the pinned ddb19d7bf723' \
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
t_gpio() { # t_gpio <case>: one case of tests/1301/gpio.sh on this source, its ok/FAIL lines counted here (TVI-WO-1301)
  local out grc=0
  out=$(TVIBURI_TESTS="$1" TVIBURI_SCRATCH="$work" "$src/tests/1301/gpio.sh" "$src" 2>&1) || grc=$?
  grep -E '^(ok|FAIL) ' <<<"$out" || true
  local f; f=$(grep -c '^FAIL ' <<<"$out" || true)
  if [ "$grc" -ne 0 ] && [ "$f" -eq 0 ]; then
    echo "FAIL gpio-$1: tests/1301/gpio.sh exited $grc with no verdict"; f=1
  fi
  fails=$((fails + f))
}
for c in $GPIO_CASES; do eval "t_gpio-$c() { t_gpio $c; }"; done
t_timer() { # t_timer <case>: one case of tests/1302/timer.sh on this source, its ok/FAIL lines counted here (TVI-WO-1302)
  local out trc=0
  out=$(TVIBURI_TESTS="$1" TVIBURI_SCRATCH="$work" "$src/tests/1302/timer.sh" "$src" 2>&1) || trc=$?
  grep -E '^(ok|FAIL) ' <<<"$out" || true
  local f; f=$(grep -c '^FAIL ' <<<"$out" || true)
  if [ "$trc" -ne 0 ] && [ "$f" -eq 0 ]; then
    echo "FAIL timer-$1: tests/1302/timer.sh exited $trc with no verdict"; f=1
  fi
  fails=$((fails + f))
}
for c in $TIMER_CASES; do eval "t_timer-$c() { t_timer $c; }"; done
t_dma() { # t_dma <case>: one case of tests/1303/dma.sh on this source, its ok/FAIL lines counted here (TVI-WO-1303)
  local out trc=0
  out=$(TVIBURI_TESTS="$1" TVIBURI_SCRATCH="$work" "$src/tests/1303/dma.sh" "$src" 2>&1) || trc=$?
  grep -E '^(ok|FAIL) ' <<<"$out" || true
  local f; f=$(grep -c '^FAIL ' <<<"$out" || true)
  if [ "$trc" -ne 0 ] && [ "$f" -eq 0 ]; then
    echo "FAIL dma-$1: tests/1303/dma.sh exited $trc with no verdict"; f=1
  fi
  fails=$((fails + f))
}
for c in $DMA_CASES; do eval "t_dma-$c() { t_dma $c; }"; done
t_flash() { # t_flash <case>: one case of tests/1304/flash.sh on this source, its ok/FAIL lines counted here (TVI-WO-1304)
  local out trc=0
  out=$(TVIBURI_TESTS="$1" TVIBURI_SCRATCH="$work" "$src/tests/1304/flash.sh" "$src" 2>&1) || trc=$?
  grep -E '^(ok|FAIL) ' <<<"$out" || true
  local f; f=$(grep -c '^FAIL ' <<<"$out" || true)
  if [ "$trc" -ne 0 ] && [ "$f" -eq 0 ]; then
    echo "FAIL flash-$1: tests/1304/flash.sh exited $trc with no verdict"; f=1
  fi
  fails=$((fails + f))
}
for c in $FLASH_CASES; do eval "t_flash-$c() { t_flash $c; }"; done
t_usart() { # t_usart <case>: one case of tests/1305/usart.sh on this source, its ok/FAIL lines counted here (TVI-WO-1305)
  local out trc=0
  out=$(TVIBURI_TESTS="$1" TVIBURI_SCRATCH="$work" "$src/tests/1305/usart.sh" "$src" 2>&1) || trc=$?
  grep -E '^(ok|FAIL) ' <<<"$out" || true
  local f; f=$(grep -c '^FAIL ' <<<"$out" || true)
  if [ "$trc" -ne 0 ] && [ "$f" -eq 0 ]; then
    echo "FAIL usart-$1: tests/1305/usart.sh exited $trc with no verdict"; f=1
  fi
  fails=$((fails + f))
}
for c in $USART_CASES; do eval "t_usart-$c() { t_usart $c; }"; done
t_i2c() { # t_i2c <case>: one case of tests/1306/i2c.sh on this source, its ok/FAIL lines counted here (TVI-WO-1306)
  local out trc=0
  out=$(TVIBURI_TESTS="$1" TVIBURI_SCRATCH="$work" "$src/tests/1306/i2c.sh" "$src" 2>&1) || trc=$?
  grep -E '^(ok|FAIL) ' <<<"$out" || true
  local f; f=$(grep -c '^FAIL ' <<<"$out" || true)
  if [ "$trc" -ne 0 ] && [ "$f" -eq 0 ]; then
    echo "FAIL i2c-$1: tests/1306/i2c.sh exited $trc with no verdict"; f=1
  fi
  fails=$((fails + f))
}
for c in $I2C_CASES; do eval "t_i2c-$c() { t_i2c $c; }"; done
t_adc() { # t_adc <case>: one case of tests/1307/adc.sh on this source, its ok/FAIL lines counted here (TVI-WO-1307)
  local out trc=0
  out=$(TVIBURI_TESTS="$1" TVIBURI_SCRATCH="$work" "$src/tests/1307/adc.sh" "$src" 2>&1) || trc=$?
  grep -E '^(ok|FAIL) ' <<<"$out" || true
  local f; f=$(grep -c '^FAIL ' <<<"$out" || true)
  if [ "$trc" -ne 0 ] && [ "$f" -eq 0 ]; then
    echo "FAIL adc-$1: tests/1307/adc.sh exited $trc with no verdict"; f=1
  fi
  fails=$((fails + f))
}
for c in $ADC_CASES; do eval "t_adc-$c() { t_adc $c; }"; done
t_1308() { # t_1308 <suite> <case>: one case of tests/1308/<suite>.sh on this source, its ok/FAIL lines counted here (TVI-WO-1308)
  local out trc=0
  out=$(TVIBURI_TESTS="$2" TVIBURI_SCRATCH="$work" "$src/tests/1308/$1.sh" "$src" 2>&1) || trc=$?
  grep -E '^(ok|FAIL) ' <<<"$out" || true
  local f; f=$(grep -c '^FAIL ' <<<"$out" || true)
  if [ "$trc" -ne 0 ] && [ "$f" -eq 0 ]; then
    echo "FAIL $1-$2: tests/1308/$1.sh exited $trc with no verdict"; f=1
  fi
  fails=$((fails + f))
}
for c in $DMAMUX_CASES; do eval "t_dmamux-$c() { t_1308 dmamux $c; }"; done
for c in $PWR_CASES; do eval "t_pwr-$c() { t_1308 pwr $c; }"; done

for t in ${TVIBURI_TESTS:-acceptance}; do
  case "$t" in
    acceptance) sel=$ACCEPTANCE ;;
    adversarial) sel=$ADVERSARIAL ;;
    all) sel=$ALL ;;
    *) case " $ALL " in *" $t "*) sel=$t ;; *) echo "unknown test: $t" >&2; exit 2 ;; esac ;;
  esac
  for u in $sel; do "t_$u"; echo "RAN $u"; done
done

echo "outputs: $work"
[ $fails -eq 0 ] && echo "ALL PASS" || { echo "$fails FAILED"; exit 1; }
