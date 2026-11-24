#!/usr/bin/env bash
# tests/1303/dma.sh [SRC] — TVI-WO-1303's acceptance cases for the DMA guard, run on the host as tests/1302/timer.sh is:
# each case one container in the pinned tools image (tests/spike.sh's IMAGE), no network, the source read-only. SRC
# defaults to a `git archive HEAD` export. tests/spike.sh runs each case as dma-<case> in its acceptance group.
#   TVIBURI_TESTS="axi tcm ..."  run only these cases.
#   TVIBURI_CI=1                 plain `podman run`, for a disposable CI runner only; elsewhere ~/bin/capped-run.
# Exit 0 only if every selected case passed. Pages: RM0433 Rev 9 §15.5 (DMA registers, pp. 659-667), §2.1.6 p108 (the
# TCMs), Table 7 p130-131, §17 (DMAMUX1, pp. 696-712). The rulings: TVI-WO-1303.measure.md §5, ruled 2026-11-24T14:10Z.
set -euo pipefail

here=$(cd "$(dirname "$0")/../.." && pwd)
IMAGE=$(sed -n 's/^IMAGE=//p' "$here/tests/spike.sh")
CASES="axi tcm itcm partcm below wrap unmapped ndt0 reload reassert sizes fth fthzero reloadtcm en enoff done same disable width read field msize16 route0 route54 mux shared sharedx request txother usart23 fields ledger registrations irq config recorded reset clock offset regen restate"
GUARDS="spi1 rcc gpioPortA gpioPortB gpioPortC gpioPortD gpioPortE gpioPortF gpioPortG gpioPortH gpioPortI gpioPortJ gpioPortK timer1 timer8 dma1 dma2 flashController usart1 usart2 usart3 uart4 uart5 usart6 uart7 uart8"
MONITOR="fields ledger registrations irq config recorded reset clock offset regen restate"
# The planted firmware's paths: every case but the monitor-driven ones.
PATHS=$(for c in $CASES; do case " $MONITOR " in *" $c "*) ;; *) printf '"%s",' "$c" ;; esac; done | sed 's/,$//')
scratch=${TVIBURI_SCRATCH:-$HOME/.cache/tviburi}
mkdir -p "$scratch"
work=$(mktemp -d "$scratch/dma.XXXXXX")

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

# One container: build the check and (for a firmware case) the planted firmware, run Renode on the case's monitor
# script, then the check on the log, naming every guarded source. Outputs in $work/<name>. PLATFORM_SED: a sed script
# appended to the board's description for this case.
run_case() { # run_case <name> <firmware path or -> <monitor lines...>
  local name=$1 fw=$2; shift 2
  mkdir -p "$work/$name"
  sed -e 's#^using "\./#using "/repo/model/#' -e "${PLATFORM_SED:-\$a\\
}" "$src/model/tviburi-nucleo-h753zi.repl" > "$work/$name/platform.repl"
  {
    sed "s#^machine LoadPlatformDescription .*#machine LoadPlatformDescription @/out/platform.repl#" "$src/scripts/prelude.resc"
    [ "$fw" = - ] || { echo "sysbus LoadELF @/out/fw.elf"; echo "cpu VectorTableOffset 0x08000000"; }
    printf '%s\n' "$@"
    echo 'echo "TVIBURI-END"'
    echo "quit"
  } > "$work/$name/run.resc"
  rc=0
  container -v "$src:/repo:ro,Z" -v "$work/$name:/out:Z" "$IMAGE" bash -c '
    export PATH=/opt/cargo/bin:$PATH RUSTUP_HOME=/opt/rustup CARGO_HOME=/out/cargo-home
    v=$(/repo/scripts/build-release) || exit 92
    cargo build --quiet --offline --locked --manifest-path /repo/tools/tviburi-check/Cargo.toml --target-dir /out/target || exit 90
    if [ "$1" != - ]; then
      rustc --edition 2021 --target thumbv7em-none-eabihf -C opt-level=s -C panic=abort -C debuginfo=0 --cfg "path=\"$1\"" \
        --check-cfg "cfg(path, values($3))" -C link-arg=-T/repo/firmware/link.x -o /out/fw.elf /repo/firmware/dma.rs || exit 91
    fi
    timeout 120 renode --disable-gui --console --plain /out/run.resc </dev/null >/out/renode.out 2>&1
    echo "renode-exit=$?"
    /out/target/debug/tviburi-check "$v" /out/renode-log.txt $2
  ' _ "$fw" "$GUARDS" "$PATHS" </dev/null >"$work/$name.stdout" 2>&1 || rc=$?
  echo "--- $name ($fw): exit $rc"; grep -E '^(CHECK|PROBLEM)' "$work/$name.stdout" || true
}

