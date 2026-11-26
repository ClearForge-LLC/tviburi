#!/usr/bin/env bash
# tests/1308/dmamux.sh [SRC] — TVI-WO-1308's acceptance cases for the DMAMUX1 guard, run on the host as tests/1307/adc.sh is:
# each case one container in the pinned tools image (tests/spike.sh's IMAGE), no network, the source read-only. SRC defaults to
# a `git archive HEAD` export. tests/spike.sh runs each case as dmamux-<case> in its acceptance group.
#   TVIBURI_TESTS="pair routes ..."  run only these cases.
#   TVIBURI_CI=1                     plain `podman run`, for a disposable CI runner only; elsewhere ~/bin/capped-run.
# Exit 0 only if every selected case passed. Pages: RM0433 Rev 9 §17 (DMAMUX, pp. 696-717), §8.7.29 p433 (DMA1RST, DMA2RST),
# §8.7.41 p455 (DMA1EN, DMA2EN); the rulings of 2026-11-26T09:31Z (Q1-Q4) and the measurement in
# docs/work-orders/TVI-WO-1308.measure.md.
set -euo pipefail

here=$(cd "$(dirname "$0")/../.." && pwd)
IMAGE=$(sed -n 's/^IMAGE=//p' "$here/tests/spike.sh")
CASES="pair pairrec pairtx pinc pinc1 parmem routes fixture live livedone livesame reserved se ge byte misaligned read clock clockone rst rstroute reset recorded norcc addr registrations fields pairs ledger regen"
MONITOR="misaligned read clock clockone rst rstroute reset recorded norcc addr registrations fields pairs ledger regen"
PATHS=$(for c in $CASES; do case " $MONITOR " in *" $c "*) ;; *) printf '"%s",' "$c" ;; esac; done | sed 's/,$//')
scratch=${TVIBURI_SCRATCH:-$HOME/.cache/tviburi}
mkdir -p "$scratch"
work=$(mktemp -d "$scratch/dmamux.XXXXXX")

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
GUARDS=$("$src/scripts/guards")   # the one guard list (scripts/guards; TVI-WO-1204 (6))

fails=0
check() { if eval "$2"; then echo "ok   $1"; else echo "FAIL $1"; fails=$((fails + 1)); fi; }

