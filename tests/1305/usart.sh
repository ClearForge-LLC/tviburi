#!/usr/bin/env bash
# tests/1305/usart.sh [SRC] — TVI-WO-1305's acceptance cases for the USART guard, run on the host as tests/1304/flash.sh is:
# each case one container in the pinned tools image (tests/spike.sh's IMAGE), no network, the source read-only. SRC defaults
# to a `git archive HEAD` export. tests/spike.sh runs each case as usart-<case> in its acceptance group.
#   TVIBURI_TESTS="config m1ue ..."  run only these cases.
#   TVIBURI_CI=1                     plain `podman run`, for a disposable CI runner only; elsewhere ~/bin/capped-run.
# Exit 0 only if every selected case passed. Pages: RM0433 Rev 9 §48 (USART); the rulings of 2026-11-24T18:55Z (Q1-Q9) and
# the measurement in docs/work-orders/TVI-WO-1305.measure.md. TVI-WO-1305a's cases (brr0 onward): the rulings at TVI-WO-1305's
# merge (Q2-Q4) and docs/work-orders/TVI-WO-1305a.measure.md.
set -euo pipefail

here=$(cd "$(dirname "$0")/../.." && pwd)
IMAGE=$(sed -n 's/^IMAGE=//p' "$here/tests/spike.sh")
CASES="config m1ue over8ue psue psoff stopue reinit reinitstop disable same ap width tdrbyte read field nine kernel clock rxoff idle idleframe idlere dmarx fields ledger registrations recorded reset config2 regen brr0 brr8 brrheld overrun idlekernel idlenorule idlenone idlehse hzcsi hzsyscsi hzsyshse hzvcol hzwide hzdivr hzpllon hzvcoh hzrge hzpll3q hzport hzcsiref hzcfgon hzbyte"
GUARDS="spi1 rcc gpioPortA gpioPortB gpioPortC gpioPortD gpioPortE gpioPortF gpioPortG gpioPortH gpioPortI gpioPortJ gpioPortK timer1 timer8 dma1 dma2 flashController usart1 usart2 usart3 uart4 uart5 usart6 uart7 uart8 i2c1 i2c2 i2c3 adcM1S2 adc3"
MONITOR="clock rxoff idle idleframe idlere dmarx fields ledger registrations recorded reset config2 regen brrheld overrun idlekernel idlenone idlehse hzcsi hzsyscsi hzsyshse hzvcol hzwide hzport hzpllon hzcsiref hzbyte"
PATHS=$(for c in $CASES; do case " $MONITOR " in *" $c "*) ;; *) printf '"%s",' "$c" ;; esac; done | sed 's/,$//')
PATHS="$PATHS,\"hzcsi\",\"hzsyscsi\",\"hzsyshse\",\"hzvcol\",\"hzwide\",\"hzpllon\",\"hzcsiref\""   # TVI-WO-1300c: firmware run with monitor lines after it
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
logged() { # logged <name> <python expr over `recs`, usart1's records in the log, in order>
  python3 -I -c '
import json, sys
recs = [json.loads(l.split("usart1: ", 1)[1]) for l in open(sys.argv[1]) if "usart1: {\"tviburi\":\"" in l and "\"rule\"" in l]
sys.exit(0 if eval("(" + sys.argv[2] + ")") else 1)
' "$work/$1/renode-log.txt" "$2"
}
idleat() { bit "$(readout "$1" "$2"20)" 4 0 && bit "$(readout "$1" "$2"40)" 4 1; }   # IDLE clear 20 us after a byte, set at 40 (31.25 us)
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
    "refuses psue TVI-USART-UE unmodeled 'r[\"bit\"] == \"PS (bit 9) changed with UE = 1\" and \"p2069\" in r[\"citation\"]'" ;;
  psoff) check "psoff: the same PCE|PS change made with UE = 0, then UE, passes; CR1 reads 0x2000060D" \
    "passes psoff && [ \"\$(readout psoff READ)\" = 0x2000060D ]" ;;
  stopue) check "stopue: CR2.STOP changed with UE = 1 refuses TVI-USART-UE (p2076)" \
    "refuses stopue TVI-USART-UE unmodeled 'r[\"access\"][\"offset\"] == \"0x4\" and r[\"bit\"] == \"STOP (13:12) changed with UE = 1\" and \"p2076\" in r[\"citation\"]'" ;;
  reinit) check "reinit: ChibiOS's usart_init on the enabled port (BRR, CR2.RXINV, CR1.M0|PCE, all with UE = 1) passes, acknowledged by name and taken (ruled Q2 (c)); BRR reads 0x2D0, CR2 0x10000" \
    "passes reinit && [ \"\$(readout reinit READ)\" = 0x000002D0 ] && [ \"\$(readout reinit READ2)\" = 0x00010000 ]" ;;
  reinitstop) check "reinitstop: the same re-init also changing STOP refuses TVI-USART-UE at the CR2 write, naming STOP only (outside the pattern)" \
    "refuses reinitstop TVI-USART-UE unmodeled 'r[\"access\"][\"offset\"] == \"0x4\" and r[\"bit\"] == \"STOP (13:12) changed with UE = 1\"'" ;;
  disable) check "disable: CR1 = 0 from UE = 1 (ChibiOS's sdStop, clearing FIFOEN and the unacknowledged PS in the same write) passes, judged as with UE = 0 (ruled Q2); CR1 reads 0" \
    "passes disable && [ \"\$(readout disable READ)\" = 0x00000000 ]" ;;
  same) check "same: BRR, CR2 (STOP = 10) and CR1 (PCE|PS) written again unchanged with UE = 1 pass (RUL-18): STOP and PS are outside the acknowledged pattern" "passes same" ;;
  ap) check "ap: ArduPilot's USART1 sequence (BRR 0x271, CR2 0x40, CR3 0xC1, CR1 0x2000013D, ICR all ones, CR1's read-modify-write) passes, enforcing; CR1 reads 0x2000011D (FIFOEN and IDLEIE kept, as silicon: the stock model reads 0x10D) and CR2 0x40" \
    "passes ap && [ \"\$(readout ap READ)\" = 0x2000011D ] && [ \"\$(readout ap READ2)\" = 0x00000040 ]" ;;
  width) check "width: a byte write to CR1 refuses TVI-USART-WIDTH, unmodeled (p2067)" \
    "refuses width TVI-USART-WIDTH unmodeled 'r[\"access\"][\"width\"] == 8 and r[\"bit\"] == \"8-bit write\" and \"p2067\" in r[\"citation\"]'" ;;
  tdrbyte) check "tdrbyte: a byte write to TDR passes (ruled Q7: ArduPilot's DMA writes it by bytes) and is sent: ISR.TC, cleared before it, reads 1" \
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
    # IDLE (p2090, p2021; ruled Q5) at 320,000 baud (the kernel clock 64 MHz over BRR 0xC8): a frame of 10 bits is 31.25 us.
    run_case idle - 'cpu IsHalted true' "$clk" 'sysbus WriteDoubleWord 0x4001100C 0xC8' 'sysbus WriteDoubleWord 0x40011000 0x2000001D' \
      'emulation RunFor "0.001"' 'echo "R-START"' 'sysbus ReadDoubleWord 0x4001101C' \
      'sysbus.usart1 WriteChar 0x41' 'emulation RunFor "0.00002"' 'echo "R-EARLY"' 'sysbus ReadDoubleWord 0x4001101C' \
      'sysbus.usart1 WriteChar 0x42' 'emulation RunFor "0.00002"' 'echo "R-RESTART"' 'sysbus ReadDoubleWord 0x4001101C' \
      'emulation RunFor "0.00002"' 'echo "R-IDLE"' 'sysbus ReadDoubleWord 0x4001101C' 'echo "R-IRQ"' 'sysbus.usart1 IRQ' \
      'sysbus WriteDoubleWord 0x40011020 0x10' 'echo "R-CLEARED"' 'sysbus ReadDoubleWord 0x4001101C' 'echo "R-IRQ2"' 'sysbus.usart1 IRQ' \
      'emulation RunFor "0.001"' 'echo "R-AGAIN"' 'sysbus ReadDoubleWord 0x4001101C' \
      'sysbus.usart1 WriteChar 0x43' 'emulation RunFor "0.0001"' 'sysbus WriteDoubleWord 0x40011000 0x2000001C' 'echo "R-OFF"' 'sysbus ReadDoubleWord 0x4001101C' \
      'sysbus WriteDoubleWord 0x40011000 0x2000004D' 'sysbus WriteDoubleWord 0x40011028 0x44' 'echo "R-IRQ3"' 'sysbus.usart1 IRQ'
    check "idle: no IDLE at a start with nothing received (ruled Q5); a byte, then IDLE only after one idle frame (31.25 us), a newer byte restarting it (clear at 20 us after each, set at 40 us after the last), raising the interrupt with IDLEIE; IDLECF clears it and it is not set again with no new byte (p2090); UE cleared resets IDLE and RXFNE (p2070); the stock model's own interrupt (TC with TCIE) passes through the guard's IRQ" \
      "passed idle && bit \"\$(readout idle START)\" 4 0 && bit \"\$(readout idle EARLY)\" 4 0 && bit \"\$(readout idle EARLY)\" 5 1 && bit \"\$(readout idle RESTART)\" 4 0 && bit \"\$(readout idle IDLE)\" 4 1 \
       && [ \"\$(readout idle IRQ)\" = 'GPIO: set' ] && bit \"\$(readout idle CLEARED)\" 4 0 && [ \"\$(readout idle IRQ2)\" = 'GPIO: unset' ] \
       && bit \"\$(readout idle AGAIN)\" 4 0 && bit \"\$(readout idle OFF)\" 4 0 && bit \"\$(readout idle OFF)\" 5 0 && [ \"\$(readout idle IRQ3)\" = 'GPIO: set' ]" ;;
  rxoff)
    # Adversarial B3: a byte arriving while the port's bus clock is off is not received (p470 USART1EN; p2019; p371): no
    # RXFNE, no IDLE, no request. The guard asks the RCC guard's query; the stock model would take the byte. TVI-WO-1300b (2):
    # clearing USART1EN under UE = 1 is now the RCC guard's TVI-RCC-CLOCK, so the RCC runs recorded here (the write passes on,
    # as a would-refuse) and the reception rule behind it is still shown.
    PLATFORM_SED='/^tviburiRcc:$/,/mode:/s/enforcing/recorded/' run_case rxoff - 'cpu IsHalted true' "$clk" 'sysbus WriteDoubleWord 0x4001100C 0x271' 'sysbus WriteDoubleWord 0x40011000 0x2000001D' \
      'sysbus WriteDoubleWord 0x580244F0 0x0' 'sysbus.usart1 WriteChar 0x41' 'emulation RunFor "0.0001"' "$clk" 'echo "R-ISR"' 'sysbus ReadDoubleWord 0x4001101C'
    check "rxoff: a byte arriving with USART1EN cleared under a running port is not received: no RXFNE and no IDLE once the clock is back (adversarial B3); the clear itself is the RCC's TVI-RCC-CLOCK (TVI-WO-1300b (2)), its only record" \
      "[ \$rc = 1 ] && [ \"\$(grep -o '\"tviburi\":\"would-refuse\",\"seq\":[0-9]*,\"rule\":\"[A-Z-]*\"' '$work/rxoff/renode-log.txt' | sed 's/.*rule\":\"//; s/\"//')\" = TVI-RCC-CLOCK ] && bit \"\$(readout rxoff ISR)\" 5 0 && bit \"\$(readout rxoff ISR)\" 4 0" ;;
  idleframe)
    # Adversarial T2: the idle frame by M and STOP (p2068-2069, p2076): M0 = PCE = 1 (9 bits with parity) and STOP = 10 (2):
    # 1 + 9 + 2 = 12 bits at 320,000 baud, 37.5 us, where 8N1 would be 31.25 and either term alone 34.375.
    run_case idleframe - 'cpu IsHalted true' "$clk" 'sysbus WriteDoubleWord 0x4001100C 0xC8' 'sysbus WriteDoubleWord 0x40011004 0x2000' \
      'sysbus WriteDoubleWord 0x40011000 0x2000141D' 'sysbus.usart1 WriteChar 0x41' 'emulation RunFor "0.000036"' 'echo "R-36"' 'sysbus ReadDoubleWord 0x4001101C' \
      'emulation RunFor "0.000003"' 'echo "R-39"' 'sysbus ReadDoubleWord 0x4001101C'
    check "idleframe: with M0 = PCE = 1 and two stop bits the idle frame is 12 bits (37.5 us): no IDLE at 36 us, IDLE at 39 us" \
      "passed idleframe && bit \"\$(readout idleframe 36)\" 4 0 && bit \"\$(readout idleframe 39)\" 4 1" ;;
  idlere)
    # WO §5: a frame that ends without IDLE. RE cleared before the idle frame elapses: the receiver is off, so no idle line is
    # detected (p2070 RE; p2090), and the guard's timer must not set IDLE.
    run_case idlere - 'cpu IsHalted true' "$clk" 'sysbus WriteDoubleWord 0x4001100C 0xC8' 'sysbus WriteDoubleWord 0x40011000 0x2000001D' \
      'sysbus.usart1 WriteChar 0x41' 'emulation RunFor "0.00001"' 'sysbus WriteDoubleWord 0x40011000 0x20000019' \
      'emulation RunFor "0.00001"' 'sysbus WriteDoubleWord 0x40011000 0x2000001D' 'emulation RunFor "0.0001"' 'echo "R-ISR"' 'sysbus ReadDoubleWord 0x4001101C'
    check "idlere: RE cleared 10 us after a byte and set again 10 us later, inside its idle frame (31.25 us): the receiver stopped, so that frame's IDLE never comes (WO §5; adversarial G5)" \
      "passed idlere && bit \"\$(readout idlere ISR)\" 4 0" ;;
  dmarx)
    # The receive request (p2048, p2070; ruled Q6), with the platform's request line: DMA1 stream 0 peripheral-to-memory on
    # request 41 into AXI SRAM. (1) armed first, three bytes; (2) a byte while the stream is disabled, then re-armed (the stock
    # model never moved it, measure.md U11); (3) DMAR set after a byte waits (U12).
    PLATFORM_SED="$rx" run_case dmarx - 'cpu IsHalted true' "$clk" 'sysbus WriteDoubleWord 0x580244D8 0x1' 'sysbus WriteDoubleWord 0x4001100C 0xC8' \
      'sysbus WriteDoubleWord 0x24000000 0xFFFFFFFF' 'sysbus WriteDoubleWord 0x24000100 0xFFFFFFFF' \
      'sysbus WriteDoubleWord 0x40020800 0x29' 'sysbus WriteDoubleWord 0x40020018 0x40011024' 'sysbus WriteDoubleWord 0x4002001C 0x24000000' \
      'sysbus WriteDoubleWord 0x40020014 0x8' 'sysbus WriteDoubleWord 0x40020010 0x400' 'sysbus WriteDoubleWord 0x40020010 0x401' \
      'sysbus WriteDoubleWord 0x40011008 0x40' 'sysbus WriteDoubleWord 0x40011000 0x2000000D' \
      'sysbus.usart1 WriteChar 0x81' 'sysbus.usart1 WriteChar 0x82' 'sysbus.usart1 WriteChar 0x83' \
      'echo "R-NDTR1"' 'sysbus ReadDoubleWord 0x40020014' 'echo "R-MEM1"' 'sysbus ReadDoubleWord 0x24000000' \
      'sysbus WriteDoubleWord 0x40020010 0x400' 'sysbus.usart1 WriteChar 0x91' 'sysbus.usart1 WriteChar 0x92' \
      'sysbus WriteDoubleWord 0x40020008 0x3D' 'sysbus WriteDoubleWord 0x4002001C 0x24000100' 'sysbus WriteDoubleWord 0x40020014 0x8' \
      'sysbus WriteDoubleWord 0x40020010 0x401' 'emulation RunFor "0.00004"' \
      'echo "R-NDTR2"' 'sysbus ReadDoubleWord 0x40020014' 'echo "R-MEM2"' 'sysbus ReadDoubleWord 0x24000100' \
      'sysbus WriteDoubleWord 0x40011008 0x0' 'sysbus.usart1 WriteChar 0xA1' 'sysbus WriteDoubleWord 0x40011008 0x40' \
      'echo "R-NDTR3"' 'sysbus ReadDoubleWord 0x40020014' 'echo "R-MEM3"' 'sysbus ReadDoubleWord 0x24000100'
    check "dmarx: with DMAR = 1 each received byte moves (NDTR 8 -> 5); two bytes received while the stream was disabled both move within a frame of its arming (one request, then one after each RDR read), and one waiting when DMAR is set moves at that write: silicon's level request (p2048), where the stock model moved none (U11, U12)" \
      "passed dmarx && [ \"\$(readout dmarx NDTR1)\" = 0x00000005 ] && [ \"\$(readout dmarx MEM1)\" = 0xFF838281 ] \
       && [ \"\$(readout dmarx NDTR2)\" = 0x00000006 ] && [ \"\$(readout dmarx MEM2)\" = 0xFFFF9291 ] \
       && [ \"\$(readout dmarx NDTR3)\" = 0x00000005 ] && [ \"\$(readout dmarx MEM3)\" = 0xFFA19291 ]" ;;
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
# 394: the generator's '# CASE' lines from this table (counted in the full suite's run at 01eb773: 312 refuse-cases recorded,
# none missing); a change to the table moves it, and this pin with it (ruled 2026-11-24T19:57Z).
sys.exit(0 if not bad and not missing and len(cases) == 394 else 1)
EOF" ;;
  ledger)
    mkdir -p "$work/ledger"
    rc=0; container -v "$src:/repo:ro,Z" -v "$work/ledger:/out:Z" "$IMAGE" sh -c '/repo/scripts/build-release /repo /out/release >/dev/null' </dev/null >"$work/ledger.stdout" 2>&1 || rc=$?
    check "ledger: the release carries the USART guard's 138 field rows over its 12 registers (FIFOEN, M0, RXINV acknowledged as stock-wrong; IDLEIE, DMAR modeled; BRR's re-init pattern and TVI-USART-BRR named, FIFOEN's TVI-USART-OVERRUN), bench rows U1 and U2; the manifest restates the eight ports and not lpuart1; the stock type's model row is split (schema 2, ruled 19:57Z): the guarded row lists the eight under the guard, the unguarded row lpuart1 with no guard" \
      "[ \$rc = 0 ] && python3 -I -c '