records() { # records <name> <python expr over `recs`, the check's REFUSAL lines parsed>
  python3 -I -c '
import json, sys
recs = [json.loads(l[8:]) for l in open(sys.argv[1]) if l.startswith("REFUSAL ")]
sys.exit(0 if eval(sys.argv[2]) else 1)
' "$work/$1.stdout" "$2"
}
first() { records "$1" "len(recs) >= 1 and (lambda r: $2)(recs[0])"; }
console() { "$src/scripts/monitor-output" "$work/$1/renode.out"; }
after() { console "$1" | grep -A1 -x 'TVIBURI-MARK-AFTER' | tail -n1 | grep -qx '0xAF7E0002'; }
readout() { console "$1" | grep -A1 -x "R-$2" | tail -n1; }
passed() { [ "$rc" = 0 ] && grep -qx 'CHECK PASS' "$work/$1.stdout"; }
passes() { passed "$1" && after "$1"; }
refuses() { [ "$rc" = 3 ] && first "$1" "r[\"rule\"] == \"$2\" and r[\"kind\"] == \"$3\" and r[\"access\"][\"pc\"] != \"none\" and $4"; }
marks=('echo "TVIBURI-MARK-AFTER"' 'sysbus ReadDoubleWord 0x20000104' 'echo "R-READ"' 'sysbus ReadDoubleWord 0x20000108' 'echo "R-READ2"' 'sysbus ReadDoubleWord 0x2000010C')
clk='sysbus WriteDoubleWord 0x580244D8 0x3'   # DMA1EN, DMA2EN (p455): since TVI-WO-1300a an unclocked peripheral is locked

