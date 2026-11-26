#!/usr/bin/env bash
# tests/1306/i2c.sh [SRC] — TVI-WO-1306's acceptance cases for the I2C guard, run on the host as tests/1305/usart.sh is: each
# case one container in the pinned tools image (tests/spike.sh's IMAGE), no network, the source read-only. SRC defaults to a
# `git archive HEAD` export. tests/spike.sh runs each case as i2c-<case> in its acceptance group.
#   TVIBURI_TESTS="config timingrpe ..."  run only these cases.
#   TVIBURI_CI=1                          plain `podman run`, for a disposable CI runner only; elsewhere ~/bin/capped-run.
# Exit 0 only if every selected case passed. Pages: RM0433 Rev 9 §47 (I2C); the rulings of 2026-11-25T05:06Z (Q1-Q12) and the
# measurement in docs/work-orders/TVI-WO-1306.measure.md; the NACK's cases (nack*) TVI-WO-1306a's, ruled 2026-11-26T00:22Z
# (docs/work-orders/TVI-WO-1306a.measure.md). Each firmware case puts a BME280 at 0x76 on i2c1 (the platform's
# sensor, declared on the guard's variable: measure.md §2.4).
set -euo pipefail

here=$(cd "$(dirname "$0")/../.." && pwd)
IMAGE=$(sed -n 's/^IMAGE=//p' "$here/tests/spike.sh")
CASES="config timingrpe timingrsame timingroff dnfpe anfoff clearfield ap absentnext absent icrres field width read kernel reset resetok startoff txdroff empty midstart midcr2 txdrread stopidle startstop stalestop blindclear softreset clock registrations recorded rccreset unheld clocklost absentname fields ledger regen nack nacktime nackpll nackirq nackstop nackpe nacktransfer nacktxdr nackclock isrmon"
GUARDS="spi1 rcc gpioPortA gpioPortB gpioPortC gpioPortD gpioPortE gpioPortF gpioPortG gpioPortH gpioPortI gpioPortJ gpioPortK timer1 timer8 dma1 dma2 flashController usart1 usart2 usart3 uart4 uart5 usart6 uart7 uart8 i2c1 i2c2 i2c3 adcM1S2 adc3"
MONITOR="clock registrations recorded rccreset unheld clocklost absentname fields ledger regen nacktime nackpll isrmon"
CUSTOM="nackirq"   # a firmware case whose monitor lines are its own (TVI-WO-1306a)
PATHS=$(for c in $CASES; do case " $MONITOR " in *" $c "*) ;; *) printf '"%s",' "$c" ;; esac; done | sed 's/,$//')
scratch=${TVIBURI_SCRATCH:-$HOME/.cache/tviburi}
mkdir -p "$scratch"
work=$(mktemp -d "$scratch/i2c.XXXXXX")

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
# then the check on the log, naming every guarded source. Outputs in $work/<name>. The board is main's with a BME280 at 0x76 on
# the guarded i2c1; PLATFORM_SED, a sed script applied to it; PLATFORM, another platform file instead.
run_case() { # run_case <name> <firmware path or -> <monitor lines...>
  local name=$1 fw=$2; shift 2
  mkdir -p "$work/$name"
  if [ -n "${PLATFORM:-}" ]; then
    sed 's#^using "\./#using "/repo/model/#' "$src/${PLATFORM#/repo/}" > "$work/$name/platform.repl"
  else
    { sed -e 's#^using "\./#using "/repo/model/#' "$src/model/tviburi-nucleo-h753zi.repl"; echo; echo 'bme: I2C.BME280 @ tviburiI2c1 0x76'; } \
      | sed -e "${PLATFORM_SED:-}" > "$work/$name/platform.repl"
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
        --check-cfg "cfg(path, values($3))" -C link-arg=-T/repo/firmware/link.x -o /out/fw.elf /repo/firmware/i2c.rs || exit 91
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
refuses() { [ "$rc" = 3 ] && first "$1" "r[\"rule\"] == \"$2\" and r[\"kind\"] == \"$3\" and r[\"peripheral\"] == \"sysbus.i2c1\" and r[\"access\"][\"pc\"] != \"none\" and $4"; }
nackat() { # nackat <name> <address phase, us> <SCL period, us> <setup monitor lines...>: TVI-WO-1306a's timing, monitor-driven (a RunFor
  # is exact; the log's stamps are not). After the setup's last line, a write START to an absent address, ISR is read 0.5 us either
  # side of NACKF (nine SCL periods, p1955, p1971) and of STOPF (one period more, inferred, ruled Q3), then after ICR = 0x30.
  local name=$1 a=$2 t=$3; shift 3
  local us; mapfile -t us < <(python3 -I -c 'import sys; a, t = map(float, sys.argv[1:]); print("\n".join("%.10f" % (x / 1e6) for x in (a - 0.5, 1, t - 1, 1)))' "$a" "$t")
  run_case "$name" - "$clk" "$@" 'echo "R-T0"' 'sysbus ReadDoubleWord 0x40005418' \
    "emulation RunFor \"${us[0]}\"" 'echo "R-T1"' 'sysbus ReadDoubleWord 0x40005418' "emulation RunFor \"${us[1]}\"" 'echo "R-T2"' 'sysbus ReadDoubleWord 0x40005418' \
    "emulation RunFor \"${us[2]}\"" 'echo "R-T3"' 'sysbus ReadDoubleWord 0x40005418' "emulation RunFor \"${us[3]}\"" 'echo "R-T4"' 'sysbus ReadDoubleWord 0x40005418' \
    'sysbus WriteDoubleWord 0x4000541C 0x30' 'echo "R-T5"' 'sysbus ReadDoubleWord 0x40005418'
  [ "$(for k in 0 1 2 3 4 5; do readout "$name" "T$k"; done | paste -sd' ')" = "0x00000001 0x00000001 0x00000011 0x00000011 0x00000031 0x00000001" ]
}
logged() { # logged <name> <python expr over `recs`, the records under <source> in the log, in order> [source]
  python3 -I -c '
import json, sys
s = sys.argv[3] + ": {\"tviburi\":\""
recs = [json.loads(l.split(sys.argv[3] + ": ", 1)[1]) for l in open(sys.argv[1]) if s in l and "\"rule\"" in l]
sys.exit(0 if eval("(" + sys.argv[2] + ")") else 1)
' "$work/$1/renode-log.txt" "$2" "${3:-i2c1}"
}
marks=('echo "TVIBURI-MARK-AFTER"' 'sysbus ReadDoubleWord 0x20000104' 'echo "R-READ"' 'sysbus ReadDoubleWord 0x20000108' 'echo "R-READ2"' 'sysbus ReadDoubleWord 0x2000010C'
  'echo "R-READ3"' 'sysbus ReadDoubleWord 0x20000110' 'echo "R-READ4"' 'sysbus ReadDoubleWord 0x20000114')
clk='sysbus WriteDoubleWord 0x580244E8 0x200000'   # I2C1EN (p463): an unclocked port is locked on the bus (TVI-WO-1300a)
cfg=("$clk" 'sysbus WriteDoubleWord 0x40005410 0x10C7123C' 'sysbus WriteDoubleWord 0x40005400 0x1')   # config, from the monitor
rec='/^tviburiI2c1:$/{n;s/enforcing/recorded/}'

for t in ${TVIBURI_TESTS:-$CASES}; do
  case " $MONITOR $CUSTOM " in *" $t "*) mon=1 ;; *) mon=0 ;; esac
  if [ $mon = 0 ]; then
    run_case "$t" "$t" 'emulation RunFor "0.001"' "${marks[@]}"
  fi
  case $t in
  config) check "config: the generated firmware's sequence (CR1 = 0, TIMINGR, CR1 = PE) passes, its kernel clock running, and the BME280 answers its chip id (0x60) twice, the second START after STOPF was cleared (the first transfer ended, not open); TIMINGR reads 0x10C7123C from the guard's copy (the stock model reads 0, measure.md §2.3 I2)" \
    "passes config && [ \"\$(readout config READ)\" = 0x00000060 ] && [ \"\$(readout config READ2)\" = 0x10C7123C ]" ;;
  timingrpe) check "timingrpe: TIMINGR changed with PE = 1 refuses TVI-I2C-PE, unmodeled, naming the port, the field and p2008 (ruled Q2)" \
    "refuses timingrpe TVI-I2C-PE unmodeled 'r[\"access\"][\"offset\"] == \"0x10\" and r[\"register\"] == \"I2C_TIMINGR\" and r[\"bit\"] == \"SCLL (7:0) changed with PE = 1\" and \"p2008\" in r[\"citation\"] and \"0x3C -> 0x3D\" in r[\"condition\"]'" ;;
  timingrsame) check "timingrsame: TIMINGR written with its own value with PE = 1 passes (RUL-18)" "passes timingrsame" ;;
  timingroff) check "timingroff: PE cleared, checked, TIMINGR changed, PE set: passes; TIMINGR reads back 0x00300F38 as written" \
    "passes timingroff && [ \"\$(readout timingroff READ)\" = 0x00300F38 ]" ;;
  dnfpe) check "dnfpe: CR1.DNF changed with PE = 1 refuses TVI-I2C-PE, unmodeled (p2003)" \
    "refuses dnfpe TVI-I2C-PE unmodeled 'r[\"access\"][\"offset\"] == \"0x0\" and r[\"bit\"] == \"DNF (11:8) changed with PE = 1\" and \"p2003\" in r[\"citation\"]'" ;;
  anfoff) check "anfoff: CR1 with ANFOFF and DNF = 1111 written with PE = 0, then PE: passes; CR1 reads 0x1F01 (the stock model drops both tags)" \
    "passes anfoff && [ \"\$(readout anfoff READ)\" = 0x00001F01 ]" ;;
  clearfield) check "clearfield: CR1 written from PE = 1 with PE = 0 and DNF changed in one write passes, judged as with PE = 0 (ruled Q2)" "passes clearfield" ;;
  ap) check "ap: ArduPilot's I2C1 sequence (CR1 0x90, TIMINGR 0x00300F38, CR1 0x91; transfers with software STOP; ICR 0x40, 0x4, 0x2, its reserved bits (ruled Q10); a probe of 0x30, absent, NACKed, its software STOP absorbed while the NACK's STOP is pending (TVI-WO-1306a Q4); a stop and a start) passes, enforcing; RXDR 0x60, TIMINGR 0x00300F38" \
    "passes ap && [ \"\$(readout ap READ)\" = 0x00000060 ] && [ \"\$(readout ap READ2)\" = 0x00300F38 ] && grep -q 'i2c1: Unknown slave at address 48\\.' '$work/ap/renode-log.txt'" ;;
  absentnext) check "absentnext: the generated firmware's probe of 0x48 (no device: NACKed, ISR polled to STOPF, ICR = 0x30), then the chip id: passes and reads 0x60" \
    "passes absentnext && [ \"\$(readout absentnext READ)\" = 0x00000060 ]" ;;
  absent) check "absent: a read START from 0x31, where the platform has no device, refuses TVI-I2C-ABSENT, unmodeled: RM0433 states the automatic STOP only for a write (p1975; TVI-WO-1306a Q6)" \
    "refuses absent TVI-I2C-ABSENT unmodeled 'r[\"access\"][\"offset\"] == \"0x4\" and \"read from 0x31\" in r[\"condition\"] and \"p1975\" in r[\"citation\"]'" ;;
  icrres) check "icrres: ICR bit 7 (reserved, not ChibiOS's 1, 2 or 6) refuses TVI-I2C-FIELD, unmodeled (p2012)" \
    "refuses icrres TVI-I2C-FIELD unmodeled 'r[\"bit\"] == \"Res7 (bit 7) = 1\" and \"p2012\" in r[\"citation\"]'" ;;
  field) check "field: CR1.TXDMAEN = 1 refuses TVI-I2C-FIELD, unmodeled (the allow-list, ruled Q11)" \
    "refuses field TVI-I2C-FIELD unmodeled 'r[\"bit\"] == \"TXDMAEN (bit 14) = 1\" and \"p2002\" in r[\"citation\"]'" ;;
  width) check "width: a byte write to CR1 refuses TVI-I2C-WIDTH, unmodeled (p2001)" \
    "refuses width TVI-I2C-WIDTH unmodeled 'r[\"access\"][\"width\"] == 8 and r[\"bit\"] == \"8-bit write\" and \"p2001\" in r[\"citation\"]'" ;;
  read) check "read: a read at 0x2C refuses TVI-I2C-READ, unmodeled (p2015)" \
    "refuses read TVI-I2C-READ unmodeled 'r[\"bit\"] == \"read of offset 0x2C\" and \"p2015\" in r[\"citation\"]'" ;;
  kernel) check "kernel: I2C1 started with I2C123SEL = 01 and PLL3 off refuses TVI-I2C-KERNEL, predicted-fault, at the CR1 write that sets PE, through the RCC guard's query (p1954, p371, p416)" \
    "refuses kernel TVI-I2C-KERNEL predicted-fault 'r[\"access\"][\"offset\"] == \"0x0\" and r[\"access\"][\"value\"] == \"0x1\" and r[\"bit\"] == \"RCC_D2CCIP2R.I2C123SEL = 01\" and \"p1954\" in r[\"citation\"] and \"p416\" in r[\"citation\"]'" ;;
  reset) check "reset: PE set as the very next access after the one that cleared it refuses TVI-I2C-RESET, unmodeled (p1960; ruled Q6)" \
    "refuses reset TVI-I2C-RESET unmodeled 'r[\"access\"][\"value\"] == \"0x1\" and \"p1960\" in r[\"citation\"]'" ;;
  resetok) check "resetok: PE cleared, checked by a read, then set: passes (p1960's sequence)" "passes resetok" ;;
  startoff) check "startoff: a START with PE = 0 refuses TVI-I2C-TRANSFER, unmodeled (p2003; the stock model would run the transfer, measure.md I5)" \
    "refuses startoff TVI-I2C-TRANSFER unmodeled 'r[\"access\"][\"offset\"] == \"0x4\" and \"START with PE = 0\" in r[\"condition\"] and \"p2003\" in r[\"citation\"]'" ;;
  txdroff) check "txdroff: a TXDR write with PE = 0 refuses TVI-I2C-TRANSFER, unmodeled" \
    "refuses txdroff TVI-I2C-TRANSFER unmodeled 'r[\"access\"][\"offset\"] == \"0x28\" and \"TXDR written with PE = 0\" in r[\"condition\"]'" ;;
  empty) check "empty: a START with NBYTES = 0 and RELOAD = 0 refuses TVI-I2C-TRANSFER, unmodeled, and Renode does not abort (the stock model, reached, does: measure.md I8b; ruled Q8)" \
    "refuses empty TVI-I2C-TRANSFER unmodeled 'r[\"access\"][\"offset\"] == \"0x4\" and \"NBYTES = 0 and RELOAD = 0\" in r[\"condition\"]' && grep -q 'renode-exit=0' '$work/empty.stdout' && ! grep -q 'Sequence contains no elements' '$work/empty/renode.out'" ;;
  midstart) check "midstart: a START while a write of 2 has sent 1 byte refuses TVI-I2C-TRANSFER, unmodeled (ruled Q8)" \
    "refuses midstart TVI-I2C-TRANSFER unmodeled 'r[\"access\"][\"value\"] == \"0x20124EC\" and \"START while a transfer is open\" in r[\"condition\"]'" ;;
  midcr2) check "midcr2: CR2.NBYTES rewritten while a transfer is open refuses TVI-I2C-TRANSFER, unmodeled (p1973)" \
    "refuses midcr2 TVI-I2C-TRANSFER unmodeled 'r[\"access\"][\"value\"] == \"0x300EC\" and \"changed while a transfer is open\" in r[\"condition\"] and \"p1973\" in r[\"citation\"]'" ;;
  txdrread) check "txdrread: a TXDR write in a read transfer refuses TVI-I2C-TRANSFER, unmodeled (p2014; the stock model would send the byte to the device; adversarial F1)" \
    "refuses txdrread TVI-I2C-TRANSFER unmodeled 'r[\"access\"][\"offset\"] == \"0x28\" and \"outside a write transfer\" in r[\"condition\"] and \"p2014\" in r[\"citation\"]'" ;;
  stopidle) check "stopidle: STOP with no transfer refuses TVI-I2C-TRANSFER, unmodeled (p2005: controller mode only; the stock model would set STOPF; adversarial F2)" \
    "refuses stopidle TVI-I2C-TRANSFER unmodeled 'r[\"access\"][\"value\"] == \"0x4000\" and \"STOP with no transfer\" in r[\"condition\"] and \"p2005\" in r[\"citation\"]'" ;;
  startstop) check "startstop: START and STOP in one CR2 write refuses TVI-I2C-TRANSFER, unmodeled (the stock model ignores both; adversarial F3)" \
    "refuses startstop TVI-I2C-TRANSFER unmodeled 'r[\"access\"][\"value\"] == \"0x160EC\" and \"START and STOP in one write\" in r[\"condition\"]'" ;;
  stalestop) check "stalestop: a STOPF left set from an earlier transfer does not end the next one: a START while that write of 2 has sent 1 byte refuses TVI-I2C-TRANSFER at the START, unmodeled (adversarial F2)" \
    "refuses stalestop TVI-I2C-TRANSFER unmodeled 'r[\"access\"][\"offset\"] == \"0x4\" and r[\"access\"][\"value\"] == \"0x124EC\" and \"START while a transfer is open\" in r[\"condition\"]'" ;;
  blindclear) check "blindclear: a read of 1 with AUTOEND ends at its RXDR read (STOPF), and ICR clears STOPF with no ISR read before it; the next START passes (the transfer's end was seen at the clearing write), and the chip id reads 0x60" \
    "passes blindclear && [ \"\$(readout blindclear READ)\" = 0x00000060 ]" ;;
  softreset) check "softreset: PE cleared with a received byte unread passes; ISR then reads 0x1 (RXNE reset, TXE set, no TC: p1959, p2011; the stock model alone keeps RXNE, measure.md I4) and CR2 keeps 0x204EC (configuration, p1959)" \
    "passes softreset && [ \"\$(readout softreset READ)\" = 0x00000001 ] && [ \"\$(readout softreset READ2)\" = 0x000204EC ]" ;;
  clock)
    # With I2C1EN = 0 the RCC guard locks the port on the bus: the write is dropped there with no record (p373; ruled Q7).
    run_case clock - 'sysbus ReadDoubleWord 0x40005400' 'sysbus WriteDoubleWord 0x40005400 0x0' "$clk" 'echo "R-CR1"' 'sysbus ReadDoubleWord 0x40005400'
    check "clock: CR1 read and written with I2C1EN = 0 (ArduPilot's pre-enable pair) are dropped by the bus lock with no record (p373; ruled Q7), and the run passes" \
      "passed clock && grep -q 'Tried to write a locked peripheral: machine-0.i2c1. Address 0x40005400' '$work/clock/renode.out' && [ \"\$(readout clock CR1)\" = 0x00000000 ]" ;;
  registrations)
    run_case registrations - "$clk" 'sysbus LogPeripheralAccess sysbus.i2c1 true' 'sysbus WriteDoubleWord 0x40005410 0x10C7123C' 'sysbus ReadDoubleWord 0x40005400' 'peripherals'
    check "registrations: peripherals lists i2c1-i2c3 as the guard, the BME280 under i2c1, and i2c4 as stock; the access log names the registers as the stock model's (Timing, Control1; measure.md §2.4)" \
      "passed registrations && grep -q 'i2c1 (GuardedSTM32F7_I2C)' '$work/registrations/renode.out' && grep -q 'i2c3 (GuardedSTM32F7_I2C)' '$work/registrations/renode.out' \
       && grep -q 'i2c4 (STM32F7_I2C)' '$work/registrations/renode.out' && grep -q 'bme (BME280)' '$work/registrations/renode.out' && [ \$(grep -c '(GuardedSTM32F7_I2C)' '$work/registrations/renode.out') = 3 ] \
       && grep -q 'i2c1: WriteUInt32 to 0x10 (Timing), value 0x10C7123C' '$work/registrations/renode-log.txt' && grep -q 'i2c1: ReadUInt32 from 0x0 (Control1), returned 0x0' '$work/registrations/renode-log.txt'" ;;
  recorded)
    PLATFORM_SED=$rec run_case recorded - "${cfg[@]}" 'sysbus WriteDoubleWord 0x40005410 0x10C7123D' 'sysbus WriteDoubleWord 0x40005404 0x020020EC' \
      'sysbus WriteDoubleWord 0x40005400 0x2001' 'echo "R-TIM"' 'sysbus ReadDoubleWord 0x40005410' 'echo "R-CR1"' 'sysbus ReadDoubleWord 0x40005400' \
      'sysbus WriteDoubleWord 0x40005400 0x0' 'sysbus WriteDoubleWord 0x40005404 0x020020EC'
    check "recorded: i2c1 recorded: TIMINGR changed with PE = 1 records TVI-I2C-PE and passes on (RUL-19), reading 0x10C7123D; an empty START records TVI-I2C-TRANSFER and is passed with START cleared, so Renode does not abort; CR1's reserved bit 13 records TVI-I2C-FIELD and reads back 0 (CR1 0x1: a read that does not lie, WO §5); an empty START with PE = 0 records TVI-I2C-TRANSFER and is not passed on either (adversarial F4); never a pass at the check" \
      "[ \$rc = 1 ] && logged recorded 'len(recs) == 4 and [r[\"rule\"] for r in recs] == [\"TVI-I2C-PE\", \"TVI-I2C-TRANSFER\", \"TVI-I2C-FIELD\", \"TVI-I2C-TRANSFER\"] and all(r[\"tviburi\"] == \"would-refuse\" for r in recs)' \
       && [ \"\$(readout recorded TIM)\" = 0x10C7123D ] && [ \"\$(readout recorded CR1)\" = 0x00000001 ] && grep -q 'renode-exit=0' '$work/recorded.stdout'" ;;
  rccreset)
    # ArduPilot pulses I2C1RST (RCC_APB1LRSTR bit 21, §8.7.33 p438) before every start (measure.md §2.6): the guard's copies reset.
    run_case rccreset - "$clk" 'sysbus WriteDoubleWord 0x40005400 0x1F00' 'sysbus WriteDoubleWord 0x40005410 0x10C7123C' 'sysbus WriteDoubleWord 0x40005400 0x1F01' \
      'sysbus WriteDoubleWord 0x40005404 0x000220EC' 'sysbus WriteDoubleWord 0x40005428 0xD0' 'echo "R-BEFORE"' 'sysbus ReadDoubleWord 0x40005410' 'echo "R-ISR0"' 'sysbus ReadDoubleWord 0x40005418' \
      'sysbus WriteDoubleWord 0x58024490 0x200000' 'sysbus WriteDoubleWord 0x58024490 0x0' 'echo "R-AFTER"' 'sysbus ReadDoubleWord 0x40005410' 'echo "R-CR1"' 'sysbus ReadDoubleWord 0x40005400' \
      'echo "R-ISR"' 'sysbus ReadDoubleWord 0x40005418'
    check "rccreset: TIMINGR and CR1 served from the copies before; an I2C1RST pulse in the middle of a write transfer (TXIS set, ISR 0x3) resets them (both read 0) and ISR to 0x1 (p2009; the stock Reset alone leaves TXIS: adversarial F5), as silicon's registers reset" \
      "passed rccreset && [ \"\$(readout rccreset BEFORE)\" = 0x10C7123C ] && [ \"\$(readout rccreset ISR0)\" = 0x00000003 ] && [ \"\$(readout rccreset AFTER)\" = 0x00000000 ] && [ \"\$(readout rccreset CR1)\" = 0x00000000 ] && [ \"\$(readout rccreset ISR)\" = 0x00000001 ]" ;;
  unheld)
    PLATFORM=/repo/tests/1306/unheld.repl run_case unheld - "$clk" 'sysbus WriteDoubleWord 0x40005410 0x10C7123C'
    check "unheld (ruled Q4): an I2C guard not registered on the RCC guard refuses its access, TVI-I2C-CONFIG, unmodeled: unwatched, it cannot pass" \
      "[ \$rc = 3 ] && records unheld 'len(recs) == 1 and recs[0][\"rule\"] == \"TVI-I2C-CONFIG\" and recs[0][\"kind\"] == \"unmodeled\" and recs[0][\"peripheral\"] == \"sysbus.i2c1\" and \"not registered on an RCC guard\" in recs[0][\"condition\"]'" ;;
  clocklost)
    # Ruled Q4: the RCC guard watches a running I2C port (PE = 1) through IRccWatched. Its kernel source switched to pll3_r_ck
    # with PLL3 off stops its clock: TVI-RCC-CLOCK, under rcc, naming PE and p1954.
    run_case clocklost - "${cfg[@]}" 'sysbus WriteDoubleWord 0x58024454 0x1000'
    check "clocklost: I2C123SEL switched to a stopped source under a running I2C1 (PE = 1) refuses TVI-RCC-CLOCK, predicted-fault, at the RCC write, naming PE and p1954 (ruled Q4)" \
      "[ \$rc = 3 ] && records clocklost 'len(recs) == 1 and recs[0][\"rule\"] == \"TVI-RCC-CLOCK\" and recs[0][\"peripheral\"] == \"sysbus.rcc\" and \"i2c1 is enabled (PE = 1)\" in recs[0][\"condition\"] and \"p1954\" in recs[0][\"citation\"]'" ;;
  absentname)
    # Ruled Q9: the generated firmware's TMP117 probe at 0x48 (no Renode model) passes, and the guard writes the stock model's
    # line under its own name, which Aethelgard's absent_address reads by source (the owned instance's has none).
    run_case absentname - "${cfg[@]}" 'sysbus WriteDoubleWord 0x40005404 0x00012090' 'echo "R-ISR"' 'sysbus ReadDoubleWord 0x40005418'
    check "absentname: a START to 0x48 (measured: the TMP117 has no model) passes, NACKed; the log has '[WARNING] i2c1: Unknown slave at address 72.' under the guard's name; ISR read at the START reads 0x1 (NACKF only at the end of the address phase)" \
      "passed absentname && grep -q '\\[WARNING\\] i2c1: Unknown slave at address 72\\.\$' '$work/absentname/renode-log.txt' && [ \"\$(readout absentname ISR)\" = 0x00000001 ]" ;;
  fields)
    # The allow-list (RUL-17) by enumeration, recorded: every field the table allows only at 0, given its widest value with
    # PE = 0 (and no START; TXDR's in a write transfer), records one TVI-I2C-FIELD naming that field; each write is undone by its register's 0.
    mapfile -t lines < <(python3 -I - "$src/tests/1306/data/rm0433-rev9-i2c-fields.tsv" "$src/tests/1306/fields.py" <<'EOF'
import subprocess, sys, re
rows = subprocess.run([sys.executable, '-I', sys.argv[2], sys.argv[1]], capture_output=True, text=True, check=True).stdout.splitlines()
for l in rows:
    m = re.match(r'\s*R\("(I2C_\w+)", (0x[0-9A-F]+), "[^"]*", "(\w+)", (\d+), (\d+), Zero,', l)
    if not m or m.group(1) in ('I2C_ISR', 'I2C_RXDR', 'I2C_PECR'):
        continue
    off, msb, lsb = int(m.group(2), 16), int(m.group(4)), int(m.group(5))
    v = ((1 << (msb - lsb + 1)) - 1) << lsb
    if m.group(1) == 'I2C_TXDR':
        print('sysbus WriteDoubleWord 0x40005400 0x1')   # TXDR outside a write transfer is TVI-I2C-TRANSFER: PE set and a
        print('sysbus WriteDoubleWord 0x40005404 0x220EC')   # write of 2 to the BME280 started for its rows (the two bytes)
    print('sysbus WriteDoubleWord 0x%08X 0x%X' % (0x40005400 + off, v))
    print('sysbus WriteDoubleWord 0x%08X 0x0' % (0x40005400 + off))
EOF
)
    PLATFORM_SED=$rec run_case fields - "$clk" "${lines[@]}"
    n=$(printf '%s\n' "${lines[@]}" | grep -vc ' 0x0$\| 0x40005400 0x1$\| 0x40005404 0x220EC$')
    check "fields: every field allowed only at 0 ($n), written its widest value with PE = 0, records one TVI-I2C-FIELD naming it, unmodeled; nothing else is recorded" \
      "[ \$rc = 1 ] && [ $n -ge 40 ] && logged fields 'len(recs) == $n and all(r[\"rule\"] == \"TVI-I2C-FIELD\" and r[\"kind\"] == \"unmodeled\" for r in recs) and len({(r[\"register\"], r[\"bit\"].split(\" \")[0]) for r in recs}) == $n'" ;;
  isrmon)
    # Gate clause 6's two I2C rows that the fields sweep leaves out, because it skips ISR (TVI-WO-1203 (4)): ISR.TXE and ISR.TXIS,
    # each allowed only at 0 (p2011). Each is written 1 by a monitor write (pc none), with PE = 0, recorded. Each must record
    # one TVI-I2C-FIELD, unmodeled, naming the field it wrote, matched to that write by its value.
    PLATFORM_SED=$rec run_case isrmon - "$clk" 'sysbus WriteDoubleWord 0x40005418 0x1' 'sysbus WriteDoubleWord 0x40005418 0x2'
    check "isrmon: ISR.TXE = 1, then ISR.TXIS = 1, each a monitor write with PE = 0, record one TVI-I2C-FIELD each, unmodeled, naming the field that write set (p2011; TVI-WO-1203 (4))" \
      "logged isrmon '[(r[\"rule\"], r[\"kind\"], r[\"register\"], r[\"bit\"].split(\" \")[0], int(r[\"access\"][\"value\"], 16), r[\"access\"][\"pc\"]) for r in recs] == [(\"TVI-I2C-FIELD\", \"unmodeled\", \"I2C_ISR\", \"TXE\", 1, \"none\"), (\"TVI-I2C-FIELD\", \"unmodeled\", \"I2C_ISR\", \"TXIS\", 2, \"none\")]'" ;;
  ledger)
    mkdir -p "$work/ledger"
    rc=0; container -v "$src:/repo:ro,Z" -v "$work/ledger:/out:Z" "$IMAGE" sh -c '/repo/scripts/build-release /repo /out/release >/dev/null' </dev/null >"$work/ledger.stdout" 2>&1 || rc=$?
    check "ledger: the release carries the I2C guard's field rows over its 11 registers (TIMINGR, DNF, ANFOFF, PE, START modeled; NACKF, NACKCF, NACKIE and STOP modeled by the guard, NACKF's STOPF time named inferred (TVI-WO-1306a); ICR's reserved bits 1, 2 and 6 acknowledged); the manifest restates i2c1-i2c3 and not i2c4; the stock type's guarded row lists the three, the unguarded row i2c4" \
      "[ \$rc = 0 ] && python3 -I -c '