# One container: build the check and (for a firmware case) the planted firmware, run Renode on the case's monitor script, then the
# check on the log, naming every guarded source. Outputs in $work/<name>. The board is main's; PLATFORM_SED, a sed script applied
# to it; PLATFORM, another platform file instead.
run_case() { # run_case <name> <firmware path or -> <monitor lines...>
  local name=$1 fw=$2; shift 2
  mkdir -p "$work/$name"
  if [ -n "${PLATFORM:-}" ]; then
    sed 's#^using "\./#using "/repo/model/#' "$src/${PLATFORM#/repo/}" > "$work/$name/platform.repl"
  else
    sed -e 's#^using "\./#using "/repo/model/#' "$src/model/tviburi-nucleo-h753zi.repl" | sed -e "${PLATFORM_SED:-}" > "$work/$name/platform.repl"
  fi
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
        --check-cfg "cfg(path, values($3))" -C link-arg=-T/repo/firmware/link.x -o /out/fw.elf /repo/firmware/dmamux.rs || exit 91
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
refuses() { # refuses <name> <rule> <kind> <python expr over r> [peripheral]: the first record, located (a PC)
  [ "$rc" = 3 ] && first "$1" "r[\"rule\"] == \"$2\" and r[\"kind\"] == \"$3\" and r[\"peripheral\"] == \"sysbus.${5:-dmamux1}\" and r[\"access\"][\"pc\"] != \"none\" and $4"
}
logged() { # logged <name> <python expr over `recs`, the records under <source> in the log, in order> [source]
  python3 -I -c '
import json, sys
s = sys.argv[3] + ": {\"tviburi\":\""
recs = [json.loads(l.split(sys.argv[3] + ": ", 1)[1]) for l in open(sys.argv[1]) if s in l and "\"rule\"" in l]
sys.exit(0 if eval("(" + sys.argv[2] + ")") else 1)
' "$work/$1/renode-log.txt" "$2" "${3:-dmamux1}"
}
haslines() { local n=$1 x; shift; for x in "$@"; do console "$n" | grep -qx "$x" || return 1; done; }   # haslines <name> <whole console lines...>
stock() { console "$1" | grep -qx "STOCK $2"; }   # stock <name> <the stock model's sixteen DMAREQ_IDs, comma-separated>
marks=('echo "TVIBURI-MARK-AFTER"' 'sysbus ReadDoubleWord 0x20000104' 'echo "R-READ"' 'sysbus ReadDoubleWord 0x20000108' 'echo "R-READ2"' 'sysbus ReadDoubleWord 0x2000010C')
clk='sysbus WriteDoubleWord 0x580244D8 0x3'   # DMA1EN, DMA2EN (p455)
rec='/^tviburiDmamux1:$/{n;s/enforcing/recorded/}'
# The stock model's own route per channel (its private requestId fields, by reflection): the guard's copy must never differ.
peek='python "from System.Reflection import BindingFlags as B; o = self.Machine[\"sysbus.dmamux1\"]; a = o.GetType().BaseType.GetField(\"requestId\", B.NonPublic | B.Instance).GetValue(o); print(\"STOCK \" + \",\".join([str(x.Value) for x in a]))"'
z15=0,0,0,0,0,0,0,0,0,0,0,0,0,0,0   # fifteen channels at 0
# DMA1 stream <n> (0-7), peripheral-to-memory, PAR <par>, 4 items into SRAM4, enabled; by the monitor (no PC), into the array P.
p2m() { local a; P=(); for a in "0x24 0x21" "0x18 $2" "0x1C 0x38001900" "0x14 0x4" "0x10 0x400" "0x10 0x401"; do
  P+=("$(printf 'sysbus WriteDoubleWord 0x%X %s' $((0x40020000 + 0x18 * $1 + ${a%% *})) "${a#* }")"); done; }
pulse=('sysbus.dmamux1 OnGPIO 41 true' 'sysbus.dmamux1 OnGPIO 41 false')   # one request on DMAMUX1 input 41, raised by hand

for t in ${TVIBURI_TESTS:-$CASES}; do
  case " $MONITOR " in *" $t "*) mon=1 ;; *) mon=0 ;; esac
  if [ $mon = 0 ]; then
    if [ "$t" = pairrec ]; then
      PLATFORM_SED=$rec run_case pairrec pair 'emulation RunFor "0.03"' "${marks[@]}"
    elif [ "$t" = parmem ]; then
      run_case "$t" "$t" 'sysbus WriteDoubleWord 0x24000000 0xA5' 'emulation RunFor "0.03"' "${marks[@]}" "${pulse[@]}" 'echo "R-NDTR"' 'sysbus ReadDoubleWord 0x40020014' \
        'echo "R-DST"' 'sysbus ReadDoubleWord 0x38001900'
    elif [ "$t" = byte ]; then
      run_case "$t" "$t" 'emulation RunFor "0.03"' "${marks[@]}" "${pulse[@]}" 'echo "R-NDTR"' 'sysbus ReadDoubleWord 0x40020044' "$peek"
    else
      run_case "$t" "$t" 'emulation RunFor "0.03"' "${marks[@]}"
    fi
  fi
  case $t in
  pair) check "pair (WO acceptance): a stream started on a channel whose route names another request (C0 = 41 usart1_rx_dma, PAR = SPI1_RXDR) refuses at its EN, TVI-DMAMUX-PAIR, unmodeled, located (a PC), naming USART1_RDR and PAR (Table 124 p698; Table 8 p134; p2098, p2228), the access located at DMA1_S0CR's EN write (offset 0x10, value 0x401)" \
      "refuses pair TVI-DMAMUX-PAIR unmodeled 'r[\"register\"] == \"DMAMUX1_C0CR\" and r[\"bit\"] == \"DMAREQ_ID = 41 (usart1_rx_dma) with DMA1_S0PAR = 0x40013030\" and \"USART1_RDR at 0x40011024\" in r[\"condition\"] and \"p2098\" in r[\"citation\"] and r[\"access\"][\"offset\"] == \"0x10\" and r[\"access\"][\"value\"] == \"0x401\"' && records pair 'len(recs) == 1'" ;;
  pairrec) check "pairrec: dmamux1 recorded, dma1 enforcing: the same stream records one would-refuse (TVI-DMAMUX-PAIR) and starts, as if no rule had fired (RUL-19): S0CR reads EN = 1; never a pass at the check" \
      "[ \$rc = 1 ] && logged pairrec 'len(recs) == 1 and recs[0][\"tviburi\"] == \"would-refuse\" and recs[0][\"rule\"] == \"TVI-DMAMUX-PAIR\"' && after pairrec && [ \"\$(readout pairrec READ)\" = 0x00000401 ]" ;;
  parmem) check "parmem (ruled Q18 (b)): a stream on 41 (usart1_rx_dma) with PAR in AXI SRAM (0x24000000), paced by the request, passes: EN reads 1, and a request on input 41 moves the byte at 0x24000000 into SRAM4 (NDTR 4 -> 3)" \
      "passes parmem && [ \"\$(readout parmem READ)\" = 0x00000401 ] && [ \"\$(readout parmem NDTR)\" = 0x00000003 ] && [ \$(( \$(readout parmem DST) & 0xFF )) = 165 ]" ;;
  pinc) check "pinc (ruled Q22 (a)): a stream on 41 with PAR = USART1_RDR but PINC = 1, PSIZE word and NDTR 4 refuses TVI-DMAMUX-PAIR, unmodeled, located at the EN write (DMA1_S0CR), naming the first address the span leaves the data register at (0x40011028)" \
      "refuses pinc TVI-DMAMUX-PAIR unmodeled 'r[\"bit\"] == \"DMAREQ_ID = 41 (usart1_rx_dma) with DMA1_S0PAR = 0x40011024, PINC = 1, NDTR = 4\" and \"leaves the data register at 0x40011028\" in r[\"condition\"] and r[\"access\"][\"offset\"] == \"0x10\" and r[\"access\"][\"value\"] == \"0x5601\" and \"p665\" in r[\"citation\"]' && records pinc 'len(recs) == 1'" ;;
  pinc1) check "pinc1: the same stream with NDTR 1 (its span is USART1_RDR alone) passes; EN reads 1" \
      "passes pinc1 && [ \"\$(readout pinc1 READ)\" = 0x00005601 ]" ;;
  pairtx) check "pairtx: a memory-to-peripheral stream on 42 (usart1_tx_dma) with PAR = USART3_TDR refuses TVI-DMAMUX-PAIR, unmodeled, naming USART1_TDR" \
      "refuses pairtx TVI-DMAMUX-PAIR unmodeled '\"USART1_TDR at 0x40011028\" in r[\"condition\"] and \"memory-to-peripheral\" in r[\"condition\"]'" ;;
  routes) check "routes: the armed run's ten streams (DMA1 S0-S7 on 41, 43, 45, 9, 38, 42, 40, 46; DMA2 S2, S3 on 39, 37), each PAR its request's data register, pass; C11CR reads 37 and C3CR 9 from the copy" \
      "passes routes && [ \"\$(readout routes READ)\" = 0x00000025 ] && [ \"\$(readout routes READ2)\" = 0x00000009 ]" ;;
  fixture) check "fixture: the fixture's six streams (C0 = 41, C1 = 38, C2 = 39, C3 = 40, C4 = 42, C9 = 37) pass; C9CR reads 37" \
      "passes fixture && [ \"\$(readout fixture READ)\" = 0x00000025 ]" ;;
  live) check "live (ruled Q3): C0CR rewritten 41 -> 43 while DMA1 stream 0 runs refuses TVI-DMAMUX-LIVE, unmodeled, located" \
      "refuses live TVI-DMAMUX-LIVE unmodeled 'r[\"register\"] == \"DMAMUX1_C0CR\" and r[\"bit\"] == \"DMAREQ_ID 41 -> 43 (DMA1 stream 0 running)\" and \"p703\" in r[\"citation\"]'" ;;
  livedone) check "livedone: C0CR rewritten 42 -> 46 while DMA1 stream 0 is completed by the twin with EN still set refuses TVI-DMAMUX-LIVE, unmodeled" \
      "refuses livedone TVI-DMAMUX-LIVE unmodeled 'r[\"bit\"] == \"DMAREQ_ID 42 -> 46 (DMA1 stream 0 completed, EN still set)\"'" ;;
  livesame) check "livesame (ruled Q3): C0CR rewritten with the same id while its stream runs passes (ArduPilot's 77,402 rewrites), and another id once the stream is off passes; C0CR reads 43" \
      "passes livesame && [ \"\$(readout livesame READ)\" = 0x0000002B ]" ;;
  reserved) check "reserved (ruled Q18 (a)): C0CR = 54 (Reserved, Table 124 p697) is taken at the write (reads 54); a stream started on it meets the DMA guard's TVI-DMA-ROUTE, predicted-fault, located, and dmamux1 records nothing" \
      "[ \$rc = 3 ] && records reserved 'len(recs) == 1 and recs[0][\"rule\"] == \"TVI-DMA-ROUTE\" and recs[0][\"kind\"] == \"predicted-fault\" and recs[0][\"peripheral\"] == \"sysbus.dma1\" and recs[0][\"access\"][\"pc\"] != \"none\" and recs[0][\"bit\"] == \"DMAMUX1_C0CR.DMAREQ_ID = 54 (reserved)\"' && [ \"\$(readout reserved READ)\" = 0x00000036 ]" ;;
 se) check "se: C0CR with SE = 1 (synchronisation, a tag allowed 0 only) refuses TVI-DMAMUX-FIELD, unmodeled, located (p708)" \
      "refuses se TVI-DMAMUX-FIELD unmodeled 'r[\"bit\"] == \"SE (bit 16) = 1\" and \"p708\" in r[\"citation\"]'" ;;
  ge) check "ge: RG3CR with GE = 1 (a request generator, a tag allowed 0 only, judged by RGxCR's rows) refuses TVI-DMAMUX-FIELD, unmodeled, located (p712)" \
      "refuses ge TVI-DMAMUX-FIELD unmodeled 'r[\"register\"] == \"DMAMUX1_RG3CR\" and r[\"bit\"] == \"GE (bit 16) = 1\" and \"p712\" in r[\"citation\"]'" ;;
  byte) check "byte (ruled Q2): a byte write of 41 to C2CR and a half-word write of 43 to C1CR route (p708): C1CR and C2CR read 43 and 41 as words and 41 as a byte, a stream on C2 with PAR = USART1_RDR passes, a request on input 41 moves one item (NDTR 4 -> 3), and the stock model holds the same routes" \
      "passes byte && [ \"\$(readout byte READ)\" = 0x0000292B ] && [ \"\$(readout byte READ2)\" = 0x00000029 ] && [ \"\$(readout byte NDTR)\" = 0x00000003 ] && stock byte 0,43,41,0,0,0,0,0,0,0,0,0,0,0,0,0" ;;
  misaligned)
    run_case misaligned - "$clk" 'sysbus WriteWord 0x40020801 0x29' 'echo "R-C0"' 'sysbus ReadDoubleWord 0x40020800'
    check "misaligned: a half-word write at offset 0x1 refuses TVI-DMAMUX-WIDTH, unmodeled (\"The address must be aligned with the data size\", p708), and changes nothing" \
      "[ \$rc = 3 ] && records misaligned 'len(recs) == 1 and recs[0][\"rule\"] == \"TVI-DMAMUX-WIDTH\" and recs[0][\"kind\"] == \"unmodeled\" and recs[0][\"bit\"] == \"misaligned 16-bit write\"' && [ \"\$(readout misaligned C0)\" = 0x00000000 ]" ;;
  read)
    run_case read - "$clk" 'sysbus ReadDoubleWord 0x40020840'
    check "read: a read of 0x40 (no register, Table 133 p716) refuses TVI-DMAMUX-READ, unmodeled" \
      "[ \$rc = 3 ] && first read 'r[\"rule\"] == \"TVI-DMAMUX-READ\" and r[\"kind\"] == \"unmodeled\" and r[\"access\"][\"offset\"] == \"0x40\"'" ;;
  clock)
    # Ruled Q4: with DMA1EN = DMA2EN = 0 a write is dropped and a read returns 0, with no record (p455, p373).
    run_case clock - 'sysbus WriteDoubleWord 0x40020800 0x29' "$peek" "$clk" 'echo "R-OFF"' 'sysbus ReadDoubleWord 0x40020800' \
      'sysbus WriteDoubleWord 0x40020800 0x2B' 'sysbus WriteDoubleWord 0x580244D8 0x0' 'echo "R-UNCLOCKED"' 'sysbus ReadDoubleWord 0x40020800' \
      'sysbus WriteDoubleWord 0x40020800 0x29' "$clk" 'echo "R-ON"' 'sysbus ReadDoubleWord 0x40020800' "$peek"
    check "clock (ruled Q4): a write with DMA1EN = DMA2EN = 0 is dropped (C0CR reads 0 once clocked, the stock model holds 0); a route reads 0 while unclocked and its value once clocked again; no record (p455, p373)" \
      "passed clock && [ \"\$(readout clock OFF)\" = 0x00000000 ] && [ \"\$(readout clock UNCLOCKED)\" = 0x00000000 ] && [ \"\$(readout clock ON)\" = 0x0000002B ] && stock clock 0,$z15 && stock clock 43,$z15" ;;
  clockone)
    run_case clockone - 'sysbus WriteDoubleWord 0x580244D8 0x2' 'sysbus WriteDoubleWord 0x40020800 0x29' 'echo "R-C0"' 'sysbus ReadDoubleWord 0x40020800'
    check "clockone: DMA2EN alone clocks DMAMUX1 (\"DMA2 and DMAMUX1 Clock Enable\", p455): the write is taken" \
      "passed clockone && [ \"\$(readout clockone C0)\" = 0x00000029 ]" ;;
  rst)
    # Ruled Q4: DMA1RST and DMA2RST each reset DMAMUX1 (p433), through the RCC guard's Written event; the stock model is reset
    # too, so a stream then enabled on channel 0 meets the DMA guard's TVI-DMA-ROUTE (it reads the stock model: no request).
    p2m 0 0x40011024
    run_case rst - "$clk" 'sysbus WriteDoubleWord 0x40020800 0x29' 'sysbus WriteDoubleWord 0x40020804 0x2B' 'sysbus WriteDoubleWord 0x58024480 0x1' \
      'sysbus WriteDoubleWord 0x58024480 0x0' 'echo "R-C0"' 'sysbus ReadDoubleWord 0x40020800' "$peek" 'sysbus WriteDoubleWord 0x40020800 0x29' \
      'sysbus WriteDoubleWord 0x58024480 0x2' 'sysbus WriteDoubleWord 0x58024480 0x0' 'echo "R-C0B"' 'sysbus ReadDoubleWord 0x40020800' "${P[@]}"
    check "rst (ruled Q4): a DMA1RST pulse resets every route (C0CR 41 and C1CR 43 read 0, the stock model's too), and a DMA2RST pulse again (p433); a stream then enabled on channel 0 meets TVI-DMA-ROUTE (no request, p703)" \
      "[ \$rc = 3 ] && [ \"\$(readout rst C0)\" = 0x00000000 ] && [ \"\$(readout rst C0B)\" = 0x00000000 ] && stock rst 0,$z15 && records rst 'len(recs) == 1 and recs[0][\"rule\"] == \"TVI-DMA-ROUTE\" and recs[0][\"bit\"] == \"DMAMUX1_C0CR.DMAREQ_ID = 0 (no request)\"'" ;;
  rstroute)
    # ArduPilot's order (measure.md §2.1): DMA1EN, the AHB1 reset pulsed (0x7FFFFFFF, then 0), then the route and the stream.
    p2m 0 0x40011024
    run_case rstroute - "$clk" 'sysbus WriteDoubleWord 0x58024480 0x7FFFFFFF' 'sysbus WriteDoubleWord 0x58024480 0x0' "$clk" \
      'sysbus WriteDoubleWord 0x40020800 0x29' "${P[@]}" 'echo "R-C0"' 'sysbus ReadDoubleWord 0x40020800'
    check "rstroute: the clock, an AHB1 reset pulse, then a route and its stream (ArduPilot's order) pass; C0CR reads 41" \
      "passed rstroute && [ \"\$(readout rstroute C0)\" = 0x00000029 ]" ;;
  reset)
    p2m 0 0x40011024
    run_case reset - "$clk" 'sysbus WriteDoubleWord 0x40020800 0x29' 'machine Reset' "$clk" 'echo "R-C0"' 'sysbus ReadDoubleWord 0x40020800' "$peek" "${P[@]}"
    check "reset: machine Reset clears the copy and the stock model (C0CR reads 0; a stream on channel 0 meets TVI-DMA-ROUTE)" \
      "[ \$rc = 3 ] && [ \"\$(readout reset C0)\" = 0x00000000 ] && stock reset 0,$z15 && records reset 'len(recs) == 1 and recs[0][\"rule\"] == \"TVI-DMA-ROUTE\"'" ;;
  recorded)
    PLATFORM_SED=$rec run_case recorded - "$clk" 'sysbus WriteDoubleWord 0x40020800 0x10029' 'echo "R-C0"' 'sysbus ReadDoubleWord 0x40020800' \
      'sysbus WriteDoubleWord 0x40020804 0x36' 'echo "R-C1"' 'sysbus ReadDoubleWord 0x40020804' "$peek"
    check "recorded: dmamux1 recorded: SE = 1 records one would-refuse (TVI-DMAMUX-FIELD) and goes on to the stock model (RUL-19): C0CR reads 41 (SE is not kept); a reserved id (54) is taken with no record (Q18 (a)), C1CR 54; the stock model holds both; never a pass at the check" \
      "[ \$rc = 1 ] && logged recorded 'len(recs) == 1 and recs[0][\"tviburi\"] == \"would-refuse\" and recs[0][\"rule\"] == \"TVI-DMAMUX-FIELD\" and recs[0][\"bit\"] == \"SE (bit 16) = 1\"' && [ \"\$(readout recorded C0)\" = 0x00000029 ] && [ \"\$(readout recorded C1)\" = 0x00000036 ] && stock recorded 41,54,0,0,0,0,0,0,0,0,0,0,0,0,0,0" ;;
 norcc)
    PLATFORM=/repo/tests/1308/norcc.repl run_case norcc - "$clk" 'sysbus WriteDoubleWord 0x40020800 0x29'
    check "norcc: a DMAMUX1 guard on a machine with no RCC guard refuses its access, TVI-DMAMUX-CONFIG, unmodeled: its clock and reset cannot be watched" \
      "[ \$rc = 3 ] && records norcc 'len(recs) == 1 and recs[0][\"rule\"] == \"TVI-DMAMUX-CONFIG\" and \"no RCC guard\" in recs[0][\"condition\"]'" ;;
  addr)
    # TVI-WO-1308 adversarial pass: the restatement moved to 0x40020C00 (the board's own `using` of it and its mode block dropped).
    sed 's/^tviburiDmamux1: Tviburi.GuardedSTM32H7_DMAMUX @ sysbus 0x40020800 as/tviburiDmamux1: Tviburi.GuardedSTM32H7_DMAMUX @ sysbus 0x40020C00 as/' \
      "$src/model/stm32h7-dmamux-restate.repl" > "$work/addr.repl"
    PLATFORM_SED='$r '"$work/addr.repl"$'\n''/^using "\/repo\/model\/stm32h7-dmamux-restate.repl"$/d;/^tviburiDmamux1:$/,/^    mode: "enforcing"$/d' \
      run_case addr - "$clk" 'sysbus ReadDoubleWord 0x40020C00'
    check "addr: a DMAMUX1 guard declared at 0x40020C00, not DMAMUX1's 0x40020800 (Table 8 p134), refuses its access, TVI-DMAMUX-CONFIG, unmodeled" \
      "[ \$rc = 3 ] && records addr 'len(recs) == 1 and recs[0][\"rule\"] == \"TVI-DMAMUX-CONFIG\" and \"bus address 0x40020C00\" in recs[0][\"condition\"]'" ;;
  registrations)
    # Ruled Q1: every reference to the variable dmamux1 is restated onto the guard, read back by reflection (the DMA guards'
    # `dmamux`, the ADC guards' `dma`) and from the connections (spi4's DMARecieve, crypto's OutDMA); a request on input 41
    # routed to channel 2 moves one item on DMA1 stream 2 through the guard.
    f() { printf 'python "from System.Reflection import BindingFlags as B; o = self.Machine[\\"sysbus.%s\\"]; print(\\"F %s \\" + o.GetType().GetField(\\"%s\\", B.NonPublic | B.Instance).GetValue(o).GetType().Name)"' "$1" "$1" "$2"; }
    e() { printf 'python "print(\\"E %s \\" + \\",\\".join([x.Receiver.GetType().Name + \\"@\\" + str(x.Number) for x in self.Machine[\\"sysbus.%s\\"].%s.Endpoints]))"' "$1" "$1" "$2"; }
    p2m 2 0x40011024
    run_case registrations - 'peripherals' "$clk" 'sysbus LogPeripheralAccess sysbus.dmamux1 true' "$(f dma1 dmamux)" "$(f dma2 dmamux)" "$(f adcM1S2 dma)" "$(f adc3 dma)" \
      "$(e spi4 DMARecieve)" "$(e crypto OutDMA)" 'sysbus WriteDoubleWord 0x40020808 0x29' "${P[@]}" "${pulse[@]}" 'echo "R-NDTR"' 'sysbus ReadDoubleWord 0x40020044'
    check "registrations (ruled Q1): peripherals lists dmamux1 once, as the guard; the DMA guards' dmamux and the ADC guards' dma are the guard, spi4's DMARecieve reaches it at 83 and crypto's OutDMA at 77; the access log names CxCR as the stock model does; a request on input 41 moves one item (NDTR 4 -> 3)" \
      "passed registrations && [ \$(grep -c '(GuardedSTM32H7_DMAMUX)' '$work/registrations/renode.out') = 1 ] && grep -q 'dmamux1 (GuardedSTM32H7_DMAMUX)' '$work/registrations/renode.out' \
       && haslines registrations 'F dma1 GuardedSTM32H7_DMAMUX' 'F dma2 GuardedSTM32H7_DMAMUX' 'F adcM1S2 GuardedSTM32H7_DMAMUX' 'F adc3 GuardedSTM32H7_DMAMUX' 'E spi4 GuardedSTM32H7_DMAMUX@83' 'E crypto GuardedSTM32H7_DMAMUX@77' \
       && grep -q 'dmamux1: WriteUInt32 to 0x8 (RequestLineMultiplexerChannelConfiguration+0x8), value 0x29' '$work/registrations/renode-log.txt' && [ \"\$(readout registrations NDTR)\" = 0x00000003 ]" ;;
  fields)
    # Gate clause 6 (RUL-17), the cases generated from the release's ledger: each field row's disallowed value refuses
    # TVI-DMAMUX-FIELD, unmodeled, naming the field; each allowed value passes; every offset with no register refuses.
    mkdir -p "$work/fields-cases"
    container -v "$src:/repo:ro,Z" -v "$work/fields-cases:/out:Z" "$IMAGE" sh -c '/repo/scripts/build-release >/dev/null &&
      /out/release.target/debug/tviburi-release cases /out/release/LEDGER.jsonl Tviburi.GuardedSTM32H7_DMAMUX 0x40020800 0x400 0x580244D8 0x3 >/out/cases.txt' </dev/null >"$work/fields-cases.stdout" 2>&1
    mapfile -t lines < "$work/fields-cases/cases.txt"
    run_case fields - "${lines[@]}"
    check "fields: every ledger row's disallowed value refuses by name, unmodeled (the synchronisation, event and generator fields, the reserved bits); every allowed value passes (DMAREQ_ID 127 among them); channels 1-15 and generators 1-7 judged by C0's and RG0's rows; no-register offsets refuse (gate clause 6)" \
      "python3 -I - '$work/fields-cases/cases.txt' '$work/fields.stdout' <<'EOF'