for t in ${TVIBURI_TESTS:-$CASES}; do
  case " $MONITOR " in *" $t "*) mon=1 ;; *) mon=0 ;; esac
  if [ $mon = 0 ]; then
    run_case "$t" "$t" 'emulation RunFor "0.001"' "${marks[@]}"
  fi
  case $t in
  axi) check "axi: a DMA1 memory-to-memory stream in AXI SRAM passes and moves its data (p108: AXI SRAM is reachable)" \
    "passes axi && [ \"\$(readout axi READ)\" = 0x11111111 ]" ;;
  tcm) check "tcm: the same stream with its destination in DTCM refuses TVI-DMA-TCM, predicted-fault, at the EN write, naming the stream, the address, the region and p108" \
    "refuses tcm TVI-DMA-TCM predicted-fault 'r[\"bit\"] == \"DMA_S0M0AR = 0x20000400 (DTCM (0x20000000-0x2001FFFF))\" and \"p108\" in r[\"citation\"] and \"stream 0\" in r[\"condition\"] and r[\"access\"][\"offset\"] == \"0x10\"'" ;;
  itcm) check "itcm: a peripheral-to-memory stream with M0AR in ITCM refuses TVI-DMA-TCM, predicted-fault (p108)" \
    "refuses itcm TVI-DMA-TCM predicted-fault 'r[\"bit\"].startswith(\"DMA_S0M0AR = 0x00000100 (ITCM\")'" ;;
  partcm) check "partcm: a memory-to-peripheral stream with PAR in DTCM refuses TVI-DMA-TCM: the peripheral bus cannot reach it either (p108)" \
    "refuses partcm TVI-DMA-TCM predicted-fault 'r[\"bit\"].startswith(\"DMA_S0PAR = 0x20000400 (DTCM\")'" ;;
  below) check "below: a span starting below DTCM (0x1FFFFFF0, 8 words) and ending inside it refuses TVI-DMA-TCM" \
    "refuses below TVI-DMA-TCM predicted-fault 'r[\"bit\"].startswith(\"DMA_S0M0AR = 0x1FFFFFF0 (DTCM\")'" ;;
  wrap) check "wrap: a span that wraps past 0xFFFFFFFF refuses TVI-DMA-ADDRESS, unmodeled (ruled Q4)" \
    "refuses wrap TVI-DMA-ADDRESS unmodeled 'r[\"bit\"] == \"DMA_S0M0AR = 0xFFFFFFF0 (past 0xFFFFFFFF)\"'" ;;
  unmapped) check "unmapped: a span in Table 7's reserved 0xA0000000 refuses TVI-DMA-ADDRESS, unmodeled (p130; ruled Q4)" \
    "refuses unmapped TVI-DMA-ADDRESS unmodeled 'r[\"bit\"] == \"DMA_S0M0AR = 0xA0000000 (0xA0000000)\"'" ;;
  ndt0) check "ndt0: NDT = 0 with M0AR in DTCM passes: no item is addressed (p665)" "passes ndt0" ;;
  reload) check "reload: a stream enabled again with NDT = 0 after 4 items were programmed refuses TVI-DMA-RELOAD, unmodeled (p665: silicon reloads; the stock model does not)" \
    "refuses reload TVI-DMA-RELOAD unmodeled 'r[\"bit\"] == \"DMA_S0NDTR.NDT = 0 (programmed 4)\" and \"p665\" in r[\"citation\"]'" ;;
  reassert) check "reassert: CR written unchanged with EN = 1 after the twin completed the transfer refuses TVI-DMA-DONE, unmodeled (silicon starts a new transfer, p665)" \
    "refuses reassert TVI-DMA-DONE unmodeled '\"starts a new transfer\" in r[\"condition\"]'" ;;
  sizes) check "sizes: a stream enabled with PSIZE = word and MSIZE = byte refuses TVI-DMA-SIZE, unmodeled (p663: direct mode forces MSIZE to PSIZE)" \
    "refuses sizes TVI-DMA-SIZE unmodeled 'r[\"bit\"] == \"DMA_SxCR.PSIZE = 2, MSIZE = 0\" and \"p663\" in r[\"citation\"]'" ;;
  fth) check "fth: FCR.FTH changed while a stream runs refuses TVI-DMA-EN, predicted-fault (p667: protected under EN)" \
    "refuses fth TVI-DMA-EN predicted-fault 'r[\"register\"] == \"DMA_SxFCR\" and r[\"bit\"].startswith(\"DMA_SxFCR bits 0x00000001 of stream 0\") and \"p667\" in r[\"citation\"]'" ;;
  fthzero) check "fthzero: FTH = 00 in direct mode passes (p667: not used in direct mode); FCR reads 0x20" \
    "passes fthzero && [ \"\$(readout fthzero READ)\" = 0x00000020 ]" ;;
  reloadtcm) check "reloadtcm: a stream enabled again without NDTR, its M0AR moved into DTCM, refuses TVI-DMA-TCM on the reloaded span (p665, p108), not RELOAD" \
    "refuses reloadtcm TVI-DMA-TCM predicted-fault 'r[\"bit\"].startswith(\"DMA_S0M0AR = 0x20000400 (DTCM\")'" ;;
  en) check "en: PAR rewritten while a stream runs refuses TVI-DMA-EN, predicted-fault (p666)" \
    "refuses en TVI-DMA-EN predicted-fault 'r[\"register\"] == \"DMA_SxPAR\" and \"running\" in r[\"bit\"] and \"p666\" in r[\"citation\"]'" ;;
  enoff) check "enoff: one CR write clearing EN and changing DIR on a running stream refuses TVI-DMA-EN (judged against the state before it)" \
    "refuses enoff TVI-DMA-EN predicted-fault 'r[\"register\"] == \"DMA_SxCR\" and r[\"bit\"].startswith(\"DMA_SxCR bits 0x00000040 of stream 0\")'" ;;
  done) check "done: a memory-to-peripheral stream the twin completed at EN, then DIR changed with EN still set: TVI-DMA-DONE, unmodeled (ruled Q3 (c))" \
    "refuses done TVI-DMA-DONE unmodeled 'r[\"register\"] == \"DMA_SxCR\" and \"completed in the twin\" in r[\"bit\"]'" ;;
  same) check "same: PAR and CR written again unchanged on a running stream, then the stream disabled: passes (RUL-18 as amended)" "passes same" ;;
  disable) check "disable: ArduPilot's USART1 RX/TX patterns pass, the TX disable clearing EN and TCIE in one write (ruled Q2); S0CR reads 0x100417, its TRBUFF, TEIE and DMEIE kept by the copy; FCR reads 0x21 (p667)" \
    "passes disable && [ \"\$(readout disable READ)\" = 0x00100417 ] && [ \"\$(readout disable READ2)\" = 0x00000021 ]" ;;
  width) check "width: a byte write to a DMA register refuses TVI-DMA-WIDTH, unmodeled (p659: words only)" \
    "refuses width TVI-DMA-WIDTH unmodeled 'r[\"access\"][\"width\"] == 8'" ;;
  read) check "read: a read at 0xD0, where no register is, refuses TVI-DMA-READ, unmodeled" \
    "refuses read TVI-DMA-READ unmodeled 'r[\"bit\"] == \"no register at offset 0xD0\"'" ;;
  field) check "field: CIRC = 1 refuses TVI-DMA-FIELD, unmodeled (circular mode is not modeled)" \
    "refuses field TVI-DMA-FIELD unmodeled 'r[\"bit\"] == \"DMA_SxCR.CIRC (bit 8) = 1\"'" ;;
  msize16) check "msize16: a memory-to-memory stream of half-words (PSIZE = MSIZE = 01) passes and moves its data (the probe ruling Q6 asked for)" \
    "passes msize16 && [ \"\$(readout msize16 READ)\" = 0x11111111 ]" ;;
  route0) check "route0: a memory-to-peripheral stream whose DMAMUX1 channel selects no request refuses TVI-DMA-ROUTE, predicted-fault (p703)" \
    "refuses route0 TVI-DMA-ROUTE predicted-fault 'r[\"bit\"] == \"DMAMUX1_C0CR.DMAREQ_ID = 0 (no request)\" and \"p703\" in r[\"citation\"]'" ;;
  route54) check "route54: a stream on reserved request 54 refuses TVI-DMA-ROUTE, predicted-fault (Table 124 p697)" \
    "refuses route54 TVI-DMA-ROUTE predicted-fault 'r[\"bit\"] == \"DMAMUX1_C0CR.DMAREQ_ID = 54 (reserved)\" and \"p697\" in r[\"citation\"]'" ;;
  mux) check "mux: a stream on request generator id 3 refuses TVI-DMA-MUX, unmodeled" \
    "refuses mux TVI-DMA-MUX unmodeled 'r[\"bit\"] == \"DMAMUX1_C0CR.DMAREQ_ID = 3 (dmamux1_req_gen2)\"'" ;;
  shared) check "shared: a second stream enabled on the request a running stream's channel selects refuses TVI-DMA-SHARED, predicted-fault (p704)" \
    "refuses shared TVI-DMA-SHARED predicted-fault 'r[\"bit\"] == \"DMAMUX1_C1CR.DMAREQ_ID = 41 on channels 1 and 0\" and \"p704\" in r[\"citation\"]'" ;;
  sharedx) check "sharedx: the same across controllers, DMA2 stream 0 (channel 8) against DMA1 stream 0 (channel 0), refuses TVI-DMA-SHARED" \
    "refuses sharedx TVI-DMA-SHARED predicted-fault 'r[\"peripheral\"] == \"sysbus.dma2\" and r[\"bit\"] == \"DMAMUX1_C8CR.DMAREQ_ID = 41 on channels 8 and 0\"'" ;;
  request) check "request: a peripheral-to-memory stream on 61 (spi3_rx_dma, raised by no model) refuses TVI-DMA-REQUEST, unmodeled (ruled Q5 (c))" \
    "refuses request TVI-DMA-REQUEST unmodeled 'r[\"bit\"] == \"DMAMUX1_C0CR.DMAREQ_ID = 61 (spi3_rx_dma)\"'" ;;
  usart23) check "usart23: peripheral-to-memory streams on 43 and 45 (usart2_rx_dma, usart3_rx_dma) and a memory-to-peripheral stream on 46 (usart3_tx_dma), as ArduPilot's armed run enables them, pass: acknowledged by name (ruled 2026-11-24T18:55Z Q6)" \
    "passes usart23" ;;
  txother) check "txother: a memory-to-peripheral stream on 44 (usart2_tx_dma, not acknowledged) refuses TVI-DMA-REQUEST, unmodeled" \
    "refuses txother TVI-DMA-REQUEST unmodeled 'r[\"bit\"] == \"DMAMUX1_C0CR.DMAREQ_ID = 44 (usart2_tx_dma)\"'" ;;
  fields)
    # Gate clause 6 (RUL-17) for DMA1, the cases generated from the release's ledger: each field row's disallowed value
    # refuses TVI-DMA-FIELD, unmodeled, naming the field; each allowed value passes; every offset with no row refuses.
    mkdir -p "$work/fields-cases"
    container -v "$src:/repo:ro,Z" -v "$work/fields-cases:/out:Z" "$IMAGE" sh -c '/repo/scripts/build-release >/dev/null &&
      /out/release.target/debug/tviburi-release cases /out/release/LEDGER.jsonl Tviburi.GuardedSTM32DMA 0x40020000 0x400 0x580244D8 0x3 >/out/cases.txt' </dev/null >"$work/fields-cases.stdout" 2>&1
    mapfile -t lines < "$work/fields-cases/cases.txt"
    run_case fields - "${lines[@]}"
    check "fields: every ledger row's disallowed value refuses by name, unmodeled; allowed values pass, EN alone meeting TVI-DMA-ROUTE; streams 1-7 judged by stream 0's rows; no-register offsets refuse (gate clause 6)" \
      "python3 -I - '$work/fields-cases/cases.txt' '$work/fields.stdout' <<'EOF'
