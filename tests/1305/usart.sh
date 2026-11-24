#!/usr/bin/env bash
# tests/1305/usart.sh [SRC] — TVI-WO-1305's acceptance cases for the USART guard, run on the host as tests/1304/flash.sh is:
# each case one container in the pinned tools image (tests/spike.sh's IMAGE), no network, the source read-only. SRC defaults
# to a `git archive HEAD` export. tests/spike.sh runs each case as usart-<case> in its acceptance group.
#   TVIBURI_TESTS="config m1ue ..."  run only these cases.
#   TVIBURI_CI=1                     plain `podman run`, for a disposable CI runner only; elsewhere ~/bin/capped-run.
# Exit 0 only if every selected case passed. Pages: RM0433 Rev 9 §48 (USART); the rulings of 2026-11-24T18:55Z (Q1-Q9) and
# the measurement in docs/work-orders/TVI-WO-1305.measure.md.
set -euo pipefail

here=$(cd "$(dirname "$0")/../.." && pwd)
IMAGE=$(sed -n 's/^IMAGE=//p' "$here/tests/spike.sh")
CASES="config m1ue over8ue psue psoff stopue reinit reinitstop disable same ap width tdrbyte read field nine kernel clock idle dmarx fields ledger registrations recorded reset config2 regen"
GUARDS="spi1 rcc gpioPortA gpioPortB gpioPortC gpioPortD gpioPortE gpioPortF gpioPortG gpioPortH gpioPortI gpioPortJ gpioPortK timer1 timer8 dma1 dma2 flashController usart1 usart2 usart3 uart4 uart5 usart6 uart7 uart8"
MONITOR="clock idle dmarx fields ledger registrations recorded reset config2 regen"
PATHS=$(for c in $CASES; do case " $MONITOR " in *" $c "*) ;; *) printf '"%s",' "$c" ;; esac; done | sed 's/,$//')
scratch=${TVIBURI_SCRATCH:-$HOME/.cache/tviburi}
mkdir -p "$scratch"
work=$(mktemp -d "$scratch/usart.XXXXXX")

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