import json, sys
cases = {}
for l in open(sys.argv[1]):
    if l.startswith('# CASE '):
        kind, name, off, val = l.split()[2:6]
        o, v = int(off, 16), int(val, 16)
        # The generator knows one offset per register; CxCR 1-15 (0x04-0x3C) and RGxCR 1-7 (0x104-0x11C) are C0's and RG0's
        # rows, which allow 0 in every field, so a 0 written there passes.
        if name == 'none' and (0x04 <= o < 0x40 or 0x104 <= o < 0x120):
            kind, name = 'pass', 'channel'
        cases[(o, v)] = (kind, name)
recs = [json.loads(l[8:]) for l in open(sys.argv[2]) if l.startswith('REFUSAL ')]
seen, bad = {}, []
for r in recs:
    k = (int(r['access']['offset'], 16), int(r['access']['value'], 16))
    if k not in cases or k in seen:
        bad.append(('not one record per case', r['rule'], r['bit'])); continue
    seen[k] = r
    kind, name = cases[k]
    if kind == 'pass' or r['rule'] != 'TVI-DMAMUX-FIELD' or r['kind'] != 'unmodeled':
        bad.append((kind, name, r['rule'], r['bit']))
    elif not (r['bit'].startswith(name.split('.')[1] + ' (') if name != 'none' else r['bit'] == 'write to offset 0x%X' % k[0]):
        bad.append(('wrong field', name, r['bit']))