import json, sys
cases = {}
for l in open(sys.argv[1]):
    if l.startswith('# CASE '):
        kind, name, off, val = l.split()[2:6]
        o, v = int(off, 16), int(val, 16)
        # The generator knows one offset per register; streams 1-7's registers (0x28-0xCC) are stream 0's rows, which
        # allow 0 in every field, so a 0 written there passes.
        if name == 'none' and 0x28 <= o < 0xD0:
            kind, name = 'pass', 'stream'
        cases[(o, v)] = (kind, name)
recs = [json.loads(l[8:]) for l in open(sys.argv[2]) if l.startswith('REFUSAL ')]
seen, bad = {}, []
for r in recs:
    k = (int(r['access']['offset'], 16), int(r['access']['value'], 16))
    if k not in cases or k in seen:
        bad.append(('not one record per case', r['rule'], r['bit'])); continue
    seen[k] = r
    kind, name = cases[k]
    if k == (0x10, 1):
        # EN alone: the stream starts with DMAMUX1 channel 0 selecting no request, TVI-DMA-ROUTE's (p703).
        if r['rule'] != 'TVI-DMA-ROUTE': bad.append(('en', r['rule']))
        continue
    if kind == 'pass' or r['rule'] != 'TVI-DMA-FIELD' or r['kind'] != 'unmodeled':
        bad.append((kind, name, r['rule'], r['bit']))
    elif not (r['bit'].startswith(name + ' (') if name != 'none' else r['bit'] == 'no register at offset 0x%X' % k[0]):
        bad.append(('wrong field', name, r['bit']))
