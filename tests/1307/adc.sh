#!/usr/bin/env bash
# tests/1307/adc.sh [SRC] — TVI-WO-1307's acceptance cases for the ADC guard, run on the host as tests/1306/i2c.sh is: each case
# one container in the pinned tools image (tests/spike.sh's IMAGE), no network, the source read-only. SRC defaults to a
# `git archive HEAD` export. tests/spike.sh runs each case as adc-<case> in its acceptance group.
#   TVIBURI_TESTS="start kernel ..."  run only these cases.
#   TVIBURI_CI=1                      plain `podman run`, for a disposable CI runner only; elsewhere ~/bin/capped-run.
# Exit 0 only if every selected case passed. Pages: RM0433 Rev 9 §25 (ADC); DS12117 Rev 10 Table 76 p166-167 (rev Y) and Table 173
# p272-274 (rev V); the rulings of 2026-11-25T21:17Z (Q1-Q14) and the measurement in docs/work-orders/TVI-WO-1307.measure.md.
# The board is main's with ADC1 channel 10 declared at 1.2 V (`levels: "10:1.2"`, a test value, ruled Q10).
set -euo pipefail

here=$(cd "$(dirname "$0")/../.." && pwd)
IMAGE=$(sed -n 's/^IMAGE=//p' "$here/tests/spike.sh")
CASES="start res12 kernel clocklost ckmode deeppwd vreg calen cfgr undeclared early res boost boostack pcsel field width half read dfsdm oneshotnone oneshot oneshotcirc internal internalok ccrack ccrother rate rate2 rccchange rccboost revy overrun overwrite setvoltage norevision badlevels registrations recorded rccreset unheld query fields ledger regen"
GUARDS="spi1 rcc gpioPortA gpioPortB gpioPortC gpioPortD gpioPortE gpioPortF gpioPortG gpioPortH gpioPortI gpioPortJ gpioPortK timer1 timer8 dma1 dma2 flashController usart1 usart2 usart3 uart4 uart5 usart6 uart7 uart8 i2c1 i2c2 i2c3 adcM1S2 adc3"
MONITOR="rate rate2 rccchange rccboost revy overrun overwrite setvoltage norevision badlevels registrations recorded rccreset unheld query fields ledger regen"
PATHS=$(for c in $CASES; do case " $MONITOR " in *" $c "*) ;; *) printf '"%s",' "$c" ;; esac; done | sed 's/,$//')
scratch=${TVIBURI_SCRATCH:-$HOME/.cache/tviburi}
mkdir -p "$scratch"
work=$(mktemp -d "$scratch/adc.XXXXXX")

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