missing = [v for k, v in cases.items() if v[0] == 'refuse' and k not in seen]
passes = sum(1 for v in cases.values() if v[0] == 'pass')
print(len(cases), 'cases;', passes, 'pass;', len(recs), 'refusals;', 'bad', bad[:5], 'missing', missing[:5])
sys.exit(0 if not bad and not missing and len(cases) > 250 and passes > 10 else 1)
EOF" ;;
  pairs)
    # Ruled Q3: the guard holds a data register for exactly the requests the DMA guard raises or acknowledges, each in its
    # direction; a request it does not hold would refuse at its EN (TVI-DMAMUX-PAIR), and one the DMA guard drops would never
    # be judged.
    check "pairs: the guard's Pairs are exactly the DMA guard's RaisedToMemory (to memory) and RaisedFromMemory (from memory)" \
      "python3 -I - '$src/model/GuardedSTM32DMA.cs' '$src/model/GuardedSTM32H7_DMAMUX.cs' <<'EOF'
import re, sys
dma, mux = open(sys.argv[1]).read(), open(sys.argv[2]).read()
ids = lambda name: sorted(int(x) for x in re.search(r'uint\[\] ' + name + r' = \{([^}]*)\}', dma).group(1).split(','))
pairs = re.findall(r'new Pair\((\d+), \"[^\"]+\", (true|false), (0x[0-9A-F]{8}),', mux)
to = sorted(int(i) for i, d, _ in pairs if d == 'true')
fr = sorted(int(i) for i, d, _ in pairs if d == 'false')
print('to', to, ids('RaisedToMemory'), 'from', fr, ids('RaisedFromMemory'))
sys.exit(0 if to == ids('RaisedToMemory') and fr == ids('RaisedFromMemory') and len(pairs) == 13 else 1)
EOF" ;;
  ledger)
    mkdir -p "$work/ledger"
    rc=0; container -v "$src:/repo:ro,Z" -v "$work/ledger:/out:Z" "$IMAGE" sh -c '/repo/scripts/build-release /repo /out/release >/dev/null' </dev/null >"$work/ledger.stdout" 2>&1 || rc=$?
    check "ledger: the release carries the DMAMUX1 guard's field rows over its 6 registers (DMAREQ_ID modeled, any id allowed, a reserved one included (Q18 (a)); SE, EGE, SOIE, SPOL, NBREQ, SYNC_ID, every RGxCR field and every reserved field of a writable register 0 only); the manifest restates dmamux1; the stock type's model row, guarded, lists it" \
      "[ \$rc = 0 ] && grep -qx 'class Miscellaneous.STM32_DMAMUX Tviburi.GuardedSTM32H7_DMAMUX' '$work/ledger/release/MANIFEST' && [ \"\$(grep -c '^restate Miscellaneous.STM32_DMAMUX dmamux1 ' '$work/ledger/release/MANIFEST')\" = 1 ] && python3 -I -c '
