#!/usr/bin/env bash
# tests/1301/gpio.sh [SRC] — TVI-WO-1301's acceptance cases for the GPIO guard, run on the host as tests/1300/rcc.sh
# is: each case one container in the pinned tools image (tests/spike.sh's IMAGE), no network, the source read-only.
# SRC defaults to a `git archive HEAD` export. tests/spike.sh runs each case as gpio-<case> in its acceptance group.
#   TVIBURI_TESTS="valid blank ..."  run only these cases.
#   TVIBURI_CI=1                     plain `podman run`, for a disposable CI runner only; elsewhere ~/bin/capped-run.
# Exit 0 only if every selected case passed.
set -euo pipefail

here=$(cd "$(dirname "$0")/../.." && pwd)
IMAGE=$(sed -n 's/^IMAGE=//p' "$here/tests/spike.sh")
CASES="valid blank blankfirst afterwards afrh package packageinput pk8af pk8out pc2 nopackage pupdr lckr brr read bsrr bsrrhalf otyper ardupilot gen-s03 recorded config query registrations reset misaligned fields ledger adv-monitor resetwrite recordedtwo twoaddr analogled analogout odrfirst moderfirst analoghold afodr afenter afenterhigh afout afentern inputidr button externalbad resetodr odrhalf exti afswitch afnosrc"
GUARDS="spi1 rcc gpioPortA gpioPortB gpioPortC gpioPortD gpioPortE gpioPortF gpioPortG gpioPortH gpioPortI gpioPortJ gpioPortK timer1 timer8 dma1 dma2 flashController usart1 usart2 usart3 uart4 uart5 usart6 uart7 uart8 i2c1 i2c2 i2c3"
PATHS='"valid","blank","blankfirst","afterwards","twice","afrh","package","packageinput","pk8af","pk8out","pc2","pupdr","lckr","brr","read","bsrr","bsrrhalf","otyper","ardupilot","gen-s03","analogled","analogout","odrfirst","moderfirst","analoghold","afodr","afenter","afenterhigh","afout","afentern","inputidr","odrhalf","afswitch","afnosrc","idle"'
scratch=${TVIBURI_SCRATCH:-$HOME/.cache/tviburi}
mkdir -p "$scratch"
work=$(mktemp -d "$scratch/gpio.XXXXXX")

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
# script, then the check on the log, naming every guarded source. Outputs in $work/<name>. PLATFORM: a planted
# platform in place of the prelude's board; PLATFORM_SED: a sed script making one from the board, written to /out.
run_case() { # run_case <name> <firmware path or -> <monitor lines...>
  local name=$1 fw=$2; shift 2
  mkdir -p "$work/$name"
  if [ -n "${PLATFORM_SED:-}" ]; then
    sed -e 's#^using "\./#using "/repo/model/#' -e "$PLATFORM_SED" "$src/model/tviburi-nucleo-h753zi.repl" > "$work/$name/platform.repl"
  fi
  local plat=${PLATFORM:-}
  [ -n "${PLATFORM_SED:-}" ] && plat=/out/platform.repl
  {
    if [ -n "$plat" ]; then
      sed "s#^machine LoadPlatformDescription .*#machine LoadPlatformDescription @$plat#" "$src/scripts/prelude.resc"
    else
      echo "include @/repo/scripts/prelude.resc"
    fi
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
        --check-cfg "cfg(path, values($3))" -C link-arg=-T/repo/firmware/link.x -o /out/fw.elf /repo/firmware/gpio.rs || exit 91
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
run='emulation RunFor "0.001"'
# The eleven ports' clocks (RCC_AHB4ENR = 0x7FF, GPIOAEN..GPIOKEN, RM0433 Rev 9 p461): since TVI-WO-1300a the RCC guard
# locks a peripheral whose enable is clear, as silicon (p373), so a monitor-driven case enables them first, and again
# after a machine Reset, as both firmwares and firmware/gpio.rs do before any GPIO access.
clk='sysbus WriteDoubleWord 0x580244E0 0x7FF'
marks=('echo "TVIBURI-MARK-AFTER"' 'sysbus ReadDoubleWord 0x20000104' 'echo "TVIBURI-MARK-READ"' 'sysbus ReadDoubleWord 0x20000108')
readmark() { console "$1" | grep -A1 -x 'TVIBURI-MARK-READ' | tail -n1; }
# TVI-WO-1301a: readouts after the firmware's marks. `R-<tag>` then a value; `pin P n` prints what port P's pin n carries.
marks2=('echo "TVIBURI-MARK-READ2"' 'sysbus ReadDoubleWord 0x2000010C')
readout() { console "$1" | grep -A1 -x "R-$2" | tail -n1; }
readmark2() { console "$1" | grep -A1 -x 'TVIBURI-MARK-READ2' | tail -n1; }
pin() { echo "python \"print('R-PIN$1$2${3:-}'); print(self.Machine['sysbus.gpioPort$1'].Connections[$2].IsSet)\""; }
bit() { [[ ${1:-} =~ ^0x[0-9A-Fa-f]{8}$ ]] || { echo none; return; }; (( ($1 >> $2) & 1 )) && echo 1 || echo 0; }  # a missing readout is neither 0 nor 1
# PA8 observed as the consumer's generated boards observe a motor pin (TVI-WO-1301 F1): an LED beside TIM1's input.
pa8obs='$a\
pa8obs: Miscellaneous.LED @ tviburiGpioA 8\
tviburiGpioA:\
    8 -> timer1@00 | pa8obs@0'

passes() { [ "$rc" = 0 ] && grep -qx 'CHECK PASS' "$work/$1.stdout" && after "$1"; }
fw_pc='r["access"]["pc"] != "none"'

for t in ${TVIBURI_TESTS:-$CASES}; do
  case $t in
  valid)
    run_case valid valid "$run" "${marks[@]}"
    check "valid: PA5 on AF5 (SPI1_SCK, DS12117 Table 9 p89), then AF mode: a pass, enforcing; each port's witness names LQFP144" \
      "passes valid && [ \$(grep -c 'gpioPort[A-K]: {\"tviburi\":\"witness\".*\"package\":\"LQFP144\"}' '$work/valid/renode-log.txt') = 11 ]" ;;
  blank)
    run_case blank blank "$run" "${marks[@]}"
    check "blank: PA5 on AF6 (blank, p89) put in AF mode refuses TVI-GPIO-AF, predicted-fault, naming the pin, the AF and the page, at the firmware's MODER store" \
      "[ \$rc = 3 ] && first blank 'r[\"rule\"] == \"TVI-GPIO-AF\" and r[\"kind\"] == \"predicted-fault\" and r[\"peripheral\"] == \"sysbus.gpioPortA\" and r[\"register\"] == \"GPIOA_MODER\" and r[\"bit\"] == \"PA5 AF6\" and \"DS12117 Rev 10 Table 9 p89\" in r[\"citation\"] and \"p541\" in r[\"citation\"] and $fw_pc'" ;;
  blankfirst)
    run_case blankfirst blankfirst "$run" "${marks[@]}"
    check "blankfirst: PA6 put in AF mode with AFSEL at reset (AF0, blank) refuses TVI-GPIO-AF at the MODER store" \
      "[ \$rc = 3 ] && first blankfirst 'r[\"rule\"] == \"TVI-GPIO-AF\" and r[\"bit\"] == \"PA6 AF0\" and r[\"register\"] == \"GPIOA_MODER\" and $fw_pc'" ;;
  afterwards)
    run_case afterwards afterwards "$run" "${marks[@]}"
    check "afterwards: PA5 in AF mode on AF5, then AFSEL changed to AF6 refuses TVI-GPIO-AF at the AFRL store (either order)" \
      "[ \$rc = 3 ] && first afterwards 'r[\"rule\"] == \"TVI-GPIO-AF\" and r[\"bit\"] == \"PA5 AF6\" and r[\"register\"] == \"GPIOA_AFRL\" and r[\"access\"][\"offset\"] == \"0x20\"'" ;;
  afrh)
    run_case afrh afrh "$run" "${marks[@]}"
    check "afrh: a half-word AFRH store giving PA9 AF0 (blank) is merged into its word, and AF mode then refuses TVI-GPIO-AF" \
      "[ \$rc = 3 ] && first afrh 'r[\"rule\"] == \"TVI-GPIO-AF\" and r[\"bit\"] == \"PA9 AF0\"'" ;;
  package)
    run_case package package "$run" "${marks[@]}"
    check "package: PH2 (not bonded on LQFP144, Table 8 p68) set to output refuses TVI-GPIO-PACKAGE, predicted-fault, naming the pin, the package and the page" \
      "[ \$rc = 3 ] && first package 'r[\"rule\"] == \"TVI-GPIO-PACKAGE\" and r[\"kind\"] == \"predicted-fault\" and r[\"peripheral\"] == \"sysbus.gpioPortH\" and r[\"bit\"] == \"PH2 MODER = 01\" and \"LQFP144\" in r[\"condition\"] and \"Table 8 p68\" in r[\"citation\"] and $fw_pc'" ;;
  packageinput)
    run_case packageinput packageinput "$run" "${marks[@]}"
    check "packageinput: every port written as input, unbonded pins included (a whole-port init): a pass" "passes packageinput" ;;
  pk8af|pk8out)
    run_case "$t" "$t" "$run" "${marks[@]}"
    if [ $t = pk8af ]; then
      check "pk8af: PK8 (no such pin; Table 9 lists PK0-PK7, p100) in AF mode refuses TVI-GPIO-AF, predicted-fault" \
        "[ \$rc = 3 ] && first pk8af 'r[\"rule\"] == \"TVI-GPIO-AF\" and r[\"bit\"] == \"PK8 AF0\" and \"PK0-PK7\" in r[\"citation\"]'"
    else
      check "pk8out: PK8 (on no package) set to output refuses TVI-GPIO-PACKAGE, predicted-fault" \
        "[ \$rc = 3 ] && first pk8out 'r[\"rule\"] == \"TVI-GPIO-PACKAGE\" and r[\"kind\"] == \"predicted-fault\" and r[\"bit\"] == \"PK8 MODER = 01\"'"
    fi ;;
  pc2)
    run_case pc2 pc2 "$run" "${marks[@]}"
    check "pc2: PC2 set to output on LQFP144 (bonded only through PC2_C, footnote 4 p88) refuses TVI-GPIO-PACKAGE, unmodeled" \
      "[ \$rc = 3 ] && first pc2 'r[\"rule\"] == \"TVI-GPIO-PACKAGE\" and r[\"kind\"] == \"unmodeled\" and \"footnote 4 p88\" in r[\"citation\"]'" ;;
  nopackage)
    PLATFORM_SED='/package: "LQFP144"/d' run_case nopackage package "$run" "${marks[@]}"
    check "nopackage: with no package declared the package rule does not run (PH2 to output passes), and each port's witness says \"package\":\"none\" (granted 19:08Z)" \
      "passes nopackage && [ \$(grep -c 'gpioPort[A-K]: {\"tviburi\":\"witness\".*\"package\":\"none\"}' '$work/nopackage/renode-log.txt') = 11 ]" ;;
  pupdr)
    run_case pupdr pupdr "$run" "${marks[@]}"
    check "pupdr: PUPDR0 = 11 (reserved, p542) refuses TVI-GPIO-FIELD, unmodeled" \
      "[ \$rc = 3 ] && first pupdr 'r[\"rule\"] == \"TVI-GPIO-FIELD\" and r[\"kind\"] == \"unmodeled\" and r[\"bit\"].startswith(\"GPIOA_PUPDR.PUPDR0 \") and $fw_pc'" ;;
  lckr)
    run_case lckr lckr "$run" "${marks[@]}"
    check "lckr: LCKR's key sequence started (LCKK = 1) refuses TVI-GPIO-FIELD, unmodeled (p545)" \
      "[ \$rc = 3 ] && first lckr 'r[\"rule\"] == \"TVI-GPIO-FIELD\" and r[\"bit\"].startswith(\"GPIOA_LCKR.LCKK \") and \"p545\" in r[\"citation\"]'" ;;
  brr)
    run_case brr brr "$run" "${marks[@]}"
    check "brr: a write at 0x28 (no register on the H7; Table 95 p547-548) refuses TVI-GPIO-FIELD, unmodeled" \
      "[ \$rc = 3 ] && first brr 'r[\"rule\"] == \"TVI-GPIO-FIELD\" and r[\"bit\"] == \"no register at offset 0x28\" and $fw_pc'" ;;
  read)
    run_case read read "$run" "${marks[@]}"
    check "read: a read at 0x2C (no register) refuses TVI-GPIO-READ, unmodeled" \
      "[ \$rc = 3 ] && first read 'r[\"rule\"] == \"TVI-GPIO-READ\" and r[\"kind\"] == \"unmodeled\" and $fw_pc'" ;;
  bsrr)
    run_case bsrr bsrr "$run" "${marks[@]}"
    check "bsrr: BSRR with BS0 and BR0 both set gives ODR0 = 1 (BS has priority, p544; the stock port gives 0): a pass" \
      "passes bsrr && [ \"\$(readmark bsrr)\" = 0x00000001 ]" ;;
  bsrrhalf)
    run_case bsrrhalf bsrrhalf "$run" "${marks[@]}"
    check "bsrrhalf: BSRR's halves as 16-bit stores (ArduPilot's form) set, then reset, ODR0: a pass" \
      "passes bsrrhalf && [ \"\$(readmark bsrrhalf)\" = 0x00000010 ]" ;;
  otyper)
    run_case otyper otyper "$run" "${marks[@]}"
    check "otyper: OTYPER = 0x00C0 is kept by the guard and read back (the stock port drops it): a pass" \
      "passes otyper && [ \"\$(readmark otyper)\" = 0x000000C0 ]" ;;
  ardupilot|gen-s03)
    run_case "$t" "$t" "$run" "${marks[@]}"
    check "$t: the measured GPIO write sequence passes, enforcing (WO §2)" "passes $t" ;;
  recorded)
    PLATFORM_SED='s/^    mode: "enforcing"$/    mode: "recorded"/' run_case recorded twice "$run" "${marks[@]}"
    check "recorded: in recorded mode the blank AF is one would-refuse (not one per later write), the firmware runs on to its marker, and the check never passes it (RUL-19)" \
      "[ \$rc = 1 ] && [ \$(grep -c '\"tviburi\":\"would-refuse\".*TVI-GPIO-AF' '$work/recorded/renode-log.txt') = 1 ] && after recorded" ;;
  config)
    PLATFORM_SED='$a\
tviburiGpioA:\
    modeResetValue: 0x0' run_case config - "$clk" 'sysbus ReadDoubleWord 0x58020000'
    check "config: a port given a reset value other than RM0433's (MODER 0 for port A, p541) refuses every access, TVI-GPIO-CONFIG, unmodeled" \
      "[ \$rc = 3 ] && first config 'r[\"rule\"] == \"TVI-GPIO-CONFIG\" and r[\"kind\"] == \"unmodeled\" and \"modeResetValue\" in r[\"condition\"]'" ;;
  query)
    q() { echo "python \"print('Q$1 ' + str(self.Machine['sysbus.gpioPortA'].$2($1)))\""; }
    run_case query valid "$run" "$(q 5 WhichSignal)" "$(q 5 WhichSignalCitation)" "$(q 0 WhichSignal)" "$(q 13 WhichSignal)"
    check "query: PA5 carries SPI1_SCK/I2S1_CK (p89); PA0 is analog (silicon's reset, p541); PA13 is AF0 JTMS-SWDIO" \
      "q=\$(tr -d '\\r' < '$work/query/renode.out') && grep -qx 'Q5 SPI1_SCK/I2S1_CK' <<<\"\$q\" && grep -q '^Q5 DS12117 Rev 10 Table 9 p89' <<<\"\$q\" && grep -qx 'Q0 analog' <<<\"\$q\" && grep -qx 'Q13 JTMS-SWDIO' <<<\"\$q\"" ;;
  registrations)
    run_case registrations - "$clk" 'peripherals' 'include @/repo/tests/1301/checks.py' 'sysbus WriteDoubleWord 0x58020400 0xFFFFFEBD' 'sysbus WriteDoubleWord 0x58020418 0x1' 'echo "R-GREEN"' 'sysbus.gpioPortB.GreenLED State'
    check "registrations: the 11 ports are the guard, no stock port remains, the timers' AF routes reach the guards, and GreenLED follows BSRR with PB0 an output (TVI-WO-1301a)" \
      "[ \$(grep -c 'gpioPort[A-K] (GuardedSTM32_GPIOPort)' '$work/registrations/renode.out') = 11 ] && grep -q 'TVICHK stock STM32_GPIOPort instances in the machine: 0' '$work/registrations/renode.out' && grep -q 'TVICHK timer1 out: .*sysbus.gpioPortA#8@1' '$work/registrations/renode.out' && console registrations | grep -A1 -x 'R-GREEN' | tail -n1 | grep -qx True" ;;
  reset)
    run_case reset - "$clk" 'sysbus WriteDoubleWord 0x58020004 0xC0' 'sysbus WriteDoubleWord 0x58020000 0x0' 'machine Reset' "$clk" \
      'echo "R-MODER"' 'sysbus ReadDoubleWord 0x58020000' 'echo "R-OTYPER"' 'sysbus ReadDoubleWord 0x58020004' 'echo "R-PUPDR"' 'sysbus ReadDoubleWord 0x5802000C'
    check "reset: after machine Reset port A reads silicon's resets (MODER 0xABFFFFFF p541, PUPDR 0x64000000 p542) and the OTYPER copy 0" \
      "[ \$rc = 0 ] && console reset | grep -A1 -x 'R-MODER' | tail -n1 | grep -qx 0xABFFFFFF && console reset | grep -A1 -x 'R-OTYPER' | tail -n1 | grep -qx 0x00000000 && console reset | grep -A1 -x 'R-PUPDR' | tail -n1 | grep -qx 0x64000000" ;;
  misaligned)
    run_case misaligned - "$clk" 'sysbus WriteDoubleWord 0x58020002 0x0'
    check "misaligned: a 32-bit GPIO write at 0x02 refuses TVI-GPIO-WIDTH, unmodeled" \
      "[ \$rc = 3 ] && first misaligned 'r[\"rule\"] == \"TVI-GPIO-WIDTH\" and \"misaligned\" in r[\"bit\"]'" ;;
  fields)
    mapfile -t lines < <(grep -v '^#' "$src/tests/1301/fields.resc")
    run_case fields - "$clk" "${lines[@]}"
    n=$(grep -c '^# FIELD ' "$src/tests/1301/fields.resc")
    check "fields: every field value outside the allow-list refuses by name, TVI-GPIO-FIELD, unmodeled ($n cases; gate clause 6)" \
      "records fields 'len(recs) == $n and all(r[\"rule\"] == \"TVI-GPIO-FIELD\" and r[\"kind\"] == \"unmodeled\" and r[\"bit\"].startswith(f) for r, f in zip(recs, [l[8:].strip() for l in open(\"$src/tests/1301/fields.resc\") if l.startswith(\"# FIELD \")]))'" ;;
  ledger)
    mkdir -p "$work/ledger"
    rc=0; container -v "$src:/repo:ro,Z" -v "$work/ledger:/out:Z" "$IMAGE" sh -c '/repo/scripts/build-release /repo /out/release >/dev/null' </dev/null >"$work/ledger.stdout" 2>&1 || rc=$?
    check "ledger: the release carries the GPIO guard's 31 field rows, covering every bit of the ten registers, and bench rows G1 and G2 with the (rule, bit) the blank and package cases emit (RUL-18)" \
      "[ \$rc = 0 ] && python3 -I -c '