missing = [v for k, v in cases.items() if v[0] == 'refuse' and k not in seen]
print(len(cases), 'cases;', len(recs), 'refusals;', 'bad', bad[:5], 'missing', missing[:5])
sys.exit(0 if not bad and not missing and len(cases) > 150 else 1)
EOF" ;;
  ledger)
    mkdir -p "$work/ledger"
    rc=0; container -v "$src:/repo:ro,Z" -v "$work/ledger:/out:Z" "$IMAGE" sh -c '/repo/scripts/build-release /repo /out/release >/dev/null' </dev/null >"$work/ledger.stdout" 2>&1 || rc=$?
    check "ledger: the release carries the DMA guard's field rows over its 10 registers, the acknowledged requests by name (41 to memory; 38, 40, 42 from memory) on DIR, EN's stock-wrong clear (p665) stated, bench row D1 with the (rule, bit) tcm emits, and the MANIFEST restating dma1 and dma2 only" \
      "[ \$rc = 0 ] && grep -qx 'class DMA.STM32DMA Tviburi.GuardedSTM32DMA' '$work/ledger/release/MANIFEST' && [ \"\$(grep -c '^restate DMA.STM32DMA dma[12] ' '$work/ledger/release/MANIFEST')\" = 2 ] && python3 -I -c '