# One container: build the check and (for a firmware case) the planted firmware, run Renode on the case's monitor script,
# then the check on the log, naming every guarded source. Outputs in $work/<name>. PLATFORM_SED: a sed script applied to
# the board.
run_case() { # run_case <name> <firmware path or -> <monitor lines...>
  local name=$1 fw=$2; shift 2
  mkdir -p "$work/$name"
  sed -e 's#^using "\./#using "/repo/model/#' -e "${PLATFORM_SED:-}" "$src/model/tviburi-nucleo-h753zi.repl" > "$work/$name/platform.repl"
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
        --check-cfg "cfg(path, values($3))" -C link-arg=-T/repo/firmware/link.x -o /out/fw.elf /repo/firmware/usart.rs || exit 91
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
refuses() { [ "$rc" = 3 ] && first "$1" "r[\"rule\"] == \"$2\" and r[\"kind\"] == \"$3\" and r[\"peripheral\"] == \"sysbus.usart1\" and r[\"access\"][\"pc\"] != \"none\" and $4"; }
bit() { python3 -I -c 'import sys; sys.exit(0 if int(sys.argv[1], 16) >> int(sys.argv[2]) & 1 == int(sys.argv[3]) else 1)' "$@"; }   # bit <hex> <n> <0|1>
marks=('echo "TVIBURI-MARK-AFTER"' 'sysbus ReadDoubleWord 0x20000104' 'echo "R-READ"' 'sysbus ReadDoubleWord 0x20000108' 'echo "R-READ2"' 'sysbus ReadDoubleWord 0x2000010C')
clk='sysbus WriteDoubleWord 0x580244F0 0x10'   # USART1EN (p470): an unclocked port is locked on the bus (TVI-WO-1300a)
rx='/^tviburiUsart1:$/a\    ReceiveDmaRequest -> dmamux1@41'   # the consumer's request line (Aethelgard AET-WO-4400i), on the guard

for t in ${TVIBURI_TESTS:-$CASES}; do
  case " $MONITOR " in *" $t "*) mon=1 ;; *) mon=0 ;; esac
  if [ $mon = 0 ]; then
    run_case "$t" "$t" 'emulation RunFor "0.001"' "${marks[@]}"
  fi
  case $t in
  config) check "config: the generated firmware's sequence (BRR, CR2 = 0, CR1 = FIFOEN|TE|RE, then with UE) passes, its kernel clock running; CR1 reads 0x2000000D (FIFOEN served from the guard's copy; the stock model drops it)" \
    "passes config && [ \"\$(readout config READ)\" = 0x2000000D ]" ;;
  m1ue) check "m1ue: CR1.M1 changed with UE = 1 refuses TVI-USART-UE, unmodeled (p2068; ruled Q2: the manual does not say what silicon does)" \
    "refuses m1ue TVI-USART-UE unmodeled 'r[\"access\"][\"offset\"] == \"0x0\" and r[\"bit\"] == \"M1 (bit 28) changed with UE = 1\" and \"p2068\" in r[\"citation\"]'" ;;
  over8ue) check "over8ue: CR1.OVER8 changed with UE = 1 refuses TVI-USART-UE, unmodeled (p2068)" \
    "refuses over8ue TVI-USART-UE unmodeled 'r[\"bit\"] == \"OVER8 (bit 15) changed with UE = 1\" and \"0 -> 1\" in r[\"condition\"]'" ;;
  psue) check "psue: CR1.PCE and PS changed with UE = 1 refuses TVI-USART-UE naming PS only: PCE alone is the acknowledged re-init pattern (p2069; ruled Q2 (c))" \
    "refuses psue TVI-USART-UE unmodeled 'r[\"bit\"] == \"PCE (bit 10), PS (bit 9) changed with UE = 1\" and \"p2069\" in r[\"citation\"]'" ;;
  psoff) check "psoff: the same PCE|PS change made with UE = 0, then UE, passes; CR1 reads 0x2000060D" \
    "passes psoff && [ \"\$(readout psoff READ)\" = 0x2000060D ]" ;;
  stopue) check "stopue: CR2.STOP changed with UE = 1 refuses TVI-USART-UE (p2076)" \
    "refuses stopue TVI-USART-UE unmodeled 'r[\"access\"][\"offset\"] == \"0x4\" and r[\"bit\"] == \"STOP (13:12) changed with UE = 1\" and \"p2076\" in r[\"citation\"]'" ;;
  reinit) check "reinit: ChibiOS's usart_init on the enabled port (BRR, CR2.RXINV, CR1.M0|PCE, all with UE = 1) passes, acknowledged by name and taken (ruled Q2 (c)); BRR reads 0x2D0, CR2 0x10000" \
    "passes reinit && [ \"\$(readout reinit READ)\" = 0x000002D0 ] && [ \"\$(readout reinit READ2)\" = 0x00010000 ]" ;;
  reinitstop) check "reinitstop: the same re-init also changing STOP refuses TVI-USART-UE at the CR2 write, naming STOP only (outside the pattern)" \
    "refuses reinitstop TVI-USART-UE unmodeled 'r[\"access\"][\"offset\"] == \"0x4\" and r[\"bit\"] == \"STOP (13:12) changed with UE = 1\"'" ;;
  disable) check "disable: CR1 = 0 from UE = 1 (ChibiOS's sdStop, clearing FIFOEN in the same write) passes, judged as with UE = 0 (ruled Q2); CR1 reads 0" \
    "passes disable && [ \"\$(readout disable READ)\" = 0x00000000 ]" ;;
  same) check "same: BRR, CR2 and CR1 written again unchanged with UE = 1 pass (RUL-18)" "passes same" ;;
  ap) check "ap: ArduPilot's USART1 sequence (BRR 0x271, CR2 0x40, CR3 0xC1, CR1 0x2000013D, ICR all ones, CR1's read-modify-write) passes, enforcing; CR1 reads 0x2000011D (FIFOEN and IDLEIE kept, as silicon: the stock model reads 0x10D) and CR2 0x40" \
    "passes ap && [ \"\$(readout ap READ)\" = 0x2000011D ] && [ \"\$(readout ap READ2)\" = 0x00000040 ]" ;;
  width) check "width: a byte write to CR1 refuses TVI-USART-WIDTH, unmodeled (p2067)" \
    "refuses width TVI-USART-WIDTH unmodeled 'r[\"access\"][\"width\"] == 8 and r[\"bit\"] == \"8-bit write\" and \"p2067\" in r[\"citation\"]'" ;;
  tdrbyte) check "tdrbyte: a byte write to TDR passes (ruled Q7: ArduPilot's DMA writes it by bytes) and is sent: ISR.TC reads 1" \
    "passes tdrbyte && bit \"\$(readout tdrbyte READ)\" 6 1" ;;
  read) check "read: a read at 0x30 refuses TVI-USART-READ, unmodeled" \
    "refuses read TVI-USART-READ unmodeled 'r[\"bit\"] == \"read of offset 0x30\"'" ;;
  field) check "field: CR3.HDSEL = 1 refuses TVI-USART-FIELD, unmodeled (the allow-list, ruled Q8)" \
    "refuses field TVI-USART-FIELD unmodeled 'r[\"bit\"] == \"HDSEL (bit 3) = 1\" and \"p2081\" in r[\"citation\"]'" ;;
  nine) check "nine: CR1.M0 = 1 with PCE = 0 (9 data bits) refuses TVI-USART-FIELD, unmodeled" \
    "refuses nine TVI-USART-FIELD unmodeled 'r[\"bit\"] == \"M0 (bit 12) = 1 with PCE = 0\"'" ;;
  kernel) check "kernel: USART1 started with USART16SEL = 010 and PLL3 off refuses TVI-USART-KERNEL, predicted-fault, at the CR1 write that sets UE, through the RCC guard's query (p2019, p371, p417)" \
    "refuses kernel TVI-USART-KERNEL predicted-fault 'r[\"access\"][\"value\"] == \"0x2000000D\" and r[\"bit\"] == \"RCC_D2CCIP2R.USART16SEL = 010\" and \"p2019\" in r[\"citation\"] and \"p417\" in r[\"citation\"]'" ;;
  clock)
    # With USART1EN = 0 the RCC guard locks the port on the bus: the write is dropped there with no record (p373; WO (2)).
    run_case clock - 'sysbus WriteDoubleWord 0x40011000 0x1' "$clk" 'echo "R-CR1"' 'sysbus ReadDoubleWord 0x40011000'
    check "clock: CR1 written with USART1EN = 0 is dropped by the bus lock with no record (p373): it reads 0 once clocked, and the run passes" \
      "passed clock && grep -q 'Tried to write a locked peripheral: machine-0.usart1. Address 0x40011000' '$work/clock/renode.out' && [ \"\$(readout clock CR1)\" = 0x00000000 ]" ;;
  idle)
    # IDLE (p2090, p2021; ruled Q5) at 320,000 baud (the stock frequency 200 MHz over BRR 0x271): a frame of 10 bits is 31.25 us.
    run_case idle - 'cpu IsHalted true' "$clk" 'sysbus WriteDoubleWord 0x4001100C 0x271' 'sysbus WriteDoubleWord 0x40011000 0x2000001D' \
      'emulation RunFor "0.001"' 'echo "R-START"' 'sysbus ReadDoubleWord 0x4001101C' \
      'sysbus.usart1 WriteChar 0x41' 'emulation RunFor "0.00002"' 'echo "R-EARLY"' 'sysbus ReadDoubleWord 0x4001101C' \
      'emulation RunFor "0.00002"' 'echo "R-IDLE"' 'sysbus ReadDoubleWord 0x4001101C' 'echo "R-IRQ"' 'sysbus.usart1 IRQ' \
      'sysbus WriteDoubleWord 0x40011020 0x10' 'echo "R-CLEARED"' 'sysbus ReadDoubleWord 0x4001101C' 'echo "R-IRQ2"' 'sysbus.usart1 IRQ' \
      'emulation RunFor "0.001"' 'echo "R-AGAIN"' 'sysbus ReadDoubleWord 0x4001101C' \
      'sysbus.usart1 WriteChar 0x42' 'emulation RunFor "0.0001"' 'sysbus WriteDoubleWord 0x40011000 0x2000001C' 'echo "R-OFF"' 'sysbus ReadDoubleWord 0x4001101C'
    check "idle: no IDLE at a start with nothing received (ruled Q5); a byte, then IDLE only after one idle frame (31.25 us: clear at 20, set at 40), raising the interrupt with IDLEIE; IDLECF clears it and it is not set again with no new byte (p2090); UE cleared resets IDLE and RXFNE (p2070)" \
      "passed idle && bit \"\$(readout idle START)\" 4 0 && bit \"\$(readout idle EARLY)\" 4 0 && bit \"\$(readout idle EARLY)\" 5 1 && bit \"\$(readout idle IDLE)\" 4 1 \
       && [ \"\$(readout idle IRQ)\" = 'GPIO: set' ] && bit \"\$(readout idle CLEARED)\" 4 0 && [ \"\$(readout idle IRQ2)\" = 'GPIO: unset' ] \
       && bit \"\$(readout idle AGAIN)\" 4 0 && bit \"\$(readout idle OFF)\" 4 0 && bit \"\$(readout idle OFF)\" 5 0" ;;
  dmarx)
    # The receive request (p2048, p2070; ruled Q6), with the platform's request line: DMA1 stream 0 peripheral-to-memory on
    # request 41 into AXI SRAM. (1) armed first, three bytes; (2) a byte while the stream is disabled, then re-armed (the stock
    # model never moved it, measure.md U11); (3) DMAR set after a byte waits (U12).
    PLATFORM_SED="$rx" run_case dmarx - 'cpu IsHalted true' "$clk" 'sysbus WriteDoubleWord 0x580244D8 0x1' 'sysbus WriteDoubleWord 0x4001100C 0x271' \
      'sysbus WriteDoubleWord 0x24000000 0xFFFFFFFF' 'sysbus WriteDoubleWord 0x24000100 0xFFFFFFFF' \
      'sysbus WriteDoubleWord 0x40020800 0x29' 'sysbus WriteDoubleWord 0x40020018 0x40011024' 'sysbus WriteDoubleWord 0x4002001C 0x24000000' \
      'sysbus WriteDoubleWord 0x40020014 0x8' 'sysbus WriteDoubleWord 0x40020010 0x400' 'sysbus WriteDoubleWord 0x40020010 0x401' \
      'sysbus WriteDoubleWord 0x40011008 0x40' 'sysbus WriteDoubleWord 0x40011000 0x2000000D' \
      'sysbus.usart1 WriteChar 0x81' 'sysbus.usart1 WriteChar 0x82' 'sysbus.usart1 WriteChar 0x83' \
      'echo "R-NDTR1"' 'sysbus ReadDoubleWord 0x40020014' 'echo "R-MEM1"' 'sysbus ReadDoubleWord 0x24000000' \
      'sysbus WriteDoubleWord 0x40020010 0x400' 'sysbus.usart1 WriteChar 0x91' \
      'sysbus WriteDoubleWord 0x40020008 0x3D' 'sysbus WriteDoubleWord 0x4002001C 0x24000100' 'sysbus WriteDoubleWord 0x40020014 0x8' \
      'sysbus WriteDoubleWord 0x40020010 0x401' 'emulation RunFor "0.0001"' \
      'echo "R-NDTR2"' 'sysbus ReadDoubleWord 0x40020014' 'echo "R-MEM2"' 'sysbus ReadDoubleWord 0x24000100' \
      'sysbus WriteDoubleWord 0x40011008 0x0' 'sysbus.usart1 WriteChar 0xA1' 'sysbus WriteDoubleWord 0x40011008 0x40' \
      'echo "R-NDTR3"' 'sysbus ReadDoubleWord 0x40020014' 'echo "R-MEM3"' 'sysbus ReadDoubleWord 0x24000100'
    check "dmarx: with DMAR = 1 each received byte moves (NDTR 8 -> 5); a byte received while the stream was disabled moves once it is armed (within a frame), and one waiting when DMAR is set moves at that write: silicon's level request (p2048), where the stock model moved neither (U11, U12)" \
      "passed dmarx && [ \"\$(readout dmarx NDTR1)\" = 0x00000005 ] && [ \"\$(readout dmarx MEM1)\" = 0xFF838281 ] \
       && [ \"\$(readout dmarx NDTR2)\" = 0x00000007 ] && [ \"\$(readout dmarx MEM2)\" = 0xFFFFFF91 ] \
       && [ \"\$(readout dmarx NDTR3)\" = 0x00000006 ] && [ \"\$(readout dmarx MEM3)\" = 0xFFFFA191 ]" ;;
  fields)
    # Gate clause 6 (RUL-17), the cases generated from the release's ledger, each from reset with USART1EN set: each field
    # row's disallowed value refuses TVI-USART-FIELD, unmodeled, naming the field; each allowed value passes, but CR1.M0 = 1
    # alone (9 data bits), which refuses by the cross-field rule; ISR and RDR take no write; an offset with no register refuses.
    mkdir -p "$work/fields-cases"
    container -v "$src:/repo:ro,Z" -v "$work/fields-cases:/out:Z" "$IMAGE" sh -c '/repo/scripts/build-release >/dev/null &&
      /out/release.target/debug/tviburi-release cases /out/release/LEDGER.jsonl Tviburi.GuardedSTM32F7_USART 0x40011000 0x400 0x580244F0 0x10 >/out/cases.txt' </dev/null >"$work/fields-cases.stdout" 2>&1
    mapfile -t lines < "$work/fields-cases/cases.txt"
    run_case fields - "${lines[@]}"
    check "fields: every ledger row's disallowed value refuses by name, unmodeled; allowed values pass (M0 alone refuses as 9 data bits); other offsets refuse (gate clause 6)" \
      "python3 -I - '$work/fields-cases/cases.txt' '$work/fields.stdout' <<'EOF'