import json, sys
rows = [json.loads(l) for l in open(sys.argv[1]).read().splitlines()[1:]]
g = [r for r in rows if r.get(\"peripheral\") == \"Tviburi.GuardedSTM32_GPIOPort\"]
f = [r for r in g if r[\"row\"] == \"field\"]
pairs = sorted((r[\"field\"], r[\"rule\"], r[\"bit\"]) for r in g if r[\"row\"] == \"bench\")
sys.exit(0 if len(f) == 31 and len({r[\"register\"] for r in f}) == 10 and pairs == [(\"G1\", \"TVI-GPIO-AF\", \"PA5 AF6\"), (\"G2\", \"TVI-GPIO-PACKAGE\", \"PH2 MODER = 01\")] else 1)
' '$work/ledger/release/LEDGER.jsonl'" ;;
  adv-monitor)
    run_case adv-monitor - 'sysbus.gpioPortA WriteDoubleWord 0x20 0x00600000' 'sysbus.gpioPortA WriteDoubleWord 0x0 0xABFFFBFF'
    check "adv-monitor: the monitor's own WriteDoubleWord on the port (not through sysbus) reaches the guard: PA5 AF6 in AF mode refuses TVI-GPIO-AF (WO §5)" \
      "[ \$rc = 3 ] && first adv-monitor 'r[\"rule\"] == \"TVI-GPIO-AF\" and r[\"bit\"] == \"PA5 AF6\"'" ;;
  resetwrite)
    run_case resetwrite - 'machine Reset' "$clk" 'sysbus WriteDoubleWord 0x5802000C 0x0' 'sysbus WriteDoubleWord 0x58020400 0x0' 'sysbus WriteDoubleWord 0x58020008 0x0' \
      'echo "R-OSPEEDR"' 'sysbus ReadDoubleWord 0x58020008' 'echo "R-PUPDR"' 'sysbus ReadDoubleWord 0x5802000C' 'echo "R-MODER"' 'sysbus ReadDoubleWord 0x58020400' \
      "python \"print('Q3 ' + str(self.Machine['sysbus.gpioPortB'].WhichSignal(3)))\""
    check "resetwrite: after a reset, writes of 0 to PUPDR A, MODER B and OSPEEDR A take (the stock register's cache is refreshed first): they read 0, and PB3 is an input" \
      "[ \$rc = 0 ] && [ \"\$(readout resetwrite OSPEEDR)\" = 0x00000000 ] && console resetwrite | grep -A1 -x 'R-PUPDR' | tail -n1 | grep -qx 0x00000000 && console resetwrite | grep -A1 -x 'R-MODER' | tail -n1 | grep -qx 0x00000000 && tr -d '\\r' < '$work/resetwrite/renode.out' | grep -qx 'Q3 input'" ;;
  recordedtwo)
    PLATFORM_SED='s/^    mode: "enforcing"$/    mode: "recorded"/' run_case recordedtwo - "$clk" 'sysbus WriteDoubleWord 0x58020020 0x06600000' 'sysbus WriteDoubleWord 0x58020000 0xABFFEBFF' 'sysbus WriteDoubleWord 0x58021C00 0xFFFFFFEF'
    check "recordedtwo: in recorded mode every pin and every rule is recorded: PA5 and PA6 on AF6 give two TVI-GPIO-AF; PH2 in AF mode on AF0 gives TVI-GPIO-AF and TVI-GPIO-PACKAGE" \
      "l='$work/recordedtwo/renode-log.txt'; [ \$(grep -c 'would-refuse.*TVI-GPIO-AF.*\"bit\":\"PA[56] AF6\"' \$l) = 2 ] && grep -q 'would-refuse.*TVI-GPIO-AF.*\"bit\":\"PH2 AF0\"' \$l && grep -q 'would-refuse.*TVI-GPIO-PACKAGE.*\"bit\":\"PH2 MODER = 10\"' \$l" ;;
  twoaddr)
    PLATFORM_SED='$a\
tviburiGpioA: @ {\
        sysbus <0x58020000, +0x400>;\
        sysbus <0x58030000, +0x400>;\
        tviburiRcc RegistrationPeripheralAlias.GPIOA\
    }' run_case twoaddr - "$clk" 'sysbus ReadDoubleWord 0x58030000'
    check "twoaddr: a port registered at a second address refuses every access, TVI-GPIO-CONFIG, unmodeled; the run fails (Renode names the port by its variable, which the check was not given)" \
      "[ \$rc = 1 ] && grep -q '\"tviburi\":\"refusal\".*\"rule\":\"TVI-GPIO-CONFIG\".*2 bus registrations' '$work/twoaddr/renode-log.txt'" ;;
  # ---- TVI-WO-1301a: what drives a pin (RM0433 Rev 9 §11.3.9-11.3.13 p537-539) ----
  analogled)
    run_case analogled analogled "$run" "${marks[@]}" 'echo "R-GreenLED"' 'sysbus.gpioPortB.GreenLED State'
    m=$(readmark analogled)
    check "analogled: BSRR = 1 on PB0, analog from reset: GreenLED stays dark (p539); ODR0 reads 1, as written (p537); IDR0 reads 0 (p539)" \
      "passes analogled && [ \"\$(readout analogled GreenLED)\" = False ] && [ \$(bit $m 16) = 1 ] && [ \$(bit $m 0) = 0 ]" ;;
  analogout|odrfirst|moderfirst)
    run_case "$t" "$t" "$run" "${marks[@]}" 'echo "R-GreenLED"' 'sysbus.gpioPortB.GreenLED State'
    check "$t: ODR set on PB0 and PB0 made an output, in the firmware's order: GreenLED lit (p537); the switch drives the latched ODR" \
      "passes $t && [ \"\$(readout $t GreenLED)\" = True ]"
    [ $t != analogout ] || check "analogout: IDR0 reads the driven pin, 1 (p537)" "[ \$(bit \$(readmark analogout) 0) = 1 ]" ;;
  analoghold)
    run_case analoghold analoghold "$run" "${marks[@]}" 'echo "R-GreenLED"' 'sysbus.gpioPortB.GreenLED State'
    check "analoghold: PB0 driven high, then analog: nothing drives it, so it is low (GreenLED dark, p539; ruled 2026-11-24T01:05Z); IDR0 reads 0 (p539)" \
      "passes analoghold && [ \"\$(readout analoghold GreenLED)\" = False ] && [ \$(bit \$(readmark analoghold) 0) = 0 ]" ;;
  afodr|afenter|afenterhigh|afout|afentern|afswitch|afnosrc)
    PLATFORM_SED=$pa8obs run_case "$t" "$t" "$run" "${marks[@]}" 'echo "R-pa8obs"' 'sysbus.gpioPortA.pa8obs State' "$(pin A 7)" "$(pin A 0)" "$(pin A 5)" "$(pin A 15)"
    m=$(readmark "$t"); obs=$(readout "$t" pa8obs)
    case $t in
    afodr) check "afodr: PA8 on AF1 (TIM1_CH1, p89), TIM1 low, then ODR = 0x100: the observer follows TIM1, not ODR (p538); ODR8 reads 1, IDR8 0; PA15, AF0 at reset with no source, carries its reset pull-up (p542) from the port's first access" \
      "passes afodr && [ $obs = False ] && [ \$(bit $m 24) = 1 ] && [ \$(bit $m 8) = 0 ] && [ \"\$(readout afodr PINA15)\" = True ]" ;;
    afswitch) check "afswitch: PA0 in AF mode moved from AF1 (TIM2_CH1, low) to AF2 (TIM5_CH1, high): the pin follows the newly selected AF (p538)" \
      "passes afswitch && [ $m = 0x00000001 ] && [ \"\$(readout afswitch PINA0)\" = True ]" ;;
    afnosrc) check "afnosrc: PA5 high, then AF5 (SPI1_SCK, which no Renode model drives): the pin holds high through an ODR reset (ruled 23:52Z (3)), IDR5 reads the held level, and a pull-down then takes it low (p542)" \
      "passes afnosrc && [ \$(bit $m 5) = 1 ] && [ \"\$(readout afnosrc PINA5)\" = False ]" ;;
    afenter) check "afenter: PA8 an output, high, then AF1 with TIM1 low: the pin takes TIM1's level at the switch (p538)" \
      "passes afenter && [ $obs = False ] && [ \$(bit $m 8) = 0 ]" ;;
    afenterhigh) check "afenterhigh: PA8 an output, low, then AF1 with TIM1 high (CCR1 > ARR): the pin goes high at the switch (p538)" \
      "passes afenterhigh && [ $obs = True ] && [ \$(bit $m 8) = 1 ]" ;;
    afout) check "afout: PA8 on AF1 with TIM1 low and ODR8 = 1; output reads 1, AF again reads 0, and the observer ends low" \
      "passes afout && [ $obs = False ] && [ $m = 0x00000002 ]" ;;
    afentern) check "afentern: PA7 an output, low, then AF1 (TIM1_CH1N, p89) with CH1 low: the pin takes CH1N, CH1 inverted as Renode routes it" \
      "passes afentern && [ \"\$(readout afentern PINA7)\" = True ] && [ \$(bit $m 7) = 1 ]" ;;
    esac ;;
  inputidr)
    PLATFORM_SED='$a\
tviburiGpioD:\
    external: "PD5:up PD7:up"' run_case inputidr inputidr "$run" "${marks[@]}" "${marks2[@]}" "$(pin D 1)" "$(pin D 2)" "$(pin D 4)" "$(pin D 7)"
    check "inputidr: IDR by mode on port D: a floating input reads 0 (ruled 23:52Z), PUPDR's pulls read as pulled (p537), an output reads ODR, analog 0 (p539), a declared level as declared; ODR reads as written (0x2A, ODR 0x5D); a pull written on an input pin moves the pin (PD1 up, PD2 down), none applies in analog (PD4 low), and an analog pin declared up is high while IDR7 reads 0 (p539)" \
      "passes inputidr && [ \"\$(readmark inputidr)\" = 0x0000002A ] && [ \$(( \$(readmark2 inputidr) & 0x7F )) = \$((0x5D)) ] && [ \"\$(readout inputidr PIND1)\" = True ] && [ \"\$(readout inputidr PIND2)\" = False ] && [ \"\$(readout inputidr PIND4)\" = False ] && [ \"\$(readout inputidr PIND7)\" = True ]" ;;
  button)
    run_case button idle "$run" 'sysbus.gpioPortC.UserButton1 Press' 'emulation RunFor "0.00001"' "$(pin C 13 PRE)" 'sysbus.gpioPortC.UserButton1 Release' 'emulation RunFor "0.00001"' \
      'sysbus WriteDoubleWord 0x58020800 0xF3FFFFFF' 'sysbus.gpioPortC.UserButton1 Press' 'emulation RunFor "0.00001"' 'echo "R-IN"' 'sysbus ReadDoubleWord 0x58020810' "$(pin C 13)" \
      'sysbus.gpioPortC.UserButton1 Release' 'emulation RunFor "0.00001"' 'sysbus WriteDoubleWord 0x58020800 0xF7FFFFFF' 'sysbus.gpioPortC.UserButton1 Press' 'emulation RunFor "0.00001"' \
      'echo "R-ODR"' 'sysbus ReadDoubleWord 0x58020814' 'echo "R-IDR"' 'sysbus ReadDoubleWord 0x58020810' "$(pin C 13 OUT)" \
      'sysbus WriteDoubleWord 0x58024488 0x4' 'sysbus WriteDoubleWord 0x58024488 0x0' "$(pin C 13 RST)"
    check "button: the Nucleo's button (an external sender) sets PC13 before the port's first access, and in input mode (IDR13 = 1, the pin high), not in output mode, where ODR and IDR read the 0 written and the pin stays low (p537); held through GPIOCRST (p435), the pin is high again: its level is outside the port" \
      "[ \$rc = 0 ] && [ \"\$(readout button PINC13PRE)\" = True ] && [ \"\$(readout button IN)\" = 0x00002000 ] && [ \"\$(readout button PINC13)\" = True ] && [ \"\$(readout button ODR)\" = 0x00000000 ] && [ \"\$(readout button IDR)\" = 0x00000000 ] && [ \"\$(readout button PINC13OUT)\" = False ] && [ \"\$(readout button PINC13RST)\" = True ]" ;;
  odrhalf)
    run_case odrhalf odrhalf "$run" "${marks[@]}" 'echo "R-GreenLED"' 'sysbus.gpioPortB.GreenLED State'
    check "odrhalf: a 16-bit ODR store (merged into its word, p541) on analog PB0 is latched, not driven: ODR0 reads 1, GreenLED dark (WO §5)" \
      "passes odrhalf && [ \"\$(readout odrhalf GreenLED)\" = False ] && [ \$(bit \$(readmark odrhalf) 0) = 1 ]" ;;
  exti)
    run_case exti idle "$run" 'sysbus WriteDoubleWord 0x580244F4 0x10002' 'sysbus WriteDoubleWord 0x58000414 0x20' 'sysbus WriteDoubleWord 0x58000000 0x2000' \
      'sysbus WriteDoubleWord 0x58000080 0x2000' 'sysbus WriteDoubleWord 0x58020800 0xF3FFFFFF' 'sysbus WriteDoubleWord 0x58020818 0x2000' 'echo "R-PR"' 'sysbus ReadDoubleWord 0x58000088' \
      'echo "R-EXTICR"' 'sysbus ReadDoubleWord 0x58000414' 'sysbus WriteDoubleWord 0x58020800 0xF7FFFFFF' 'echo "R-PROUT"' 'sysbus ReadDoubleWord 0x58000088'
    check "exti: PC13 an input on EXTI13 (rising, unmasked): BSRR = 0x2000 fires nothing (p537; the stock port fires it); made an output, the latched 1 rises and EXTI13 is pending" \
      "[ \$rc = 0 ] && [ \"\$(readout exti EXTICR)\" = 0x00000020 ] && [ \"\$(readout exti PR)\" = 0x00000000 ] && [ \"\$(readout exti PROUT)\" = 0x00002000 ]" ;;
  externalbad)
    PLATFORM_SED='$a\
tviburiGpioD:\
    external: "PA5:up"' run_case externalbad - "$clk" 'sysbus ReadDoubleWord 0x58020C00'
    check "externalbad: port D declared with a pin of port A refuses every access, TVI-GPIO-CONFIG, unmodeled, naming the declaration" \
      "[ \$rc = 3 ] && first externalbad 'r[\"rule\"] == \"TVI-GPIO-CONFIG\" and r[\"kind\"] == \"unmodeled\" and \"external\" in r[\"condition\"] and r[\"peripheral\"] == \"sysbus.gpioPortD\"'" ;;
  resetodr)
    run_case resetodr - "$clk" 'sysbus WriteDoubleWord 0x58020400 0xFFFFFEBD' 'sysbus WriteDoubleWord 0x58020418 0x1' 'echo "R-LIT"' 'sysbus.gpioPortB.GreenLED State' \
      'machine Reset' "$(pin B 0 RESET)" "$(pin B 4 RESET)" "$clk" 'sysbus WriteDoubleWord 0x58020400 0xFFFFFEBD' 'echo "R-OUT"' 'sysbus.gpioPortB.GreenLED State'
    check "resetodr: GreenLED lit, then machine Reset: PB0 low, PB4 carries its reset pull-up again (p542), and PB0 made an output drives ODR's reset value, 0 (p543)" \
      "[ \$rc = 0 ] && [ \"\$(readout resetodr LIT)\" = True ] && [ \"\$(readout resetodr PINB0RESET)\" = False ] && [ \"\$(readout resetodr PINB4RESET)\" = True ] && [ \"\$(readout resetodr OUT)\" = False ]" ;;
  *) echo "unknown case $t" >&2; exit 2 ;;
  esac
done

echo "$fails failed"
[ $fails = 0 ]