import json, sys
rows = [json.loads(l) for l in open(sys.argv[1]).read().splitlines()[1:]]
g = \"Tviburi.GuardedSTM32F7_USART\"
f = [r for r in rows if r.get(\"peripheral\") == g and r[\"row\"] == \"field\"]
by = {(r[\"register\"], r[\"field\"]): r for r in f}
ok = len(f) == 138 and len({r[\"register\"] for r in f}) == 12
ok = ok and by[(\"USART_CR1\", \"FIFOEN\")][\"state\"] == \"stock-wrong\" and \"acknowledged by name\" in by[(\"USART_CR1\", \"FIFOEN\")][\"detail\"]
ok = ok and by[(\"USART_CR1\", \"IDLEIE\")][\"state\"] == \"guard\" and by[(\"USART_CR3\", \"DMAR\")][\"state\"] == \"guard\"
ok = ok and \"TVI-USART-BRR\" in by[(\"USART_BRR\", \"BRR\")][\"behaves_as\"] and \"TVI-USART-OVERRUN\" in by[(\"USART_CR1\", \"FIFOEN\")][\"behaves_as\"]
ok = ok and \"usart_init on an enabled port\" in by[(\"USART_BRR\", \"BRR\")][\"behaves_as\"] and by[(\"USART_CR1\", \"M1\")][\"allowed\"] == [0]
bench = sorted((r[\"field\"], r[\"rule\"]) for r in rows if r.get(\"peripheral\") == g and r[\"row\"] == \"bench\")
ok = ok and bench == [(\"U1\", \"TVI-USART-UE\"), (\"U2\", \"TVI-USART-BRR\")]
m = [r for r in rows if r[\"row\"] == \"model\" and r[\"peripheral\"] == \"UART.STM32F7_USART\"]
eight = [\"uart4\", \"uart5\", \"uart7\", \"uart8\", \"usart1\", \"usart2\", \"usart3\", \"usart6\"]
mg = [x for x in m if x[\"field\"] == \"guarded\"]; mu = [x for x in m if x[\"field\"] == \"unguarded\"]
ok = ok and len(m) == 2 and len(mg) == 1 and mg[0].get(\"guarded_by\") == g and mg[0][\"instances\"] == eight
ok = ok and len(mu) == 1 and \"guarded_by\" not in mu[0] and mu[0][\"instances\"] == [\"lpuart1\"]
ok = ok and json.loads(open(sys.argv[1]).readline())[\"schema\"] == 2
r = sorted(l.split()[2] for l in open(sys.argv[2]) if l.startswith(\"restate UART.STM32F7_USART \"))
ok = ok and r == eight
sys.exit(0 if ok else 1)
' '$work/ledger/release/LEDGER.jsonl' '$work/ledger/release/MANIFEST'" ;;
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
  brr0) check "brr0: ArduPilot's measured start (usart_init with BRR = 0, sdStop, usart_init with BRR = 0, then BRR 0x271 with UE = 1; TVI-WO-1305a.measure.md §1) passes, enforcing, acknowledged by name; the byte written after the rewrite is sent (TC); BRR reads 0x271" \
    "passes brr0 && [ \"\$(readout brr0 READ)\" = 0x00000271 ] && bit \"\$(readout brr0 READ2)\" 6 1" ;;
  brr8) check "brr8: a start with BRR = 8 refuses TVI-USART-BRR, unmodeled, at the CR1 write that sets UE (\"USARTDIV must be greater than or equal to 16\", p2035)" \
    "refuses brr8 TVI-USART-BRR unmodeled 'r[\"access\"][\"offset\"] == \"0x0\" and r[\"access\"][\"value\"] == \"0x2000000D\" and r[\"bit\"] == \"BRR (15:0) = 0x8 (USARTDIV < 16)\" and r[\"condition\"].startswith(\"a start\") and \"p2035\" in r[\"citation\"]'" ;;
  brrheld)
    # Recorded (RUL-19), so that one run shows each way out of the acknowledged start: a start with BRR = 0 and DMAT (not the
    # measured pattern); then the held start, and in it the same BRR again (RUL-18), a TDR write, an RDR read, DMAR set, a byte
    # received, BRR rewritten to 5; then BRR 0xC8 ends it, and a TDR write and an RDR read pass.
    PLATFORM_SED='/^tviburiUsart1:$/{n;s/enforcing/recorded/}' run_case brrheld - 'cpu IsHalted true' "$clk" 'emulation RunFor "0.0001"' \
      'sysbus WriteDoubleWord 0x40011008 0x80' 'sysbus WriteDoubleWord 0x4001100C 0x0' 'sysbus WriteDoubleWord 0x40011000 0x2000000D' \
      'sysbus WriteDoubleWord 0x40011000 0x0' 'sysbus WriteDoubleWord 0x40011008 0x0' 'sysbus WriteDoubleWord 0x40011000 0x2000000D' \
      'sysbus WriteDoubleWord 0x4001100C 0x0' 'sysbus WriteDoubleWord 0x40011028 0x41' 'sysbus ReadDoubleWord 0x40011024' \
      'sysbus WriteDoubleWord 0x40011008 0x40' 'sysbus.usart1 WriteChar 0x42' 'sysbus WriteDoubleWord 0x4001100C 0x5' \
      'sysbus WriteDoubleWord 0x4001100C 0xC8' 'sysbus WriteDoubleWord 0x40011028 0x43' 'echo "R-RDR"' 'sysbus ReadDoubleWord 0x40011024'
    check "brrheld: recorded: six TVI-USART-BRR would-refuses, unmodeled, in order: a BRR = 0 start with DMAT; in the held start a TDR write, an RDR read, DMAR set, a byte received, BRR rewritten to 5 on the running port; none for the same BRR again or after BRR 0xC8 (the byte then reads 0x42); never a pass" \
      "[ \$rc = 1 ] && logged brrheld 'len(recs) == 6 and all(r[\"rule\"] == \"TVI-USART-BRR\" and r[\"kind\"] == \"unmodeled\" and \"p2035\" in r[\"citation\"] for r in recs)
        and all(r[\"bit\"] == \"BRR (15:0) = 0x0 (USARTDIV < 16)\" for r in recs[:5]) and recs[5][\"bit\"] == \"BRR (15:0) = 0x5 (USARTDIV < 16)\"
        and \"with BRR = 0x0 and CR3.DMAR or DMAT set:\" in recs[0][\"condition\"] and recs[1][\"condition\"].startswith(\"a TDR write after a start with BRR = 0, before BRR >= 16 was written: the acknowledged start moves no byte (TVI-WO-1305a.measure.md §1): \")
        and recs[2][\"condition\"].startswith(\"an RDR read after\") and recs[3][\"condition\"].startswith(\"CR3.DMAR or DMAT set after\")
        and recs[4][\"condition\"].startswith(\"a byte received after\") and recs[4][\"access\"][\"value\"] == \"0x42\"
        and recs[5][\"condition\"].startswith(\"BRR rewritten to 0x5 on a running port:\")' && [ \"\$(readout brrheld RDR)\" = 0x00000042 ]" ;;
  overrun)
    # (2): FIFO mode first (two bytes unread, both kept: acknowledged, ruled Q8), then the same traffic with FIFOEN = 0.
    run_case overrun - 'cpu IsHalted true' "$clk" 'sysbus WriteDoubleWord 0x4001100C 0xC8' 'sysbus WriteDoubleWord 0x40011000 0x2000000D' \
      'sysbus.usart1 WriteChar 0x41' 'sysbus.usart1 WriteChar 0x42' 'echo "R-A"' 'sysbus ReadDoubleWord 0x40011024' 'echo "R-B"' 'sysbus ReadDoubleWord 0x40011024' \
      'sysbus WriteDoubleWord 0x40011000 0x0' 'sysbus WriteDoubleWord 0x40011000 0xD' 'sysbus.usart1 WriteChar 0x51' 'sysbus.usart1 WriteChar 0x52' \
      'echo "R-C"' 'sysbus ReadDoubleWord 0x40011024'
    check "overrun: with FIFOEN = 1 two unread bytes pass and both read back (0x41, 0x42); with FIFOEN = 0 the second unread byte refuses TVI-USART-OVERRUN, unmodeled, at its arrival (p2030), and is not received (RDR 0x51)" \
      "[ \$rc = 3 ] && records overrun 'len(recs) == 1 and recs[0][\"rule\"] == \"TVI-USART-OVERRUN\" and recs[0][\"kind\"] == \"unmodeled\" and recs[0][\"access\"][\"value\"] == \"0x52\" and recs[0][\"access\"][\"offset\"] == \"0x24\" and \"p2030\" in recs[0][\"citation\"]' \
       && [ \"\$(readout overrun A)\" = 0x00000041 ] && [ \"\$(readout overrun B)\" = 0x00000042 ] && [ \"\$(readout overrun C)\" = 0x00000051 ]" ;;
  idlekernel)
    # (3): IDLE one 10-bit frame after a byte at the kernel clock's baud, 320,000 (31.25 us) unless named, in clock trees each
    # from reset; the stock frequency (200 MHz, 125 MHz on usart2) would give another time at every BRR here. P: rcc_pclk2 =
    # 64 MHz / D1CPRE 2 / HPRE 2 / D2PPRE2 2 = 8 MHz (D2PPRE1 4, which usart1 must not use), BRR 25. H: hsi_ker_ck = 64 MHz /
    # HSIDIV 2 = 32 MHz (rcc_pclk2 16 MHz, D2PPRE2 2), BRR 100. A: ArduPilot's own PLL2 (TVI-WO-1305a.measure.md §2:
    # PLLCKSELR 0x00408040, DIVM2 8; PLL2DIVR 0x0704022C, DIVN2 44, DIVQ2 4), 8 MHz * 45 / 5 = 72 MHz, exactly the frequency
    # the consumer declares (ruled 2026-11-25T00:00Z: kept as a test), BRR 0x271: 115,200 baud, 86.8 us (64 MHz would give
    # 97.7 us, 200 MHz 31.25); in ArduPilot's order, PLL2 first, then RCC_APB2RSTR.USART1RST pulsed (p443), which resets the
    # port and not what it knows of PLL2; and after a wide PLL2DIVR was written before the machine Reset, which clears it.
    # PLL2RGE = 11 (8-16 MHz) for its 8 MHz reference (pp. 401-402), as ArduPilot sets it: the RCC guard's KernelHz has no rule
    # for a range left at reset (TVI-WO-1300c, ruled 2026-11-26T04:03Z Q3; the case's time is unchanged).
    # (USART16SEL only: ArduPilot's 0x9 also moves usart2's mux, and the RCC guard keeps a mux's state across machine Reset, a
    # row for the RCC guard in this work order's feedback.)
    # U: usart2 on rcc_pclk1 = 64 MHz / D2PPRE1 4 = 16 MHz (USART16SEL = 011 beside it, D2PPRE2 2), BRR 50.
    byte=('sysbus WriteDoubleWord 0x40011000 0x2000001D' 'sysbus.usart1 WriteChar 0x41' 'emulation RunFor "0.00002"')
    run_case idlekernel - 'cpu IsHalted true' "$clk" 'emulation RunFor "0.0001"' \
      'sysbus WriteDoubleWord 0x58024418 0x808' 'sysbus WriteDoubleWord 0x5802441C 0x450' 'sysbus WriteDoubleWord 0x4001100C 0x19' "${byte[@]}" \
      'echo "R-P20"' 'sysbus ReadDoubleWord 0x4001101C' 'emulation RunFor "0.00002"' 'echo "R-P40"' 'sysbus ReadDoubleWord 0x4001101C' \
      'machine Reset' 'cpu IsHalted true' "$clk" \
      'sysbus WriteDoubleWord 0x58024400 0x9' 'sysbus WriteDoubleWord 0x5802441C 0x400' 'sysbus WriteDoubleWord 0x58024454 0x18' 'sysbus WriteDoubleWord 0x4001100C 0x64' "${byte[@]}" \
      'echo "R-H20"' 'sysbus ReadDoubleWord 0x4001101C' 'emulation RunFor "0.00002"' 'echo "R-H40"' 'sysbus ReadDoubleWord 0x4001101C' \
      'sysbus WriteDoubleWord 0x58024438 0x01090367' 'machine Reset' 'cpu IsHalted true' "$clk" \
      'sysbus WriteDoubleWord 0x58024428 0x00408040' 'sysbus WriteDoubleWord 0x5802442C 0x01FF00C0' 'sysbus WriteDoubleWord 0x58024438 0x0704022C' 'sysbus WriteDoubleWord 0x58024400 0x04000001' \
      'sysbus WriteDoubleWord 0x58024454 0x8' 'sysbus WriteDoubleWord 0x58024498 0x10' 'sysbus WriteDoubleWord 0x58024498 0x0' 'sysbus WriteDoubleWord 0x4001100C 0x271' 'sysbus WriteDoubleWord 0x40011000 0x2000001D' 'sysbus.usart1 WriteChar 0x41' \
      'emulation RunFor "0.000085"' 'echo "R-A85"' 'sysbus ReadDoubleWord 0x4001101C' 'emulation RunFor "0.000004"' 'echo "R-A89"' 'sysbus ReadDoubleWord 0x4001101C' \
      'machine Reset' 'cpu IsHalted true' 'sysbus WriteDoubleWord 0x580244E8 0x20000' \
      'sysbus WriteDoubleWord 0x58024454 0x18' 'sysbus WriteDoubleWord 0x5802441C 0x450' 'sysbus WriteDoubleWord 0x4000440C 0x32' 'sysbus WriteDoubleWord 0x40004400 0x2000001D' \
      'sysbus.usart2 WriteChar 0x41' 'emulation RunFor "0.00002"' 'echo "R-U20"' 'sysbus ReadDoubleWord 0x4000441C' 'emulation RunFor "0.00002"' 'echo "R-U40"' 'sysbus ReadDoubleWord 0x4000441C'
    check "idlekernel: IDLE one frame after a byte at the kernel clock's baud, not the stock frequency's: from rcc_pclk2 through D1CPRE, HPRE and D2PPRE2, from hsi_ker_ck through HSIDIV (each clear at 20 us, set at 40 us), from ArduPilot's own PLL2 at exactly 72 MHz after a USART1RST pulse (clear at 85 us, set at 89 us at BRR 0x271), and on usart2 from rcc_pclk1 through D2PPRE1 (TVI-WO-1305a (3))" \
      "passed idlekernel && idleat idlekernel P && idleat idlekernel H && bit \"\$(readout idlekernel A85)\" 4 0 && bit \"\$(readout idlekernel A89)\" 4 1 && idleat idlekernel U" ;;
  idlenorule) check "idlenorule: USART1 started on pll2_q_ck, running, with PLL2's VCO at 6 MHz (outside p407's 192-960 MHz), refuses TVI-USART-IDLE, unmodeled, at the CR1 write: the RCC guard's KernelHz has no rule for its frequency and the guard uses no default (TVI-WO-1305a (3); invariant 1; re-planted from csi_ker_ck, which the union now times, by TVI-WO-1300c Q6)" \
    "refuses idlenorule TVI-USART-IDLE unmodeled 'r[\"access\"][\"value\"] == \"0x2000000D\" and \"USART16SEL = 001 (pll2_q_ck): PLL2 outside the ranges p404 gives\" in r[\"bit\"] and \"p2034\" in r[\"citation\"]'" ;;
  hzcsi|hzsyscsi|hzsyshse|hzvcol|hzwide|hzcsiref)
    # TVI-WO-1300c: the one answer, read as IDLE one frame after a byte at BRR 0x22C (p2034): csi_ker_ck or rcc_pclk2 from the CSI,
    # 4 MHz: 1,390.048 us; rcc_pclk2 from the HSE (hseFrequency 8 MHz): 695.025 us; pll2_q_ck at 72 MHz: 77.223 us; pll2_q_ck from
    # the CSI at 100 MHz: 55.601 us. Clear 1 us before, set 1 us after.
    case $t in hzcsi|hzsyscsi) us=1390 ;; hzsyshse) us=695 ;; hzcsiref) us=56 ;; *) us=77 ;; esac
    run_case "$t" "$t" 'emulation RunFor "0.001"' "${marks[@]}" 'sysbus.usart1 WriteChar 0x41' "emulation RunFor \"$(printf '0.%06d' $((us - 1)))\"" \
      'echo "R-T1"' 'sysbus ReadDoubleWord 0x4001101C' 'emulation RunFor "0.000002"' 'echo "R-T2"' 'sysbus ReadDoubleWord 0x4001101C'
    check "$t: $(case $t in hzcsi) echo 'csi_ker_ck (USART16SEL = 100)';; hzsyscsi) echo 'rcc_pclk2 with sys_ck on the CSI';; hzsyshse) echo 'rcc_pclk2 with sys_ck on the HSE';; hzvcol) echo "pll2_q_ck from ArduPilot's PLL2, VCOL on an 8 MHz reference (acknowledged by name, Q1), its PLLCFGR read back as written (0x01BF0FFD: the RCC guard's copy, (4))";; hzwide) echo 'pll2_q_ck after one PLL2DIVR write the stock model narrowed (silicon holds DIVN2 = 359, Q2)';; hzcsiref) echo 'pll2_q_ck from a CSI reference (PLLSRC = 01), 100 MHz';; esac): the RCC guard's KernelHz gives the frequency, and IDLE is clear $((us - 1)) us after a byte and set at $((us + 1)) (TVI-WO-1300c, the union)" \
      "passes $t && bit \"\$(readout $t T1)\" 4 0 && bit \"\$(readout $t T2)\" 4 1 && { [ $t != hzvcol ] || [ \"\$(readout hzvcol READ2)\" = 0x01BF0FFD ]; }" ;;
  hzvcoh|hzrge|hzdivr|hzpllon|hzpll3q|hzcfgon)
    if [ $t = hzpllon ]; then
      PLATFORM_SED='/^tviburiRcc:$/,/mode:/s/enforcing/recorded/' run_case "$t" "$t" 'emulation RunFor "0.001"' "${marks[@]}"
    fi
    case $t in
      hzvcoh) w='PLL2VCOSEL = 0 (VCOH, 2 to 16 MHz, p346) with ref2_ck at 1000000 Hz' ;;
      hzrge) w='PLL2RGE = 00 (1 to 2 MHz) with ref2_ck at 8000000 Hz' ;;
      hzdivr|hzpllon) w='RCC_PLL2DIVR written after a write the stock model did not keep, or while PLL2 ran' ;;
      hzpll3q) w='USART16SEL = 010 (pll3_q_ck): no rule for its frequency on a USART' ;;
      hzcfgon) w='PLL2RGE or PLL2VCOSEL written while it ran, which silicon does not take (p347' ;;
    esac
    check "$t: the RCC guard's KernelHz has no rule ($w), so USART1's start refuses TVI-USART-IDLE, unmodeled (TVI-WO-1300c Q1, Q2, Q3, Q10)$([ $t = hzpllon ] && echo '; the RCC, recorded, records TVI-RCC-PLL for the change')" \
      "$([ $t = hzpllon ] && echo "grep -q '\"tviburi\":\"would-refuse\".*TVI-RCC-PLL' '$work/hzpllon/renode-log.txt' && [ \$rc = 1 -o \$rc = 3 ] && logged hzpllon" || echo "refuses $t") $([ $t = hzpllon ] && echo "'len(recs) >= 1 and recs[0][\"rule\"] == \"TVI-USART-IDLE\" and \"$w\" in recs[0][\"bit\"]'" || echo "TVI-USART-IDLE unmodeled '\"$w\" in r[\"bit\"]'")" ;;
  hzbyte)
    # TVI-WO-1300c: a byte with no rule for its frame (FrameTime's TVI-USART-IDLE, at the byte; once idlenone's csi_ker_ck, which the
    # union now times): usart1 started on rcc_pclk2, then PLL3 on and USART16SEL = 010 under the running port (a clock on both inputs,
    # so the mux switches, p371, and TVI-RCC-CLOCK has nothing lost), then a byte: pll3_q_ck has no rule on a USART (Q10). Recorded.
    PLATFORM_SED='/^tviburiUsart1:$/{n;s/enforcing/recorded/}' run_case hzbyte - 'cpu IsHalted true' "$clk" \
      'sysbus WriteDoubleWord 0x4001100C 0xC8' 'sysbus WriteDoubleWord 0x40011000 0x2000000D' 'sysbus WriteDoubleWord 0x58024400 0x10000001' \
      'sysbus WriteDoubleWord 0x58024454 0x10' 'sysbus.usart1 WriteChar 0x41' 'emulation RunFor "0.0001"'
    check "hzbyte: a byte after the source moved to pll3_q_ck under the running port: TVI-USART-IDLE, unmodeled, at the byte (RDR, its value), no rule on a USART (TVI-WO-1300c Q10), and nothing else" \
      "[ \$rc = 1 ] && logged hzbyte 'len(recs) == 1 and recs[0][\"rule\"] == \"TVI-USART-IDLE\" and \"(pll3_q_ck): no rule for its frequency on a USART\" in recs[0][\"bit\"] and recs[0][\"access\"][\"offset\"] == \"0x24\" and recs[0][\"access\"][\"value\"] == \"0x41\"'" ;;
  hzport)
    # Q6: a port outside the RCC guard's query (usart6) answers "no rule": recorded, USART6EN (the RCC's would-refuse) and a start with RE.
    PLATFORM_SED='/^tviburiRcc:$/,/mode:/s/enforcing/recorded/; /^tviburiUsart6:$/{n;s/enforcing/recorded/}' run_case hzport - 'cpu IsHalted true' \
      'sysbus WriteDoubleWord 0x580244F0 0x20' 'sysbus WriteDoubleWord 0x4001140C 0x22C' 'sysbus WriteDoubleWord 0x40011400 0x2000000D'
    check "hzport: usart6, outside the RCC guard's query, started with RE in recorded mode: TVI-USART-IDLE, unmodeled, its reason the query's (the query is not widened; TVI-WO-1300c Q6)" \
      "grep -q 'usart6: {\"tviburi\":\"would-refuse\",\"seq\":[0-9]*,\"rule\":\"TVI-USART-IDLE\",\"kind\":\"unmodeled\",.*\"bit\":\"IDLE (bit 4): no kernel clock rule for usart6\"' '$work/hzport/renode-log.txt'" ;;
  idlenone)
    # Recorded: the other ways to no rule, each from reset: (1) a byte arriving after the source moved to csi_ker_ck under the
    # running port (the RCC guard's TVI-RCC-CLOCK does not fire: the CSI is on, so the mux switches with a clock on both
    # inputs, p371); (2) rcc_pclk2 with sys_ck on the CSI; then (6) and (7), before
    # any PLL2 scenario leaves usart1's mux on pll2_q_ck (the RCC guard keeps it across machine Reset: a row); (3) pll2_q_ck from a
    # CSI reference; (4) pll2_q_ck after PLL2DIVR was written with DIVN2 = 359, which the stock model keeps as 103 (p407 has 9
    # bits), and a USART1RST pulse, which must not forget it; (5) pll2_q_ck with DIVN2 = 2 (x3): a 6 MHz VCO, outside p407's
    # 192-960 MHz; (6) rcc_pclk2 from PLL1 with PLL1VCOSEL = 1 (150-420 MHz, p404) and a 500 MHz VCO: the stock model reads
    # VCOSEL as 0 (measure §3), under which 500 MHz would pass; (7) rcc_pclk2 from PLL1 after PLL1DIVR was written with DIVP1 =
    # 2 (/3, "odd division factors are not allowed", p404), a write the stock model ignores.
    # TVI-WO-1300c (the RCC guard's KernelHz, the one rule; ruled 2026-11-26T04:03Z): (1) and (2) have a frequency now (the CSI, 4
    # MHz: the union), and so has (4) (one PLL2DIVR write the stock model narrowed is silicon's value, Q2). (7) is "no rule" by
    # (3)'s DIVP1 rule (p404, Figure 47). The flash guard, which asks the same rule for the AXI clock, refuses at (6) and (7)
    # (enforcing): the twin cannot compute sys_ck there (Q9).
    PLATFORM_SED='/^tviburiUsart1:$/{n;s/enforcing/recorded/}' run_case idlenone - 'cpu IsHalted true' "$clk" 'emulation RunFor "0.0001"' \
      'sysbus WriteDoubleWord 0x4001100C 0xC8' 'sysbus WriteDoubleWord 0x40011000 0x2000000D' 'sysbus WriteDoubleWord 0x58024400 0x81' \
      'sysbus WriteDoubleWord 0x58024454 0x20' 'sysbus.usart1 WriteChar 0x41' \
      'machine Reset' 'cpu IsHalted true' "$clk" 'sysbus WriteDoubleWord 0x58024400 0x81' 'sysbus WriteDoubleWord 0x58024410 0x1' \
      'sysbus WriteDoubleWord 0x4001100C 0xC8' 'sysbus WriteDoubleWord 0x40011000 0x2000000D' \
      'machine Reset' 'cpu IsHalted true' "$clk" 'sysbus WriteDoubleWord 0x5802442C 0x01FF0002' 'sysbus WriteDoubleWord 0x58024430 0x010106F9' \
      'sysbus WriteDoubleWord 0x58024400 0x01000001' 'sysbus WriteDoubleWord 0x58024410 0x3' 'sysbus WriteDoubleWord 0x4001100C 0xC8' 'sysbus WriteDoubleWord 0x40011000 0x2000000D' \
      'machine Reset' 'cpu IsHalted true' "$clk" 'sysbus WriteDoubleWord 0x58024430 0x01010480' \
      'sysbus WriteDoubleWord 0x58024400 0x01000001' 'sysbus WriteDoubleWord 0x58024410 0x3' 'sysbus WriteDoubleWord 0x4001100C 0xC8' 'sysbus WriteDoubleWord 0x40011000 0x2000000D' \
      'machine Reset' 'cpu IsHalted true' "$clk" 'sysbus WriteDoubleWord 0x58024400 0x81' 'sysbus WriteDoubleWord 0x58024428 0x02020201' \
      'sysbus WriteDoubleWord 0x58024400 0x04000081' 'sysbus WriteDoubleWord 0x58024454 0x8' 'sysbus WriteDoubleWord 0x4001100C 0xC8' 'sysbus WriteDoubleWord 0x40011000 0x2000000D' \
      'machine Reset' 'cpu IsHalted true' "$clk" 'sysbus WriteDoubleWord 0x58024438 0x01090367' 'sysbus WriteDoubleWord 0x58024400 0x04000001' \
      'sysbus WriteDoubleWord 0x58024454 0x8' 'sysbus WriteDoubleWord 0x58024498 0x10' 'sysbus WriteDoubleWord 0x58024498 0x0' 'sysbus WriteDoubleWord 0x4001100C 0xE1' 'sysbus WriteDoubleWord 0x40011000 0x2000000D' \
      'machine Reset' 'cpu IsHalted true' "$clk" 'sysbus WriteDoubleWord 0x58024438 0x01010002' 'sysbus WriteDoubleWord 0x58024400 0x04000001' \
      'sysbus WriteDoubleWord 0x58024454 0x8' 'sysbus WriteDoubleWord 0x4001100C 0x10' 'sysbus WriteDoubleWord 0x40011000 0x2000000D'
    check "idlenone: recorded: four TVI-USART-IDLE would-refuses, unmodeled, each the RCC guard's reason: receiver starts on PLL1 with a VCO outside the medium range its VCOSEL chose, on PLL1 after a DIVP1 the stock model ignored (/3: p404, Figure 47), on pll2_q_ck from a CSI reference, and with a VCO outside p407's range; the CSI paths and the one wide PLL2DIVR write have a frequency (TVI-WO-1300c Q2, Q6); and the flash guard's two TVI-FLASH-WAIT, unmodeled, at the two PLL1 paths (Q9); never a pass" \
      "[ \$rc = 3 ] && logged idlenone 'len(recs) == 4 and all(r[\"rule\"] == \"TVI-USART-IDLE\" and r[\"kind\"] == \"unmodeled\" for r in recs)
        and \"(rcc_pclk2): PLL1 outside the ranges p404 gives, or with no reference (ref 2000000 Hz, VCO 500000000 Hz)\" in recs[0][\"bit\"]
        and \"(rcc_pclk2): DIVP1 = 2 (/3)\" in recs[1][\"bit\"] and \"Figure 47 p346\" in recs[1][\"bit\"]
        and \"(pll2_q_ck): PLL2 outside the ranges p404 gives, or with no reference (ref 125000 Hz\" in recs[2][\"bit\"]
        and \"(pll2_q_ck): PLL2 outside the ranges p404 gives, or with no reference (ref 2000000 Hz, VCO 6000000 Hz)\" in recs[3][\"bit\"]' \
       && [ \$(grep -c 'flashController: {\"tviburi\":\"refusal\".*\"rule\":\"TVI-FLASH-WAIT\",\"kind\":\"unmodeled\".*cannot be computed' '$work/idlenone/renode-log.txt') = 2 ]" ;;
  idlehse)
    # (3), ruled 2026-11-25T01:12Z: rcc_pclk2 from sys_ck = PLL1's P output on the HSE, as hse-crystal-16's generated firmware
    # sets it up (HSEON, HSECSSON, PLLCKSELR 0x52: PLLSRC HSE, DIVM1 5; PLLCFGR 0x30004; PLL1DIVR 0x0103064F: x80, /4; PLL1ON;
    # SW = PLL1), BRR 0xC8. D: the board declares hseFrequency 16 MHz (the generated board's): 3.2 MHz * 80 / 4 = 64 MHz, 320,000
    # baud, IDLE 31.25 us after a byte. N: the board declares no crystal: the first record is the RCC guard's TVI-RCC-HSE,
    # unmodeled, at the write that starts the HSE, and no frequency is taken (TVI-WO-1300b (4)).
    hse=('cpu IsHalted true' "$clk" 'sysbus WriteDoubleWord 0x58024400 0x00010001' 'sysbus WriteDoubleWord 0x58024400 0x00090001'
      'sysbus WriteDoubleWord 0x58024428 0x52' 'sysbus WriteDoubleWord 0x5802442C 0x30004' 'sysbus WriteDoubleWord 0x58024430 0x0103064F'
      'sysbus WriteDoubleWord 0x58024400 0x01090001' 'sysbus WriteDoubleWord 0x58024410 0x3' 'sysbus WriteDoubleWord 0x4001100C 0xC8'
      'sysbus WriteDoubleWord 0x40011000 0x2000001D' 'sysbus.usart1 WriteChar 0x41')
    PLATFORM_SED='s/^    hseFrequency: 8000000$/    hseFrequency: 16000000/' run_case idlehse - "${hse[@]}" \
      'emulation RunFor "0.00002"' 'echo "R-D20"' 'sysbus ReadDoubleWord 0x4001101C' 'emulation RunFor "0.00002"' 'echo "R-D40"' 'sysbus ReadDoubleWord 0x4001101C'
    d=$rc; mv "$work/idlehse" "$work/idlehse-d"; mv "$work/idlehse.stdout" "$work/idlehse-d.stdout"
    PLATFORM_SED='/^    hseFrequency: 8000000$/d' run_case idlehse - "${hse[@]}"
    check "idlehse: rcc_pclk2 from PLL1's P output on the HSE (the generated hse-crystal-16 sequence): with hseFrequency 16 MHz declared, 64 MHz (IDLE clear at 20 us, set at 40); with none declared, no HSE frequency: the HSE's start refuses at the RCC guard, TVI-RCC-HSE, unmodeled (TVI-WO-1300b (4); no 8 MHz default)" \
      "[ \$d = 0 ] && grep -qx 'CHECK PASS' '$work/idlehse-d.stdout' && grep -qx '    hseFrequency: 16000000' '$work/idlehse-d/platform.repl' && ! grep -q '^    hseFrequency' '$work/idlehse/platform.repl' \
       && bit \"\$(readout idlehse-d D20)\" 4 0 && bit \"\$(readout idlehse-d D40)\" 4 1 \
       && [ \$rc = 3 ] && first idlehse 'r[\"rule\"] == \"TVI-RCC-HSE\" and r[\"kind\"] == \"unmodeled\" and r[\"peripheral\"] == \"sysbus.rcc\" and r[\"bit\"] == \"RCC_CR.HSEON (bit 16) = 1\"'" ;;
  *) echo "unknown case: $t" >&2; exit 2 ;;
  esac
done

echo "$fails failed"
[ $fails = 0 ]