# One container: build the check and (for a firmware case) the planted firmware, run Renode on the case's monitor script, then the
# check on the log, naming every guarded source. Outputs in $work/<name>. The board is main's with ADC1 channel 10 declared at 1.2 V;
# PLATFORM_SED, a sed script applied to it; PLATFORM, another platform file instead.
run_case() { # run_case <name> <firmware path or -> <monitor lines...>
  local name=$1 fw=$2; shift 2
  mkdir -p "$work/$name"
  if [ -n "${PLATFORM:-}" ]; then
    sed 's#^using "\./#using "/repo/model/#' "$src/${PLATFORM#/repo/}" > "$work/$name/platform.repl"
  else
    { sed -e 's#^using "\./#using "/repo/model/#' "$src/model/tviburi-nucleo-h753zi.repl"; echo; printf 'tviburiAdc12:\n    levels: "10:1.2"\n\ntviburiAdc3:\n    levels: "18:0.7"\n'; } \
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
        --check-cfg "cfg(path, values($3))" -C link-arg=-T/repo/firmware/link.x -o /out/fw.elf /repo/firmware/adc.rs || exit 91
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
refuses() { # refuses <name> <rule> <kind> <python expr over r> [peripheral]
  [ "$rc" = 3 ] && first "$1" "r[\"rule\"] == \"$2\" and r[\"kind\"] == \"$3\" and r[\"peripheral\"] == \"sysbus.${5:-adcM1S2}\" and r[\"access\"][\"pc\"] != \"none\" and $4"
}
logged() { # logged <name> <python expr over `recs`, the records under <source> in the log, in order> [source]
  python3 -I -c '
import json, sys
s = sys.argv[3] + ": {\"tviburi\":\""
recs = [json.loads(l.split(sys.argv[3] + ": ", 1)[1]) for l in open(sys.argv[1]) if s in l and "\"rule\"" in l]
sys.exit(0 if eval("(" + sys.argv[2] + ")") else 1)
' "$work/$1/renode-log.txt" "$2" "${3:-adcM1S2}"
}
marks=('echo "TVIBURI-MARK-AFTER"' 'sysbus ReadDoubleWord 0x20000104' 'echo "R-READ"' 'sysbus ReadDoubleWord 0x20000108' 'echo "R-READ2"' 'sysbus ReadDoubleWord 0x2000010C')
clk='sysbus WriteDoubleWord 0x580244D8 0x20'   # ADC12EN (p455): an unclocked ADC is locked on the bus (TVI-WO-1300a)
w() { printf 'sysbus WriteDoubleWord 0x%X 0x%X' $((0x40022000 + $1)) "$2"; }
# ADC1 on CKMODE = 11 (8 MHz on rev V), out of deep-power-down, its regulator started, channel <ch> preselected and sampled 387.5
# cycles alone in the sequence, CFGR = <cfgr>, enabled; by the monitor (no PC: the passing paths only).
on() { # on <ch> <cfgr> [ccr] [boost]
  local ch=$1 cfgr=$2 ccr=${3:-0x30000} boost=${4:-0x100}
  printf '%s\n' "$clk" "$(w 0x308 $ccr)" "$(w 0x08 0)" "$(w 0x08 $((0x10000000 | boost)))" 'emulation RunFor "0.00002"' \
    "$(w 0x1C $((1 << ch)))" "$(w 0x18 $((6 << (3 * (ch - 10)))))" "$(w 0x30 $((ch << 6)))" "$(w 0x0C $cfgr)" \
    "$(w 0x08 $((0x10000001 | boost)))" 'emulation RunFor "0.00001"'
}
start() { w 0x08 $((0x10000005 | ${1:-0x100})); }
rec='/^tviburiAdc12:$/{n;s/enforcing/recorded/}'
pll2='python "self.Machine.SystemBus.WriteDoubleWord(0x58024400, self.Machine.SystemBus.ReadDoubleWord(0x58024400) | (1 << 26))"'   # PLL2ON (p383)

for t in ${TVIBURI_TESTS:-$CASES}; do
  case " $MONITOR " in *" $t "*) mon=1 ;; *) mon=0 ;; esac
  if [ $mon = 0 ]; then
    run_case "$t" "$t" 'emulation RunFor "0.03"' "${marks[@]}"
  fi
  case $t in
  start) check "start: the enable sequence (DEEPPWD cleared, ADVREGEN and its start-up time, a calibration with ADCALLIN, ADEN and ADRDY) and one conversion of channel 10, declared at 1.2 V, pass; DR reads 0x5D17 (round(1.2 / 3.3 x 65535), p996), ISR 0x100B after (EOC cleared by the read, p1000; LDORDY on rev V)" \
    "passes start && [ \"\$(readout start READ)\" = 0x00005D17 ] && [ \"\$(readout start READ2)\" = 0x0000100B ]" ;;
  res12) check "res12: RES = 110 (12 bits on rev V, p1016) reads 0x5D1 (round(1.2 / 3.3 x 4095))" \
    "passes res12 && [ \"\$(readout res12 READ)\" = 0x000005D1 ]" ;;
  kernel) check "kernel: ADSTART with CKMODE = 00 and ADCSEL's pll2_p_ck stopped (PLL2 turned off after ADEN) refuses TVI-ADC-KERNEL, predicted-fault, naming the ADC, the selector and p920 (ruled Q3; the WO's acceptance)" \
    "refuses kernel TVI-ADC-KERNEL predicted-fault 'r[\"bit\"].startswith(\"RCC_D3CCIPR.ADCSEL = 00\") and \"p920\" in r[\"citation\"] and \"adcM1S2 ADSTART\" in r[\"condition\"] and \"PLL2ON = 0\" in r[\"condition\"] and r[\"access\"][\"value\"] == \"0x10000205\"'" ;;
  clocklost) check "clocklost: PLL2, ADCSEL's source, stopped under running conversions (ADSTART, CKMODE = 00) refuses TVI-RCC-CLOCK, predicted-fault, at the RCC write, naming ADSTART and p920 (IRccWatched, granted Q3; WO §5)" \
    "[ \$rc = 3 ] && records clocklost 'len(recs) == 1 and recs[0][\"rule\"] == \"TVI-RCC-CLOCK\" and recs[0][\"peripheral\"] == \"sysbus.rcc\" and \"adc12 is enabled (ADSTART = 1)\" in recs[0][\"condition\"] and \"p920\" in recs[0][\"citation\"]'" ;;
  ckmode) check "ckmode: CCR.CKMODE changed with ADEN = 1 refuses TVI-ADC-STATE, unmodeled, naming CKMODE, p934 and p1038-1039 (ruled Q5; WO §5)" \
    "refuses ckmode TVI-ADC-STATE unmodeled 'r[\"register\"] == \"ADCx_CCR\" and \"CKMODE\" in r[\"bit\"] and \"p934\" in r[\"citation\"] and \"p1038-1039\" in r[\"citation\"]'" ;;
  deeppwd) check "deeppwd: ADVREGEN set with DEEPPWD = 1 refuses TVI-ADC-STATE, unmodeled (p926, p933; ruled Q5)" \
    "refuses deeppwd TVI-ADC-STATE unmodeled 'r[\"bit\"] == \"ADVREGEN (bit 28) = 1\" and \"DEEPPWD = 1\" in r[\"condition\"]'" ;;
  vreg) check "vreg: ADEN right after ADVREGEN, before t_ADCVREG_STUP, refuses TVI-ADC-STATE, unmodeled (p933; DS12117; ruled Q5)" \
    "refuses vreg TVI-ADC-STATE unmodeled 'r[\"bit\"] == \"ADEN (bit 0) = 1\" and \"start-up time\" in r[\"condition\"]'" ;;
  calen) check "calen: ADCAL with ADEN = 1 refuses TVI-ADC-STATE, unmodeled (p1003; ruled Q5)" \
    "refuses calen TVI-ADC-STATE unmodeled 'r[\"bit\"] == \"ADCAL (bit 31) = 1\" and \"ADEN = 1\" in r[\"condition\"]'" ;;
  cfgr) check "cfgr: CFGR changed while ADSTART = 1 refuses TVI-ADC-STATE, unmodeled (p934; ruled Q5)" \
    "refuses cfgr TVI-ADC-STATE unmodeled 'r[\"register\"] == \"ADC_CFGR\" and \"ADSTART = 1\" in r[\"condition\"] and r[\"access\"][\"offset\"] == \"0xC\"'" ;;
  undeclared) check "undeclared: DR read holding a conversion of channel 11, whose level nothing declared, refuses TVI-ADC-LEVEL, unmodeled (never 0 V by default; ruled Q10; WO §5)" \
    "refuses undeclared TVI-ADC-LEVEL unmodeled 'r[\"bit\"] == \"channel 11\" and r[\"access\"][\"offset\"] == \"0x40\"'" ;;
  early) check "early: DR read before EOC returns its previous content (0, no conversion yet) with no record; after EOC it reads 0x5D17 (WO §5: a level read before the sample time has passed)" \
    "passes early && [ \"\$(readout early READ)\" = 0x00000000 ] && [ \"\$(readout early READ2)\" = 0x00005D17 ]" ;;
  res) check "res: ADSTART with RES = 100, a code rev V reserves, refuses TVI-ADC-RATE, unmodeled (p1016; ruled Q2)" \
    "refuses res TVI-ADC-RATE unmodeled 'r[\"bit\"] == \"RES (4:2) = 100\" and \"p1016\" in r[\"citation\"]'" ;;
  boost) check "boost: ADSTART with BOOST = 00 at f_ADC 8 MHz (rev V asks 01) refuses TVI-ADC-RATE, unmodeled, naming p922 and p1009 (ruled Q7)" \
    "refuses boost TVI-ADC-RATE unmodeled 'r[\"bit\"] == \"BOOST (9:8) = 0\" and \"8 MHz\" in r[\"condition\"] and \"p1009\" in r[\"citation\"]'" ;;
  boostack) check "boostack: BOOST = 01 at f_ADC 16.125 MHz, where rev V asks 10 (ArduPilot's ChibiOS pattern), passes, acknowledged by name (ruled Q7); DR 0x5D17" \
    "passes boostack && [ \"\$(readout boostack READ)\" = 0x00005D17 ]" ;;
  pcsel) check "pcsel: ADSTART with channel 10 sequenced and not preselected refuses TVI-ADC-PCSEL, unmodeled (p935, p1021)" \
    "refuses pcsel TVI-ADC-PCSEL unmodeled 'r[\"bit\"] == \"PCSEL10 = 0\"'" ;;
  field) check "field: CFGR.EXTEN = 01 refuses TVI-ADC-FIELD, unmodeled (the allow-list, ruled Q12)" \
    "refuses field TVI-ADC-FIELD unmodeled 'r[\"register\"] == \"ADC_CFGR\" and r[\"bit\"] == \"EXTEN (11:10) = 01\"'" ;;
  width) check "width: a byte write to CR refuses TVI-ADC-WIDTH, unmodeled (p926)" \
    "refuses width TVI-ADC-WIDTH unmodeled 'r[\"bit\"] == \"8-bit write\" and \"p926\" in r[\"citation\"]'" ;;
  half) check "half: a half-word read of DR with DMNGT = 00 (not ArduPilot's DMA pattern) refuses TVI-ADC-WIDTH, unmodeled (ruled Q14)" \
    "refuses half TVI-ADC-WIDTH unmodeled 'r[\"bit\"] == \"16-bit read\" and r[\"access\"][\"offset\"] == \"0x40\"'" ;;
  read) check "read: a read at 0x100, ADC2's window, refuses TVI-ADC-READ, unmodeled (ADC2 is not modeled, ruled Q1)" \
    "refuses read TVI-ADC-READ unmodeled 'r[\"access\"][\"offset\"] == \"0x100\" and \"ADC2\" in r[\"condition\"]'" ;;
  dfsdm) check "dfsdm: ADSTART with DMNGT = 10 refuses TVI-ADC-DMNGT, unmodeled (no DFSDM model; p1016; ruled Q8)" \
    "refuses dfsdm TVI-ADC-DMNGT unmodeled 'r[\"bit\"] == \"DMNGT (1:0) = 10\" and \"DFSDM\" in r[\"condition\"]'" ;;
  oneshotnone) check "oneshotnone: ADSTART with DMNGT = 01 and no DMA stream selecting request 9 refuses TVI-ADC-DMNGT, unmodeled (ruled Q8)" \
    "refuses oneshotnone TVI-ADC-DMNGT unmodeled 'r[\"bit\"] == \"DMNGT (1:0) = 01\" and \"request 9\" in r[\"condition\"]'" ;;
  oneshot) check "oneshot: DMNGT = 01, continuous, on a stream of 4 words: the stream ends (NDT 0) and the ADC raises no more requests and never overruns (OVR clear after 2 ms; p964-965, ruled Q8; WO §5: a stream that never ends)" \
    "passes oneshot && [ \$(( \$(readout oneshot READ) & 0x10 )) = 0 ] && [ \"\$(readout oneshot READ2)\" = 0x00000000 ]" ;;
  oneshotcirc) check "oneshotcirc: ADSTART with DMNGT = 01 on a circular DMA stream refuses TVI-ADC-DMNGT, unmodeled: its transfers never end, so the twin cannot see the last (WO §5: a stream that never ends)" \
    "refuses oneshotcirc TVI-ADC-DMNGT unmodeled 'r[\"bit\"] == \"DMNGT (1:0) = 01\" and \"circular\" in r[\"condition\"]'" ;;
  internal) check "internal: ADC3's VSENSE (channel 18, declared 0.7 V) converted with TSEN = 0 refuses TVI-ADC-LEVEL at the DR read, unmodeled: the source is off (p920, p1038; WO §5: a sample the twin invents)" \
    "refuses internal TVI-ADC-LEVEL unmodeled 'r[\"bit\"] == \"channel 18\" and \"TSEN\" in r[\"condition\"]' adc3" ;;
  internalok) check "internalok: the same with TSEN = 1 passes and reads 0x364D (round(0.7 / 3.3 x 65535))" \
    "passes internalok && [ \"\$(readout internalok READ)\" = 0x0000364D ]" ;;
  ccrack) check "ccrack: ADC3's CCR VREFEN, TSEN and VBATEN written one by one with ADEN = 1 (ArduPilot's order) pass, acknowledged by name (ruled Q6); they accumulate: CCR 0x01C30000" \
    "passes ccrack && [ \"\$(readout ccrack READ)\" = 0x01C30000 ]" ;;
  ccrother) check "ccrother: ADC3's CCR.PRESC changed with ADEN = 1 refuses TVI-ADC-STATE, unmodeled: only ArduPilot's internal-channel bits are acknowledged (ruled Q6)" \
    "refuses ccrother TVI-ADC-STATE unmodeled 'r[\"register\"] == \"ADCx_CCR\" and \"PRESC\" in r[\"bit\"]' adc3" ;;
  rate)
    # 8 MHz (CKMODE = 11: 64 MHz HSI / 4 / 2, rev V p1039), 387.5 + 8.5 = 396 cycles: EOC at 49.5 us after ADSTART (Table 214 p955).
    mapfile -t s < <(on 10 0)
    run_case rate - "${s[@]}" "$(start)" 'emulation RunFor "0.0000494"' 'echo "R-BEFORE"' "sysbus ReadDoubleWord 0x40022000" \
      'emulation RunFor "0.0000002"' 'echo "R-AFTER"' "sysbus ReadDoubleWord 0x40022000" 'echo "R-DR"' "sysbus ReadDoubleWord 0x40022040"
    check "rate: at f_ADC 8 MHz (CKMODE = 11, rev V's /2) a 387.5-cycle 16-bit conversion's EOC is not set 49.4 us after ADSTART and is set at 49.6 us (396 cycles = 49.5 us, p1019, p955; ruled Q4); DR 0x5D17" \
      "passed rate && [ \"\$(readout rate BEFORE)\" = 0x00001001 ] && [ \"\$(readout rate AFTER)\" = 0x0000100F ] && [ \"\$(readout rate DR)\" = 0x00005D17 ]" ;;
  rate2)
    # CKMODE = 00 on pll2_p_ck (ADCSEL's reset, p419), PLL2 started from reset (HSI / 32 x 129 / 2 = 129 MHz) / PRESC 4 / 2 = 16.125
    # MHz, BOOST = 10: 396 cycles = 24.56 us.
    mapfile -t s < <(on 10 0 0x80000 0x200)
    run_case rate2 - "$pll2" "${s[@]}" "$(start 0x200)" 'emulation RunFor "0.0000244"' 'echo "R-BEFORE"' \
      "sysbus ReadDoubleWord 0x40022000" 'emulation RunFor "0.0000002"' 'echo "R-AFTER"' "sysbus ReadDoubleWord 0x40022000"
    check "rate2: on the kernel clock (CKMODE = 00, ADCSEL = pll2_p_ck 129 MHz, PRESC /4: 16.125 MHz on rev V) EOC comes at 24.56 us, not before 24.4 us (p920, p1039; ruled Q4)" \
      "passed rate2 && [ \"\$(readout rate2 BEFORE)\" = 0x00001001 ] && [ \"\$(readout rate2 AFTER)\" = 0x0000100F ]" ;;
  rccchange)
    # Revision Y (BOOST = 1 allows any f_ADC up to 36 MHz, p1005): continuous on CKMODE = 11, adc_hclk / 4 = 16 MHz, 24.75 us a
    # conversion. At 50 us (two EOCs) DR is read and HPRE set to /2 (RCC_D1CFGR, p395): hclk 32 MHz, 8 MHz. The conversion under way
    # ends at 74.25 us; the next takes the new clock, 49.5 us, so its EOC comes at 123.75 us, not at 99 us.
    mapfile -t s < <(on 10 0x2000)
    PLATFORM_SED='/^tviburiAdc12:$/,/^$/s/revision: "V"/revision: "Y"/' run_case rccchange - "${s[@]}" "$(start)" 'emulation RunFor "0.00005"' \
      'sysbus ReadDoubleWord 0x40022040' 'sysbus WriteDoubleWord 0x58024418 0x8' 'emulation RunFor "0.00003"' 'sysbus ReadDoubleWord 0x40022040' \
      'emulation RunFor "0.00003"' 'echo "R-MID"' "sysbus ReadDoubleWord 0x40022000" 'emulation RunFor "0.00002"' 'echo "R-END"' "sysbus ReadDoubleWord 0x40022000"
    check "rccchange: HPRE changed (/2) under continuous conversions on adc_hclk (rev Y): the next conversion takes the new clock, 49.5 us, not the rate computed at ADSTART (no EOC at 110 us, EOC by 130 us; p920-921; WO §5)" \
      "passed rccchange && [ \$(( \$(readout rccchange MID) & 4 )) = 0 ] && [ \$(( \$(readout rccchange END) & 4 )) = 4 ]" ;;
  rccboost)
    # Revision V: the same change halves f_ADC from 8 to 4 MHz, below BOOST = 01's range (p1009).
    mapfile -t s < <(on 10 0x2000)
    run_case rccboost - "${s[@]}" "$(start)" 'emulation RunFor "0.0001"' 'sysbus WriteDoubleWord 0x58024418 0x8' 'emulation RunFor "0.0002"'
    check "rccboost: HPRE changed under continuous conversions on rev V takes f_ADC to 4 MHz, where BOOST = 01 no longer holds: TVI-ADC-RATE, unmodeled, at the next conversion (p1009; ruled Q7)" \
      "[ \$rc = 3 ] && records rccboost 'len(recs) == 1 and recs[0][\"rule\"] == \"TVI-ADC-RATE\" and \"changed under running conversions\" in recs[0][\"condition\"] and \"4 MHz\" in recs[0][\"condition\"]'" ;;
  revy)
    mapfile -t s < <(on 10 0)
    PLATFORM_SED='/^tviburiAdc12:$/,/^$/s/revision: "V"/revision: "Y"/' run_case revy - "${s[@]}" "$(start)" 'emulation RunFor "0.0000246"' 'echo "R-BEFORE"' \
      "sysbus ReadDoubleWord 0x40022000" 'emulation RunFor "0.0000002"' 'echo "R-AFTER"' "sysbus ReadDoubleWord 0x40022000"
    check "revy: the same start on revision Y converts twice as fast (adc_hclk / 4 = 16 MHz, no /2: 24.75 us) and has no LDORDY (ISR 0x1, then 0xF) (p920-921, p998, p1039; ruled Q2)" \
      "passed revy && [ \"\$(readout revy BEFORE)\" = 0x00000001 ] && [ \"\$(readout revy AFTER)\" = 0x0000000F ]" ;;
  overrun|overwrite)
    # Channel 11 declared by SetVoltage at 1.2 V; continuous, no DMA, DR not read: the second conversion ends with EOC set.
    [ $t = overrun ] && cfgr=0x2000 || cfgr=0x3000
    mapfile -t s < <(on 11 $cfgr)
    run_case $t - 'sysbus.adcM1S2 SetVoltage 1200000 11' "${s[@]}" "$(start)" 'emulation RunFor "0.00006"' 'sysbus.adcM1S2 SetVoltage 2400000 11' \
      'emulation RunFor "0.00005"' 'echo "R-ISR"' "sysbus ReadDoubleWord 0x40022000" 'echo "R-DR"' "sysbus ReadDoubleWord 0x40022040"
    if [ $t = overrun ]; then
      check "overrun: a conversion ending with EOC still set sets OVR (p999); with OVRMOD = 0 DR keeps the first conversion, 0x5D17, not the second's (p1015; ruled Q9)" \
        "passed overrun && [ \$(( \$(readout overrun ISR) & 0x14 )) = 20 ] && [ \"\$(readout overrun DR)\" = 0x00005D17 ]"
    else
      check "overwrite: the same with OVRMOD = 1 overwrites DR with the second conversion, 2.4 V: 0xBA2E (p1015; ruled Q9); SetVoltage declares the level (ruled Q10)" \
        "passed overwrite && [ \$(( \$(readout overwrite ISR) & 0x14 )) = 20 ] && [ \"\$(readout overwrite DR)\" = 0x0000BA2E ]"
    fi ;;
  setvoltage)
    mapfile -t s < <(on 12 0)
    run_case setvoltage - 'sysbus.adcM1S2 SetVoltage 0 12' "${s[@]}" "$(start)" 'emulation RunFor "0.0001"' 'echo "R-DR"' "sysbus ReadDoubleWord 0x40022040"
    check "setvoltage: a channel declared 0 V by Renode's SetVoltage (the consumer's surface, d1-drv8876's) reads 0 with no record: a declared 0 is not an undeclared channel (ruled Q10)" \
      "passed setvoltage && [ \"\$(readout setvoltage DR)\" = 0x00000000 ]" ;;
  norevision)
    PLATFORM_SED='/^tviburiAdc12:$/,/^$/{/revision/d}' run_case norevision - "$clk" "$(w 0x308 0x30000)"
    check "norevision: a platform declaring no revision refuses at load, by name, TVI-ADC-CONFIG, unmodeled, and every access after (ruled Q2: never defaulted)" \
      "[ \$rc = 3 ] && records norevision 'recs[0][\"rule\"] == \"TVI-ADC-CONFIG\" and recs[0][\"bit\"] == \"revision\" and recs[0][\"access\"][\"offset\"] == \"0x0\" and \"no revision declared\" in recs[0][\"condition\"] and all(r[\"rule\"] == \"TVI-ADC-CONFIG\" for r in recs) and len(recs) == 2'" ;;
  badlevels)
    PLATFORM_SED='s/levels: "10:1.2"/levels: "10=1.2"/' run_case badlevels - "$clk" "$(w 0x308 0x30000)"
    check "badlevels: a levels property the guard cannot read refuses every access, TVI-ADC-CONFIG, unmodeled (ruled Q10)" \
      "[ \$rc = 3 ] && records badlevels 'len(recs) == 1 and recs[0][\"rule\"] == \"TVI-ADC-CONFIG\" and \"10=1.2\" in recs[0][\"condition\"]'" ;;
  registrations)
    run_case registrations - "$clk" 'sysbus LogPeripheralAccess sysbus.adcM1S2 true' "$(w 0x308 0x30000)" 'sysbus ReadDoubleWord 0x40022008' 'peripherals'
    check "registrations: peripherals lists adcM1S2 and adc3 as the guard, with their channel sources; the access log names the registers as the stock model's (CommonConfiguration, Control; measure.md §2.3 F)" \
      "passed registrations && grep -q 'adcM1S2 (GuardedSTM32H7_ADC)' '$work/registrations/renode.out' && grep -q 'adc3 (GuardedSTM32H7_ADC)' '$work/registrations/renode.out' \
       && grep -q 'adcM1S2-channel10 (ADCDefaultChannelSource)' '$work/registrations/renode.out' && [ \$(grep -c '(GuardedSTM32H7_ADC)' '$work/registrations/renode.out') = 2 ] \
       && grep -q 'adcM1S2: WriteUInt32 to 0x308 (CommonConfiguration), value 0x30000' '$work/registrations/renode-log.txt' && grep -q 'adcM1S2: ReadUInt32 from 0x8 (Control), returned 0x20000000' '$work/registrations/renode-log.txt'" ;;
  recorded)
    PLATFORM_SED=$rec run_case recorded - "$clk" "$(w 0x0C 0x400)" 'echo "R-CFGR"' 'sysbus ReadDoubleWord 0x4002200C' "$(w 0x08 0x30000000)"
    check "recorded: adcM1S2 recorded: CFGR.EXTEN = 01 records TVI-ADC-FIELD and passes on (RUL-19), reading 0x400; ADVREGEN with DEEPPWD = 1 records TVI-ADC-STATE; never a pass at the check" \
      "[ \$rc = 1 ] && logged recorded 'len(recs) == 2 and [r[\"rule\"] for r in recs] == [\"TVI-ADC-FIELD\", \"TVI-ADC-STATE\"] and all(r[\"tviburi\"] == \"would-refuse\" for r in recs)' && [ \"\$(readout recorded CFGR)\" = 0x00000400 ]" ;;
  rccreset)
    # ADC12RST (RCC_AHB1RSTR bit 5, §8.7.29 p432) resets ADC1/2: the guard's copies return to their reset values.
    run_case rccreset - "$clk" "$(w 0x308 0x30000)" "$(w 0x08 0)" 'echo "R-BEFORE"' 'sysbus ReadDoubleWord 0x40022308' \
      'sysbus WriteDoubleWord 0x58024480 0x20' 'sysbus WriteDoubleWord 0x58024480 0x0' 'echo "R-CCR"' 'sysbus ReadDoubleWord 0x40022308' 'echo "R-CR"' 'sysbus ReadDoubleWord 0x40022008'
    check "rccreset: CCR read 0x30000 from the copy; an ADC12RST pulse resets it to 0 and CR to 0x20000000 (DEEPPWD = 1, p1003), as silicon's registers reset" \
      "passed rccreset && [ \"\$(readout rccreset BEFORE)\" = 0x00030000 ] && [ \"\$(readout rccreset CCR)\" = 0x00000000 ] && [ \"\$(readout rccreset CR)\" = 0x20000000 ]" ;;
  unheld)
    PLATFORM=/repo/tests/1307/unheld.repl run_case unheld - "$clk" "$(w 0x308 0x30000)"
    check "unheld: an ADC guard not registered on the RCC guard refuses its access, TVI-ADC-CONFIG, unmodeled: unwatched, it cannot pass" \
      "[ \$rc = 3 ] && records unheld 'len(recs) == 1 and recs[0][\"rule\"] == \"TVI-ADC-CONFIG\" and \"not registered on an RCC guard\" in recs[0][\"condition\"]'" ;;
  query)
    q() { printf 'python "print(\\"Q \\" + str(self.Machine[\\"sysbus.rcc\\"].QueryKernelClock(\\"%s\\")))"' "$1"; }
    run_case query - "$clk" "$(q adc12)" "$pll2" "$(q adc12)" 'sysbus WriteDoubleWord 0x580244E0 0x1000000' "$(q adc3)"
    check "query (granted Q3): adc12 with ADCSEL = 00 (pll2_p_ck, PLL2 off) is modeled and not running; with PLL2 on, running; adc3 the same mux with ADC3EN" \
      "passed query && grep -q '^Q adc12: NOT running; .*RCC_D3CCIPR.ADCSEL = 00' '$work/query/renode.out' && grep -q '^Q adc12: running; pll2_p_ck' '$work/query/renode.out' && grep -q '^Q adc3: running; pll2_p_ck' '$work/query/renode.out'" ;;
  fields)
    # The allow-list (RUL-17) by enumeration, recorded: every field the table allows only at 0, written its widest value with the
    # ADC disabled, records one TVI-ADC-FIELD naming that field; each write is undone by its register's reset value.
    mapfile -t lines < <(python3 -I - "$src/tests/1307/data/rm0433-rev9-adc-fields.tsv" "$src/tests/1307/fields.py" <<'EOF'
import subprocess, sys, re
rows = subprocess.run([sys.executable, '-I', sys.argv[2], sys.argv[1]], capture_output=True, text=True, check=True).stdout.splitlines()
reset = {0x08: 0x20000000, 0x0C: 0x80000000, 0x24: 0x03FFFFFF, 0xB4: 0x03FFFFFF, 0xBC: 0x03FFFFFF}
for l in rows:
    m = re.match(r'\s*R\("(ADCx?_\w+)", (0x[0-9A-F]+), "[^"]*", "(\w+)", (\d+), (\d+), Zero,', l)
    if not m:
        continue
    off, msb, lsb = int(m.group(2), 16), int(m.group(4)), int(m.group(5))
    v = ((1 << (msb - lsb + 1)) - 1) << lsb
    print('sysbus WriteDoubleWord 0x%08X 0x%X' % (0x40022000 + off, v | reset.get(off, 0)))
    print('sysbus WriteDoubleWord 0x%08X 0x%X' % (0x40022000 + off, reset.get(off, 0)))
EOF
)
    PLATFORM_SED=$rec run_case fields - "$clk" "${lines[@]}"
    n=$(( ${#lines[@]} / 2 ))
    check "fields: every field allowed only at 0 ($n), written its widest value with the ADC disabled, records one TVI-ADC-FIELD naming it, unmodeled; nothing else is recorded" \
      "[ \$rc = 1 ] && [ $n -ge 60 ] && logged fields 'len(recs) == $n and all(r[\"rule\"] == \"TVI-ADC-FIELD\" and r[\"kind\"] == \"unmodeled\" for r in recs) and len({(r[\"register\"], r[\"bit\"].split(\" \")[0]) for r in recs}) == $n'" ;;
  ledger)
    mkdir -p "$work/ledger"
    rc=0; container -v "$src:/repo:ro,Z" -v "$work/ledger:/out:Z" "$IMAGE" sh -c '/repo/scripts/build-release /repo /out/release >/dev/null' </dev/null >"$work/ledger.stdout" 2>&1 || rc=$?
    check "ledger: the release carries the ADC guard's field rows over its 37 registers (ADC_CR's ADSTART, BOOST and ADCAL, CFGR's DMNGT and RES, CCR's CKMODE modeled; EXTEN, the injected group and CFGR2 0 only; Q6, Q7, Q14's acknowledgements named); the manifest restates adc3 and adcM1S2; the stock type's one model row, guarded, lists the two" \
      "[ \$rc = 0 ] && python3 -I -c '
import json, sys
rows = [json.loads(l) for l in open(sys.argv[1]).read().splitlines()[1:]]
g = \"Tviburi.GuardedSTM32H7_ADC\"
f = [r for r in rows if r.get(\"peripheral\") == g and r[\"row\"] == \"field\"]
by = {(r[\"register\"], r[\"field\"]): r for r in f}
ok = len({r[\"register\"] for r in f}) == 37 and all(by[(\"ADC_CR\", x)][\"state\"] == \"guard\" for x in (\"ADSTART\", \"BOOST\", \"ADCAL\"))
ok = ok and by[(\"ADC_CFGR\", \"DMNGT\")][\"state\"] == \"guard\" and by[(\"ADC_CFGR\", \"RES\")][\"allowed\"] == \"any\" and by[(\"ADCx_CCR\", \"CKMODE\")][\"state\"] == \"guard\"
ok = ok and by[(\"ADC_CFGR\", \"EXTEN\")][\"allowed\"] == [0] and by[(\"ADC_JSQR\", \"JL\")][\"allowed\"] == [0] and by[(\"ADC_CFGR2\", \"LSHIFT\")][\"allowed\"] == [0]
ok = ok and \"Q6\" in by[(\"ADCx_CCR\", \"VREFEN\")][\"behaves_as\"] and \"Q7\" in by[(\"ADC_CR\", \"BOOST\")][\"behaves_as\"] and \"Q14\" in by[(\"ADC_DR\", \"RDATA\")][\"behaves_as\"]
m = [r for r in rows if r[\"row\"] == \"model\" and r[\"peripheral\"] == \"Analog.STM32H7_ADC\"]
ok = ok and len(m) == 1 and m[0].get(\"guarded_by\") == g and sorted(m[0][\"instances\"]) == [\"adc3\", \"adcM1S2\"]
r = sorted(l.split()[2] for l in open(sys.argv[2]) if l.startswith(\"restate Analog.STM32H7_ADC \"))
ok = ok and r == [\"adc3\", \"adcM1S2\"]
sys.exit(0 if ok else 1)
' '$work/ledger/release/LEDGER.jsonl' '$work/ledger/release/MANIFEST'" ;;
  regen)
    rc=0; python3 -I "$src/tests/1307/fields.py" "$src/tests/1307/data/rm0433-rev9-adc-fields.tsv" > "$work/fields.cs" || rc=$?
    mkdir -p "$work/regen"
    container -v "$src:/repo:ro,Z" -v "$work/regen:/out:Z" "$IMAGE" sh -c 'awk -v sha=$(sha256sum /opt/renode/platforms/cpus/stm32h7.repl | cut -c1-64) \
      -f /repo/model/stm32h7-adc-restate.awk /opt/renode/platforms/cpus/stm32h7.repl > /out/restate.repl' </dev/null >"$work/regen.stdout" 2>&1 || rc=$?
    check "regen: the guard's Fields table regenerates from tests/1307/data by tests/1307/fields.py, and the restatement from the pinned stm32h7.repl, byte for byte (RUL-16)" \
      "[ \$rc = 0 ] && sed -n '/^        private static readonly Row\[\] Fields =$/,/^        };$/p' '$src/model/GuardedSTM32H7_ADC.cs' | sed '1,2d;\$d' | cmp -s - '$work/fields.cs' && cmp -s '$work/regen/restate.repl' '$src/model/stm32h7-adc-restate.repl'" ;;
  *) echo "unknown case: $t" >&2; exit 2 ;;
  esac
done
[ $fails -eq 0 ] && echo "ALL PASS" || echo "$fails FAILED"
[ $fails -eq 0 ]