import json, sys
rows = [json.loads(l) for l in open(sys.argv[1]).read().splitlines()[1:]]
f = [r for r in rows if r.get(\"peripheral\") == \"Tviburi.GuardedSTM32DMA\" and r[\"row\"] == \"field\"]
by = {(r[\"register\"], r[\"field\"]): r for r in f}
ok = len(f) == 135 and len({r[\"register\"] for r in f}) == 10
d = by[(\"DMA_SxCR\", \"DIR[1:0]\")]
ok = ok and d[\"state\"] == \"stock-wrong\" and all(x in d[\"detail\"] for x in (\"41 usart1_rx_dma\", \"38 spi1_tx_dma\", \"40 spi2_tx_dma\", \"42 usart1_tx_dma\"))
ok = ok and d[\"allowed\"] == [0, 1, 2] and by[(\"DMA_SxCR\", \"CIRC\")][\"allowed\"] == [0] and \"p665\" in by[(\"DMA_SxCR\", \"EN\")][\"behaves_as\"]
ok = ok and by[(\"DMA_SxCR\", \"PL[1:0]\")][\"state\"] == \"stock-wrong\" and by[(\"DMA_SxFCR\", \"FTH[1:0]\")][\"allowed\"] == \"any\"
bench = sorted((r[\"field\"], r[\"rule\"], r[\"bit\"]) for r in rows if r.get(\"peripheral\") == \"Tviburi.GuardedSTM32DMA\" and r[\"row\"] == \"bench\")
ok = ok and bench == [(\"D1\", \"TVI-DMA-TCM\", \"DMA_S0M0AR = 0x20000400 (DTCM (0x20000000-0x2001FFFF))\")]
sys.exit(0 if ok else 1)
' '$work/ledger/release/LEDGER.jsonl'" ;;
  registrations)
    # A request raised at DMAMUX1 reaches the guard's stream (D11 of the measurement, through the guard).
    run_case registrations - "$clk" 'peripherals' 'sysbus WriteDoubleWord 0x40020808 0x29' 'sysbus WriteDoubleWord 0x40020048 0x40011024' \
      'sysbus WriteDoubleWord 0x4002004C 0x38001900' 'sysbus WriteDoubleWord 0x40020044 0x4' 'sysbus WriteDoubleWord 0x40020040 0x401' \
      'sysbus.dmamux1 OnGPIO 41 true' 'sysbus.dmamux1 OnGPIO 41 false' 'echo "R-NDTR"' 'sysbus ReadDoubleWord 0x40020044'
    check "registrations: peripherals lists dma1 and dma2 as the guard; a request on DMAMUX1 input 41 routed to channel 2 moves one item on DMA1 stream 2 (NDTR 4 -> 3)" \
      "passed registrations && grep -q 'dma1 (GuardedSTM32DMA)' '$work/registrations/renode.out' && grep -q 'dma2 (GuardedSTM32DMA)' '$work/registrations/renode.out' && [ \"\$(readout registrations NDTR)\" = 0x00000003 ]" ;;
  irq)
    # A completed transfer with TCIE raises the stream's interrupt at the NVIC through the guard (DMA1 stream 0 is IRQ 11,
    # stm32h7.repl; NVIC_ISPR0 bit 11).
    run_case irq - "$clk" 'sysbus WriteDoubleWord 0x24000000 0x5A5A5A5A' 'sysbus WriteDoubleWord 0x40020018 0x24000000' 'sysbus WriteDoubleWord 0x4002001C 0x24001000' \
      'sysbus WriteDoubleWord 0x40020014 0x1' 'sysbus WriteDoubleWord 0x40020010 0x5690' 'sysbus WriteDoubleWord 0x40020010 0x5691' 'echo "R-ISPR0"' 'sysbus ReadDoubleWord 0xE000E200'
    check "irq: a completed memory-to-memory transfer with TCIE sets DMA1 stream 0's interrupt (IRQ 11) pending at the NVIC, through the guard" \
      "passed irq && [ \$(( \$(readout irq ISPR0) >> 11 & 1 )) = 1 ]" ;;
  config)
    PLATFORM_SED='$a\
tviburiDma2:\
    dmamux: empty' run_case config - "$clk" 'sysbus ReadDoubleWord 0x40020400'
    check "config: a DMA guard given no dmamux refuses every access, TVI-DMA-CONFIG, unmodeled" \
      "[ \$rc = 3 ] && first config 'r[\"rule\"] == \"TVI-DMA-CONFIG\" and r[\"kind\"] == \"unmodeled\" and r[\"peripheral\"] == \"sysbus.dma2\"'" ;;
  recorded)
    PLATFORM_SED='/^tviburiDma1:$/{n;s/enforcing/recorded/}' run_case recorded - "$clk" \
      'sysbus WriteDoubleWord 0x40020800 0x29' 'sysbus WriteDoubleWord 0x40020018 0x40011024' 'sysbus WriteDoubleWord 0x4002001C 0x38001900' \
      'sysbus WriteDoubleWord 0x40020014 0x4' 'sysbus WriteDoubleWord 0x40020010 0x401' 'sysbus WriteDoubleWord 0x40020018 0x40011028' \
      'sysbus WriteDoubleWord 0x40020010 0x441' 'echo "R-PAR"' 'sysbus ReadDoubleWord 0x40020018' 'echo "R-CR"' 'sysbus ReadDoubleWord 0x40020010'
    check "recorded: dma1 recorded: PAR and DIR changed on a running stream each record a would-refuse (TVI-DMA-EN), and the access goes on with the protected fields kept as silicon keeps them: PAR and CR read their old values; never a pass at the check" \
      "[ \$rc = 1 ] && [ \"\$(grep -c '\"tviburi\":\"would-refuse\".*TVI-DMA-EN' '$work/recorded/renode-log.txt')\" = 2 ] && [ \"\$(readout recorded PAR)\" = 0x40011024 ] && [ \"\$(readout recorded CR)\" = 0x00000401 ]" ;;
  reset)
    run_case reset - "$clk" 'sysbus WriteDoubleWord 0x40020024 0x21' 'sysbus WriteDoubleWord 0x40020010 0x30400' 'machine Reset' "$clk" \
      'echo "R-CR"' 'sysbus ReadDoubleWord 0x40020010' 'echo "R-FCR"' 'sysbus ReadDoubleWord 0x40020024'
    check "reset: machine Reset clears the copies: CR reads 0 and FCR its reset value 0x21 (p662, p667)" \
      "passed reset && [ \"\$(readout reset CR)\" = 0x00000000 ] && [ \"\$(readout reset FCR)\" = 0x00000021 ]" ;;
  clock)
    # WO (2), ruled Q7: with DMA1EN = 0 the RCC guard locks dma1 on the bus: the write is dropped, a read returns 0, and no
    # record is emitted. Silicon ignores the write (p373). Covered, not refused.
    run_case clock - 'sysbus WriteDoubleWord 0x40020014 0x5' "$clk" 'echo "R-NDTR"' 'sysbus ReadDoubleWord 0x40020014'
    check "clock: NDTR written with DMA1EN = 0 is dropped by the bus lock with no record (p373; ruled Q7): it reads 0 once clocked, and the run passes" \
      "passed clock && grep -q 'Tried to write a locked peripheral: machine-0.dma1. Address 0x40020014' '$work/clock/renode.out' && [ \"\$(readout clock NDTR)\" = 0x00000000 ]" ;;
  offset)
    # TVI-WO-1305 (ruled 2026-11-24T18:55Z Q6): software clearing EN while a stream runs suspends it as silicon does: TCIF
    # set (§15.3.16 p654), and the next transfer at the programmed base (p654, p666), not the stock model's old offset
    # (STM32DMA.cs L160, L256, L262). Three requests raised by hand on an 8-item P2M stream, a disable, LISR read; a new M0AR
    # over 0xFF bytes and NDTR, the enable, one request: the byte lands at the new base. Replaces TVI-DMA-OFFSET.
    run_case offset - "$clk" 'sysbus WriteDoubleWord 0x38001A00 0xFFFFFFFF' 'sysbus WriteDoubleWord 0x40020800 0x29' 'sysbus WriteDoubleWord 0x40020018 0x40011024' \
      'sysbus WriteDoubleWord 0x4002001C 0x38001900' 'sysbus WriteDoubleWord 0x40020014 0x8' 'sysbus WriteDoubleWord 0x40020010 0x401' \
      'sysbus.dmamux1 OnGPIO 41 true' 'sysbus.dmamux1 OnGPIO 41 false' 'sysbus.dmamux1 OnGPIO 41 true' 'sysbus.dmamux1 OnGPIO 41 false' \
      'sysbus.dmamux1 OnGPIO 41 true' 'sysbus.dmamux1 OnGPIO 41 false' 'echo "R-LISR0"' 'sysbus ReadDoubleWord 0x40020000' \
      'sysbus WriteDoubleWord 0x40020010 0x400' 'echo "R-LISR"' 'sysbus ReadDoubleWord 0x40020000' 'echo "R-NDTR"' 'sysbus ReadDoubleWord 0x40020014' \
      'sysbus WriteDoubleWord 0x40020008 0x3D' 'sysbus WriteDoubleWord 0x4002001C 0x38001A00' 'sysbus WriteDoubleWord 0x40020014 0x8' \
      'sysbus WriteDoubleWord 0x40020010 0x401' 'sysbus.dmamux1 OnGPIO 41 true' 'sysbus.dmamux1 OnGPIO 41 false' 'echo "R-BASE"' 'sysbus ReadDoubleWord 0x38001A00'
    check "offset: a stream disabled by software after 3 of 8 items has TCIF0 set (p654; it was clear before) and NDTR 5; re-armed on a new M0AR, its next item lands at the new base (p654, p666), and the run passes" \
      "passed offset && [ \"\$(readout offset LISR0)\" = 0x00000000 ] && [ \"\$(readout offset LISR)\" = 0x00000020 ] && [ \"\$(readout offset NDTR)\" = 0x00000005 ] && [ \"\$(readout offset BASE)\" = 0xFFFFFF00 ]" ;;
  regen)
    rc=0; python3 -I "$src/tests/1303/fields.py" > "$work/fields.cs" || rc=$?
    check "regen: the guard's Fields table regenerates from tests/1303/fields.py, byte for byte (RUL-16)" \
      "[ \$rc = 0 ] && sed -n '/^        private static readonly Row\[\] Fields =$/,/^        };$/p' '$src/model/GuardedSTM32DMA.cs' | sed '1,2d;\$d' | cmp -s - '$work/fields.cs'" ;;
  restate)
    mkdir -p "$work/restate"
    container -v "$src:/repo:ro,Z" -v "$work/restate:/out:Z" "$IMAGE" sh -c 'f=/opt/renode/platforms/cpus/stm32h7.repl;
      awk -v sha=$(sha256sum $f | cut -c1-64) -f /repo/model/stm32h7-dma-restate.awk $f > /out/restate.repl' </dev/null >"$work/restate.stdout" 2>&1 || true
    check "restate: model/stm32h7-dma-restate.repl regenerates from the pinned stm32h7.repl by its generator, byte for byte" \
      "cmp -s '$work/restate/restate.repl' '$src/model/stm32h7-dma-restate.repl'" ;;
  *) echo "unknown case: $t" >&2; exit 2 ;;
  esac
done

echo "$fails failed"
[ $fails = 0 ]