import json, sys
rows = [json.loads(l) for l in open(sys.argv[1]).read().splitlines()[1:]]
g = \"Tviburi.GuardedSTM32H7_DMAMUX\"
f = [r for r in rows if r.get(\"peripheral\") == g and r[\"row\"] == \"field\"]
by = {(r[\"register\"], r[\"field\"]): r for r in f}
ok = len(f) == 26 and len({r[\"register\"] for r in f}) == 6
d = by[(\"DMAMUX1_CxCR\", \"DMAREQ_ID\")]
ok = ok and d[\"state\"] == \"guard\" and d[\"allowed\"] == \"any\"
ok = ok and all(by[(\"DMAMUX1_CxCR\", x)][\"allowed\"] == [0] and by[(\"DMAMUX1_CxCR\", x)][\"state\"] == \"tag\" for x in (\"SE\", \"EGE\", \"SOIE\", \"SPOL\", \"NBREQ\", \"SYNC_ID\"))
ok = ok and all(by[(\"DMAMUX1_RGxCR\", x)][\"allowed\"] == [0] for x in (\"SIG_ID\", \"OIE\", \"GE\", \"GPOL\", \"GNBREQ\"))
ok = ok and sorted(r[\"field\"] for r in f if r[\"state\"] == \"reserved\" and r[\"allowed\"] == [0]) == [\"Res15_10\", \"Res15_9\", \"Res31_16\", \"Res31_16\", \"Res31_24\", \"Res31_27\", \"Res7\", \"Res7_3\"]
m = [r for r in rows if r[\"row\"] == \"model\" and r[\"peripheral\"] == \"Miscellaneous.STM32_DMAMUX\"]
ok = ok and len(m) == 1 and m[0].get(\"guarded_by\") == g and m[0][\"instances\"] == [\"dmamux1\"]
sys.exit(0 if ok else 1)
' '$work/ledger/release/LEDGER.jsonl'" ;;
  regen)
    mkdir -p "$work/regen"
    rc=0; container -v "$src:/repo:ro,Z" -v "$work/regen:/out:Z" "$IMAGE" sh -c 'awk -v sha=$(sha256sum /opt/renode/platforms/cpus/stm32h7.repl | cut -c1-64) \
      -f /repo/model/stm32h7-dmamux-restate.awk /opt/renode/platforms/cpus/stm32h7.repl > /out/restate.repl' </dev/null >"$work/regen.stdout" 2>&1 || rc=$?
    check "regen: the restatement regenerates from the pinned stm32h7.repl, byte for byte (RUL-09 (b))" \
      "[ \$rc = 0 ] && cmp -s '$work/regen/restate.repl' '$src/model/stm32h7-dmamux-restate.repl'" ;;
  *) echo "unknown case: $t" >&2; exit 2 ;;
  esac
done
[ $fails -eq 0 ] && echo "ALL PASS" || echo "$fails FAILED"
[ $fails -eq 0 ]