import json, sys
rows = [json.loads(l) for l in open(sys.argv[1]).read().splitlines()[1:]]
g = \"Tviburi.GuardedSTM32F7_I2C\"
f = [r for r in rows if r.get(\"peripheral\") == g and r[\"row\"] == \"field\"]
by = {(r[\"register\"], r[\"field\"]): r for r in f}
ok = len({r[\"register\"] for r in f}) == 11 and all(by[(\"I2C_TIMINGR\", x)][\"state\"] == \"guard\" for x in (\"PRESC\", \"SCLDEL\", \"SDADEL\", \"SCLH\", \"SCLL\"))
ok = ok and by[(\"I2C_CR1\", \"DNF\")][\"state\"] == \"guard\" and by[(\"I2C_CR1\", \"ANFOFF\")][\"state\"] == \"guard\" and by[(\"I2C_CR2\", \"START\")][\"state\"] == \"guard\"
ok = ok and all(by[(r, f)][\"state\"] == \"guard\" for r, f in ((\"I2C_ISR\", \"NACKF\"), (\"I2C_ICR\", \"NACKCF\"), (\"I2C_CR1\", \"NACKIE\"), (\"I2C_CR2\", \"STOP\")))
ok = ok and \"inferred: RM0433 gives no figure\" in by[(\"I2C_ISR\", \"NACKF\")][\"behaves_as\"] and \"p1975\" in by[(\"I2C_CR2\", \"START\")][\"behaves_as\"] and by[(\"I2C_ICR\", \"Res6\")][\"allowed\"] == \"any\" and by[(\"I2C_ICR\", \"Res7\")][\"allowed\"] == [0]
ok = ok and by[(\"I2C_ICR\", \"Res2_1\")][\"state\"] == \"inherited-unaudited\" and by[(\"I2C_CR2\", \"RELOAD\")][\"allowed\"] == [0]
m = [r for r in rows if r[\"row\"] == \"model\" and r[\"peripheral\"] == \"I2C.STM32F7_I2C\"]
mg = [x for x in m if x[\"field\"] == \"guarded\"]; mu = [x for x in m if x[\"field\"] == \"unguarded\"]
ok = ok and len(mg) == 1 and mg[0].get(\"guarded_by\") == g and mg[0][\"instances\"] == [\"i2c1\", \"i2c2\", \"i2c3\"]
ok = ok and len(mu) == 1 and \"guarded_by\" not in mu[0] and mu[0][\"instances\"] == [\"i2c4\"]
r = sorted(l.split()[2] for l in open(sys.argv[2]) if l.startswith(\"restate I2C.STM32F7_I2C \"))
ok = ok and r == [\"i2c1\", \"i2c2\", \"i2c3\"]
sys.exit(0 if ok else 1)
' '$work/ledger/release/LEDGER.jsonl' '$work/ledger/release/MANIFEST' '$src/model/GuardedSTM32F7_I2C.cs'" ;;
  regen)
    rc=0; python3 -I "$src/tests/1306/fields.py" "$src/tests/1306/data/rm0433-rev9-i2c-fields.tsv" > "$work/fields.cs" || rc=$?
    mkdir -p "$work/regen"
    container -v "$src:/repo:ro,Z" -v "$work/regen:/out:Z" "$IMAGE" sh -c 'awk -v sha=$(sha256sum /opt/renode/platforms/cpus/stm32h7.repl | cut -c1-64) \
      -f /repo/model/stm32h7-i2c-restate.awk /opt/renode/platforms/cpus/stm32h7.repl > /out/restate.repl' </dev/null >"$work/regen.stdout" 2>&1 || rc=$?
    check "regen: the guard's Fields table regenerates from tests/1306/data by tests/1306/fields.py, and the restatement from the pinned stm32h7.repl, byte for byte (RUL-16)" \
      "[ \$rc = 0 ] && sed -n '/^        private static readonly Row\[\] Fields =$/,/^        };$/p' '$src/model/GuardedSTM32F7_I2C.cs' | sed '1,2d;\$d' | cmp -s - '$work/fields.cs' && cmp -s '$work/regen/restate.repl' '$src/model/stm32h7-i2c-restate.repl'" ;;
  nack) check "nack (TVI-WO-1306a acceptance, the planted firmware): a write START with AUTOEND = 1 to 0x31, no device: ISR 0x1 at the START, polled to NACKF (0x11: STOPF not yet), to STOPF (0x31), and ICR = 0x30 clears both (0x1), the guard's NACKCF not handed to the stock tag (no 'Unhandled write ... NACKCF'); no record (p1975, p2011, p2012). Its timing: nacktime" \
    "passes nack && [ \"\$(readout nack READ)\" = 0x00000001 ] && [ \"\$(readout nack READ2)\" = 0x00000011 ] && [ \"\$(readout nack READ3)\" = 0x00000031 ] && [ \"\$(readout nack READ4)\" = 0x00000001 ] && ! grep -q 'NACKCF' '$work/nack/renode.out'" ;;
  nacktime)
    # nack's configuration from the monitor: rcc_pclk1 = 32 MHz (HSI 64 MHz, D2PPRE1 = 100: /2, p396), DNF = 15, TIMINGR 0x10C7123C:
    # tSCL = ((0x12 + 1) + (0x3C + 1)) x 2 + 2 x (2 + 15) = 194 kernel clocks = 6.0625 us; the address phase 54.5625 us.
    check "nacktime (the acceptance's timing): NACKF 54.5625 us after a write START (AUTOEND = 1) and not before (ISR 0x1 at 54.06 us, 0x11 at 55.06), STOPF one SCL period (6.0625 us) later (0x11 at 60.13 us, 0x31 at 61.13), ICR = 0x30 clears both: the page's timing at rcc_pclk1 / 2, DNF 15 (p1955, p1971; the RCC guard's KernelHz, ruled Q2; STOPF's period inferred, Q3)" \
      "nackat nacktime 54.5625 6.0625 'sysbus WriteDoubleWord 0x5802441C 0x40' 'sysbus WriteDoubleWord 0x40005400 0xF00' 'sysbus WriteDoubleWord 0x40005410 0x10C7123C' 'sysbus WriteDoubleWord 0x40005400 0xF01' 'sysbus WriteDoubleWord 0x40005404 0x02012062' && passed nacktime" ;;
  nackpll)
    # pll3_r_ck = HSI 64 MHz / DIVM3 32 (reset) x 300 / 20 = 30 MHz (pp. 398-410), I2C123SEL = 01 (p416); ArduPilot's CR1 and TIMINGR
    # 0x00300F38: tSCL = (16 + 57) + 4 = 77 kernel clocks = 2.5667 us; the address phase 23.1 us.
    check "nackpll: the kernel clock pll3_r_ck at 30 MHz through the RCC guard's KernelHz, ArduPilot's TIMINGR 0x00300F38, a write START with AUTOEND = 0 to 0x30: NACKF at 23.1 us, STOPF 2.567 us later (a STOP whatever AUTOEND, p1975; ruled Q1)" \
      "nackat nackpll 23.1 2.566667 'sysbus WriteDoubleWord 0x58024440 0x1301032B' 'sysbus WriteDoubleWord 0x58024400 0x10000025' 'sysbus WriteDoubleWord 0x58024454 0x1000' 'sysbus WriteDoubleWord 0x40005400 0x90' 'sysbus WriteDoubleWord 0x40005410 0x00300F38' 'sysbus WriteDoubleWord 0x40005400 0x91' 'sysbus WriteDoubleWord 0x40005404 0x00012060' && passed nackpll" ;;
  nackirq)
    run_case nackirq nackirq 'emulation RunFor "0.00001"' 'echo "R-PRE"' 'sysbus.i2c1 EventInterrupt' 'emulation RunFor "0.0001"' 'echo "R-IRQ"' 'sysbus.i2c1 EventInterrupt' \
      'echo "R-ISPR"' 'sysbus ReadDoubleWord 0xE000E200' 'sysbus WriteDoubleWord 0x4000541C 0x10' 'echo "R-CLR"' 'sysbus.i2c1 EventInterrupt' \
      'sysbus WriteDoubleWord 0x4000541C 0x20' 'echo "R-CLR2"' 'sysbus.i2c1 EventInterrupt' \
      'sysbus WriteDoubleWord 0x40005404 0x00012062' 'emulation RunFor "0.0001"' 'sysbus WriteDoubleWord 0x4000541C 0x20' 'echo "R-CLR3"' 'sysbus.i2c1 EventInterrupt' \
      'sysbus WriteDoubleWord 0x4000541C 0x10' 'echo "R-CLR4"' 'sysbus.i2c1 EventInterrupt' 'echo "TVIBURI-MARK-AFTER"' 'sysbus ReadDoubleWord 0x20000104'
    check "nackirq: with NACKIE and STOPIE, a NACK raises I2C1's event line (unset 10 us after the START, set at 110 us; I2C1_EV pending in the NVIC, ISPR0 bit 31; p1999 Table 400); NACKCF leaves it set by the NACK's STOPF, and STOPCF drops it; a second NACK, cleared the other way round: STOPCF leaves it set by NACKF, and NACKCF drops it (p2012)" \
      "passed nackirq && [ \"\$(readout nackirq PRE)\" = 'GPIO: unset' ] && [ \"\$(readout nackirq IRQ)\" = 'GPIO: set' ] && [ \"\$(readout nackirq ISPR)\" = 0x80000000 ] && [ \"\$(readout nackirq CLR)\" = 'GPIO: set' ] && [ \"\$(readout nackirq CLR2)\" = 'GPIO: unset' ] && [ \"\$(readout nackirq CLR3)\" = 'GPIO: set' ] && [ \"\$(readout nackirq CLR4)\" = 'GPIO: unset' ]" ;;
  nackstop) check "nackstop: ChibiOS's NACK: ISR 0x11 at NACKF, ICR = ISR & 0xFE, CR2 |= STOP while the NACK's STOP is pending: absorbed (ISR 0x1, no STOPF yet: p2005), STOPF at its time (0x21), the driver restarted and the chip id read (0x60): passes (ruled Q4)" \
    "passes nackstop && [ \"\$(readout nackstop READ)\" = 0x00000011 ] && [ \"\$(readout nackstop READ2)\" = 0x00000001 ] && [ \"\$(readout nackstop READ3)\" = 0x00000021 ] && [ \"\$(readout nackstop READ4)\" = 0x00000060 ]" ;;
  nackpe) check "nackpe: PE cleared, checked and set while a NACK is pending cancels it (p1959: the state machines reset), and so does an I2C1RST pulse (p438, the guard's Reset): no NACKF comes either time (ISR 0x1, 0x1); passes" \
    "passes nackpe && [ \"\$(readout nackpe READ)\" = 0x00000001 ] && [ \"\$(readout nackpe READ2)\" = 0x00000001 ]" ;;
  nacktransfer) check "nacktransfer: a START to the BME280 while a NACKed address's automatic STOP is pending refuses TVI-I2C-TRANSFER, unmodeled (ruled Q4)" \
    "refuses nacktransfer TVI-I2C-TRANSFER unmodeled 'r[\"access\"][\"value\"] == \"0x120EC\" and \"automatic STOP is pending\" in r[\"condition\"]'" ;;
  nacktxdr) check "nacktxdr: a TXDR write after a NACKed START, before its STOPF, refuses TVI-I2C-TRANSFER, unmodeled: no data phase follows a NACK (p1975, p2014); the stock model would send the byte" \
    "refuses nacktxdr TVI-I2C-TRANSFER unmodeled 'r[\"access\"][\"offset\"] == \"0x28\" and \"outside a write transfer\" in r[\"condition\"]'" ;;
  nackclock) check "nackclock: a write START to 0x31 with I2C123SEL = 01 and PLL3's VCO at 20 MHz (outside p404's ranges: KernelHz has no frequency) refuses TVI-I2C-NACK, unmodeled (ruled Q2)" \
    "refuses nackclock TVI-I2C-NACK unmodeled 'r[\"access\"][\"offset\"] == \"0x4\" and \"cannot time it\" in r[\"condition\"] and \"outside the ranges p404 gives\" in r[\"condition\"]'" ;;
  *) echo "unknown case: $t" >&2; exit 2 ;;
  esac
done
[ $fails -eq 0 ] && echo "ALL PASS" || echo "$fails FAILED"
[ $fails -eq 0 ]