import json, sys
cases = {}
for l in open(sys.argv[1]):
    if l.startswith('# CASE '):
        kind, name, off, val = l.split()[2:6]
        cases[(int(off, 16), int(val, 16))] = (kind, name)
recs = [json.loads(l[8:]) for l in open(sys.argv[2]) if l.startswith('REFUSAL ')]
seen, bad = {}, []
for r in recs:
    k = (int(r['access']['offset'], 16), int(r['access']['value'], 16))
    if k not in cases or k in seen:
        bad.append(('not one record per case', r['rule'], r['bit'])); continue
    seen[k] = r
    kind, name = cases[k]
    field = name.split('.', 1)[-1]
    if name == 'none':
        if r['rule'] != 'TVI-USART-FIELD' or r['bit'] != 'write to offset 0x%X' % k[0]: bad.append(('none', hex(k[0]), r['rule']))
    elif name == 'USART_CR1.M0' and kind == 'pass':
        if r['bit'] != 'M0 (bit 12) = 1 with PCE = 0': bad.append(('nine', r['bit']))
    elif kind == 'pass' or r['rule'] != 'TVI-USART-FIELD' or r['kind'] != 'unmodeled':
        bad.append((kind, name, r['rule'], r['bit']))
    elif not r['bit'].startswith(field + ' ('):
        bad.append(('wrong field', name, r['bit']))
missing = [(v, hex(k[0])) for k, v in cases.items() if (v[0] == 'refuse' or v[1] == 'USART_CR1.M0') and k not in seen]
print(len(cases), 'cases;', len(recs), 'refusals;', 'bad', bad[:5], 'missing', missing[:5])
sys.exit(0 if not bad and not missing and len(cases) > 400 else 1)
EOF" ;;
  ledger)
    mkdir -p "$work/ledger"
    rc=0; container -v "$src:/repo:ro,Z" -v "$work/ledger:/out:Z" "$IMAGE" sh -c '/repo/scripts/build-release /repo /out/release >/dev/null' </dev/null >"$work/ledger.stdout" 2>&1 || rc=$?
    check "ledger: the release carries the USART guard's 138 field rows over its 12 registers (FIFOEN, M0, RXINV acknowledged as stock-wrong; IDLEIE, DMAR modeled; BRR's re-init pattern named), bench row U1; the stock model listed as guarded on the eight ports, lpuart1 left out" \
      "[ \$rc = 0 ] && python3 -I -c '
import json, sys
rows = [json.loads(l) for l in open(sys.argv[1]).read().splitlines()[1:]]
g = \"Tviburi.GuardedSTM32F7_USART\"
f = [r for r in rows if r.get(\"peripheral\") == g and r[\"row\"] == \"field\"]
by = {(r[\"register\"], r[\"field\"]): r for r in f}
ok = len(f) == 138 and len({r[\"register\"] for r in f}) == 12
ok = ok and by[(\"USART_CR1\", \"FIFOEN\")][\"state\"] == \"stock-wrong\" and \"acknowledged by name\" in by[(\"USART_CR1\", \"FIFOEN\")][\"detail\"]
ok = ok and by[(\"USART_CR1\", \"IDLEIE\")][\"state\"] == \"guard\" and by[(\"USART_CR3\", \"DMAR\")][\"state\"] == \"guard\"
ok = ok and \"usart_init on an enabled port\" in by[(\"USART_BRR\", \"BRR\")][\"behaves_as\"] and by[(\"USART_CR1\", \"M1\")][\"allowed\"] == [0]
bench = sorted((r[\"field\"], r[\"rule\"]) for r in rows if r.get(\"peripheral\") == g and r[\"row\"] == \"bench\")
ok = ok and bench == [(\"U1\", \"TVI-USART-UE\")]
m = [r for r in rows if r[\"row\"] == \"model\" and r[\"peripheral\"] == \"UART.STM32F7_USART\"]
ok = ok and len(m) == 1 and m[0].get(\"guarded_by\") == g and m[0][\"instances\"] == [\"usart1\", \"usart2\", \"usart3\", \"uart4\", \"uart5\", \"usart6\", \"uart7\", \"uart8\"]
sys.exit(0 if ok else 1)
' '$work/ledger/release/LEDGER.jsonl'" ;;
  registrations)
    run_case registrations - "$clk" 'sysbus LogPeripheralAccess sysbus.usart1 true' 'sysbus WriteDoubleWord 0x4001100C 0x271' 'sysbus ReadDoubleWord 0x40011000' 'peripherals'
    check "registrations: peripherals lists the eight ports as the guard and lpuart1 as stock; the access log names their registers as the stock model's (BaudRate, ControlRegister1; TVI-WO-1305.measure.md §2.3)" \
      "passed registrations && grep -q 'usart1 (GuardedSTM32F7_USART)' '$work/registrations/renode.out' && grep -q 'uart8 (GuardedSTM32F7_USART)' '$work/registrations/renode.out' \
       && grep -q 'lpuart1 (STM32F7_USART)' '$work/registrations/renode.out' && [ \$(grep -c '(GuardedSTM32F7_USART)' '$work/registrations/renode.out') = 8 ] \
       && grep -q 'usart1: WriteUInt32 to 0xC (BaudRate), value 0x271' '$work/registrations/renode-log.txt' && grep -q 'usart1: ReadUInt32 from 0x0 (ControlRegister1), returned 0x0' '$work/registrations/renode-log.txt'" ;;
  recorded)
    PLATFORM_SED='/^tviburiUsart1:$/{n;s/enforcing/recorded/}' run_case recorded - "$clk" 'sysbus WriteDoubleWord 0x40011000 0x2000000D' \
      'sysbus WriteDoubleWord 0x40011000 0x3000000D' 'echo "R-CR1"' 'sysbus ReadDoubleWord 0x40011000'
    check "recorded: usart1 recorded: CR1.M1 set with UE = 1 records two would-refuses (TVI-USART-UE and TVI-USART-FIELD) and passes on (RUL-19); CR1 reads 0x3000000D; never a pass at the check" \
      "[ \$rc = 1 ] && [ \$(grep -c '\"tviburi\":\"would-refuse\".*TVI-USART-' '$work/recorded/renode-log.txt') = 2 ] && [ \"\$(readout recorded CR1)\" = 0x3000000D ]" ;;
  reset)
    run_case reset - "$clk" 'sysbus WriteDoubleWord 0x40011004 0x10040' 'sysbus WriteDoubleWord 0x40011000 0x2000000C' 'echo "R-BEFORE"' 'sysbus ReadDoubleWord 0x40011004' \
      'machine Reset' "$clk" 'echo "R-AFTER"' 'sysbus ReadDoubleWord 0x40011004' 'echo "R-CR1"' 'sysbus ReadDoubleWord 0x40011000'
    check "reset: CR2 served from the copy (0x10040) before; machine Reset clears the copies (CR2 and CR1 read 0)" \
      "passed reset && [ \"\$(readout reset BEFORE)\" = 0x00010040 ] && [ \"\$(readout reset AFTER)\" = 0x00000000 ] && [ \"\$(readout reset CR1)\" = 0x00000000 ]" ;;
  config2)
    PLATFORM_SED='/^tviburiUsart1:$/a\    lowPowerMode: true' run_case config2 - "$clk" 'sysbus ReadDoubleWord 0x40011000'
    check "config2: a USART guard given lowPowerMode (LPUART1's) refuses every access, TVI-USART-CONFIG, unmodeled" \
      "[ \$rc = 3 ] && first config2 'r[\"rule\"] == \"TVI-USART-CONFIG\" and r[\"kind\"] == \"unmodeled\" and \"lowPowerMode\" in r[\"condition\"]'" ;;
  regen)
    rc=0; python3 -I "$src/tests/1305/fields.py" "$src/tests/1305/data/rm0433-rev9-usart-fields.tsv" > "$work/fields.cs" || rc=$?
    mkdir -p "$work/regen"
    container -v "$src:/repo:ro,Z" -v "$work/regen:/out:Z" "$IMAGE" sh -c 'awk -v sha=$(sha256sum /opt/renode/platforms/cpus/stm32h7.repl | cut -c1-64) \
      -f /repo/model/stm32h7-usart-restate.awk /opt/renode/platforms/cpus/stm32h7.repl > /out/restate.repl' </dev/null >"$work/regen.stdout" 2>&1 || rc=$?
    check "regen: the guard's Fields table regenerates from tests/1305/data by tests/1305/fields.py, and the restatement from the pinned stm32h7.repl, byte for byte (RUL-16)" \
      "[ \$rc = 0 ] && sed -n '/^        private static readonly Row\[\] Fields =$/,/^        };$/p' '$src/model/GuardedSTM32F7_USART.cs' | sed '1,2d;\$d' | cmp -s - '$work/fields.cs' && cmp -s '$work/regen/restate.repl' '$src/model/stm32h7-usart-restate.repl'" ;;
  *) echo "unknown case: $t" >&2; exit 2 ;;
  esac
done

echo "$fails failed"
[ $fails = 0 ]
