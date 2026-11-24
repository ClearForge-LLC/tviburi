#!/usr/bin/env bash
# tests/1302/timer.sh [SRC] — TVI-WO-1302's acceptance cases for the advanced-control timer guard, run on the host as
# tests/1301/gpio.sh is: each case one container in the pinned tools image (tests/spike.sh's IMAGE), no network, the
# source read-only. SRC defaults to a `git archive HEAD` export. tests/spike.sh runs each case as timer-<case> in its
# acceptance group.
#   TVIBURI_TESTS="pwm moe0 ..."  run only these cases.
#   TVIBURI_CI=1                  plain `podman run`, for a disposable CI runner only; elsewhere ~/bin/capped-run.
# Exit 0 only if every selected case passed. Pages: RM0433 Rev 9 §38.4 (TIM1/TIM8 registers), Table 337 p1608.
set -euo pipefail

here=$(cd "$(dirname "$0")/../.." && pwd)
IMAGE=$(sed -n 's/^IMAGE=//p' "$here/tests/spike.sh")
CASES="pwm moe0 cce0 force polarity ccrreset ccr0 ossi ccne ccnp lock1 lock2 lock3 lockfrozen locksame dmar half byte aoe read tim8 tim8on reenable lockbelow lockbelow2 halfhigh gen-pwm ap-m fields ledger registrations config recorded reset rccreset regen gconce"
GUARDS="spi1 rcc gpioPortA gpioPortB gpioPortC gpioPortD gpioPortE gpioPortF gpioPortG gpioPortH gpioPortI gpioPortJ gpioPortK timer1 timer8 dma1 dma2"
# The planted firmware's paths: every case but the monitor-driven ones.
PATHS=$(for c in $CASES; do case $c in fields|ledger|registrations|config|recorded|reset|rccreset|regen) ;; *) printf '"%s",' "$c" ;; esac; done | sed 's/,$//')
scratch=${TVIBURI_SCRATCH:-$HOME/.cache/tviburi}
mkdir -p "$scratch"
work=$(mktemp -d "$scratch/timer.XXXXXX")

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
# making the case's platform from the board, written to /out.
run_case() { # run_case <name> <firmware path or -> <monitor lines...>
  local name=$1 fw=$2; shift 2
  mkdir -p "$work/$name"
  sed -e 's#^using "\./#using "/repo/model/#' -e "${PLATFORM_SED:-$observers}" "$src/model/tviburi-nucleo-h753zi.repl" > "$work/$name/platform.repl"
  {
    sed "s#^machine LoadPlatformDescription .*#machine LoadPlatformDescription @/out/platform.repl#" "$src/scripts/prelude.resc"
    [ "$fw" = - ] || { echo "sysbus LoadELF @/out/fw.elf"; echo "cpu VectorTableOffset 0x08000000"; }
    echo "logLevel -1 sysbus.gpioPortA.pa8obs"
    echo "logLevel -1 sysbus.gpioPortC.pc6obs"
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
        --check-cfg "cfg(path, values($3))" -C link-arg=-T/repo/firmware/link.x -o /out/fw.elf /repo/firmware/timer.rs || exit 91
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
rises() { grep -c "${2:-pa8obs}: LED state changed to True" "$work/$1/renode.out" || true; }
passed() { [ "$rc" = 0 ] && grep -qx 'CHECK PASS' "$work/$1.stdout"; }
passes() { passed "$1" && after "$1"; }
refuses() { [ "$rc" = 3 ] && first "$1" "r[\"rule\"] == \"$2\" and r[\"kind\"] == \"$3\" and r[\"access\"][\"pc\"] != \"none\" and $4"; }
# The motor pins observed as the consumer's generated boards observe them (TVI-WO-1302.measure.md §2.4): an LED beside
# each pin's timer input; PC6 (TIM8_CH1, AF3) too.
observers='$a\
pa8obs: Miscellaneous.LED @ tviburiGpioA 8\
pc6obs: Miscellaneous.LED @ tviburiGpioC 6\
tviburiGpioA:\
    8 -> tviburiTimer1@00 | pa8obs@0\
tviburiGpioC:\
    6 -> timer3@00 | tviburiTimer8@00 | pc6obs@0'
marks=('echo "TVIBURI-MARK-AFTER"' 'sysbus ReadDoubleWord 0x20000104' 'echo "TVIBURI-MARK-READ"' 'sysbus ReadDoubleWord 0x20000108')
pins=('echo "R-PA8"' 'sysbus.gpioPortA.pa8obs State' 'echo "R-PC6"' 'sysbus.gpioPortC.pc6obs State')
clk='sysbus WriteDoubleWord 0x580244F0 0x3'   # TIM1EN, TIM8EN (p470): since TVI-WO-1300a an unclocked peripheral is locked
# The stock model's own CH1 line (its OCxREF, the guard's input), beside what the guard drives.
stock='python "g = self.Machine[\"sysbus.timer1\"]; print(\"R-STOCK1\"); print(g.GetType().BaseType.GetProperty(\"Connections\").GetValue(g)[0].IsSet)"'
readmark() { console "$1" | grep -A1 -x 'TVIBURI-MARK-READ' | tail -n1; }

for t in ${TVIBURI_TESTS:-$CASES}; do
  case $t in
  pwm|moe0|cce0|force|polarity|ccrreset|ccr0|ossi|locksame|half|tim8|tim8on|reenable|lockbelow|lockbelow2|halfhigh|gen-pwm|ap-m)
    span='"0.001"'; [ "$t" = gen-pwm ] && span='"0.003"'; [ "$t" = ap-m ] && span='"0.04"'
    extra=(); [ "$t" = polarity ] && extra=("$stock")
    run_case "$t" "$t" "emulation RunFor $span" "${marks[@]}" "${pins[@]}" "${extra[@]}"
    pa8=$(readout "$t" PA8); n=$(rises "$t")
    case $t in
    pwm) check "pwm: TIM1 CH1 PWM mode 1 with MOE = 1 drives PA8 as today (Table 337 p1608: OCx = OCxREF); CCMR1 reads 0x68 (OC1PE kept, p1602)" \
      "passes pwm && [ $n -gt 10 ] && [ \"\$(readmark pwm)\" = 0x00000068 ]" ;;
    moe0) check "moe0: the same PWM with MOE never set drives no pulse at PA8 or its observer: not driven (Hi-Z), shown low (Table 337 p1608; ruled Q4)" \
      "passes moe0 && [ $n = 0 ] && [ \"$pa8\" = False ]" ;;
    cce0) check "cce0: CC1E = 0 with CC1IE = 1 (the stock channel still runs): no pulse at PA8 (Table 337 p1608)" \
      "passes cce0 && [ $n = 0 ] && [ \"$pa8\" = False ]" ;;
    force) check "force: CC1E = 0 with OC1M = force active (the stock model sets its line): PA8 low (Table 337 p1608)" \
      "passes force && [ $n = 0 ] && [ \"$pa8\" = False ]" ;;
    polarity) check "polarity: CC1P = 1 with OCxREF held 1 (CCR1 > ARR, p1558): the stock line is high and PA8 low, OCx = OCxREF xor CCxP (p1608), applied once" \
      "passes polarity && [ \"$pa8\" = False ] && [ \"\$(readout polarity STOCK1)\" = True ]" ;;
    ccrreset) check "ccrreset: PWM mode 1 with CCR1 never written: CCR1 reads 0 (p1610) and OCxREF is held 0 (p1558): PA8 low" \
      "passes ccrreset && [ $n = 0 ] && [ \"$pa8\" = False ] && [ \"\$(readmark ccrreset)\" = 0x00000000 ]" ;;
    ccr0) check "ccr0: PA8 high (CCR1 > ARR), then CCR1 = 0: OCxREF held 0 (p1558), PA8 low" \
      "passes ccr0 && [ $n -ge 1 ] && [ \"$pa8\" = False ]" ;;
    ossi) check "ossi: MOE = 0, OSSI = 1, OIS1 = 1, CC1E = 1: PA8 at its idle level, high (p1615; Table 337 p1608)" \
      "passes ossi && [ \"$pa8\" = True ]" ;;
    locksame) check "locksame: under LOCK 3 the same CCER, CCMR1 and BDTR pass (RUL-18 amended), and clearing MOE (not locked) takes PA8 low" \
      "passes locksame && [ \"$pa8\" = False ]" ;;
    half) check "half: CCER written as a 16-bit store is merged and passed (p1588): PA8 toggles" \
      "passes half && [ $n -gt 10 ]" ;;
    tim8) check "tim8: TIM8 CH1 PWM on PC6 (AF3) with MOE never set: no pulse at PC6; TIM8 is guarded too" \
      "passes tim8 && [ \$(rises tim8 pc6obs) = 0 ] && [ \"\$(readout tim8 PC6)\" = False ]" ;;
    tim8on) check "tim8on: the same with MOE = 1: PC6 toggles, so TIM8's routes land on the guard's outputs" \
      "passes tim8on && [ \$(rises tim8on pc6obs) -gt 10 ]" ;;
    reenable) check "reenable: force active, CC1E = 1, 0, then 1 again: PA8 high again, OCxREF not depending on CCxE (p1607; Table 337 p1608)" \
      "passes reenable && [ \"$pa8\" = True ]" ;;
    lockbelow) check "lockbelow: under LOCK 1, a CC1P change, an OSSI change and an OC1M change pass: each is locked only above level 1 (p1615)" \
      "passes lockbelow" ;;
    lockbelow2) check "lockbelow2: under LOCK 2, an OC1M change passes: OCxM is locked only at level 3 (p1615)" \
      "passes lockbelow2" ;;
    halfhigh) check "halfhigh: a 16-bit store of 1 to CCMR3's upper half (0x56) is merged into bit 16 (p1588, p1617): CCMR3 reads 0x00010000, PA8 toggles" \
      "passes halfhigh && [ $n -gt 10 ] && [ \"\$(readmark halfhigh)\" = 0x00010000 ]" ;;
    gen-pwm) check "gen-pwm: motors-pwm's TIM1 writes (Aethelgard 65c74788) pass, enforcing, and PA8 pulses" \
      "passes gen-pwm && [ $n -ge 1 ]" ;;
    ap-m) check "ap-m: unmodified ArduPilot's TIM1 writes with motor outputs (CCMR3 included, no CCRx) pass, enforcing; PA8 stays low for 40 ms, silicon's level (p1610, p1558; ruled Q2), where the stock model holds it high from one period on" \
      "passes ap-m && [ $n = 0 ] && [ \"$pa8\" = False ]" ;;
    esac ;;
  ccne|ccnp|lock1|lock2|lock3|lockfrozen|dmar|byte|aoe|read)
    run_case "$t" "$t" 'emulation RunFor "0.001"'
    case $t in
    ccne) check "ccne: CC1NE = 1 refuses TVI-TIM-FIELD, unmodeled (ruled Q5 (a)), at the CCER store" "refuses ccne TVI-TIM-FIELD unmodeled 'r[\"bit\"].startswith(\"TIMx_CCER.CC1NE (\")'" ;;
    ccnp) check "ccnp: CC1NP = 1 refuses TVI-TIM-FIELD, unmodeled" "refuses ccnp TVI-TIM-FIELD unmodeled 'r[\"bit\"].startswith(\"TIMx_CCER.CC1NP (\")'" ;;
    lock1) check "lock1: under LOCK 1, a CR2.OIS1 change refuses TVI-TIM-LOCK, predicted-fault (p1615)" "refuses lock1 TVI-TIM-LOCK predicted-fault 'r[\"register\"] == \"TIMx_CR2\" and \"p1615\" in r[\"citation\"]'" ;;
    lock2) check "lock2: under LOCK 2, a CCER.CC1P change refuses TVI-TIM-LOCK, predicted-fault (p1607, p1615)" "refuses lock2 TVI-TIM-LOCK predicted-fault 'r[\"bit\"] == \"TIMx_CCER bits 0x00000002 under LOCK level 2\" and \"p1607\" in r[\"citation\"]'" ;;
    lock3) check "lock3: under LOCK 3, a CCMR1.OC1M change refuses TVI-TIM-LOCK, predicted-fault (p1602, p1615)" "refuses lock3 TVI-TIM-LOCK predicted-fault 'r[\"register\"] == \"TIMx_CCMR1\"'" ;;
    lockfrozen) check "lockfrozen: LOCK is frozen by the first BDTR write (p1615): a second write changing it refuses TVI-TIM-LOCK" "refuses lockfrozen TVI-TIM-LOCK predicted-fault 'r[\"bit\"] == \"TIMx_BDTR bits 0x00000100 under LOCK level 0\" and \"frozen\" in r[\"condition\"]'" ;;
    dmar) check "dmar: a DMAR write refuses TVI-TIM-DMAR, unmodeled (p1616)" "refuses dmar TVI-TIM-DMAR unmodeled 'True'" ;;
    byte) check "byte: an 8-bit CCER store refuses TVI-TIM-WIDTH, unmodeled (p1588)" "refuses byte TVI-TIM-WIDTH unmodeled 'r[\"access\"][\"width\"] == 8'" ;;
    aoe) check "aoe: AOE = 1 refuses TVI-TIM-FIELD, unmodeled" "refuses aoe TVI-TIM-FIELD unmodeled 'r[\"bit\"].startswith(\"TIMx_BDTR.AOE (\")'" ;;
    read) check "read: a read at 0x50 (no register, p1628) refuses TVI-TIM-READ, unmodeled" "refuses read TVI-TIM-READ unmodeled 'r[\"bit\"] == \"no register at offset 0x50\"'" ;;
    esac ;;
  fields)
    # Gate clause 6 (RUL-17) for TIM1, the cases generated from the release's ledger: each field row's disallowed
    # value refuses TVI-TIM-FIELD, unmodeled, naming the field; each allowed value passes; every offset with no row
    # refuses. DMAR's offset answers TVI-TIM-DMAR first.
    mkdir -p "$work/fields-cases"
    container -v "$src:/repo:ro,Z" -v "$work/fields-cases:/out:Z" "$IMAGE" sh -c '/repo/scripts/build-release >/dev/null &&
      /out/release.target/debug/tviburi-release cases /out/release/LEDGER.jsonl Tviburi.GuardedSTM32_Timer 0x40010000 0x400 0x580244F0 0x3 >/out/cases.txt' </dev/null >"$work/fields-cases.stdout" 2>&1
    mapfile -t lines < "$work/fields-cases/cases.txt"
    run_case fields - "${lines[@]}"
    check "fields: every ledger row's disallowed value refuses by name, unmodeled; allowed values pass; no-register offsets refuse (gate clause 6)" \
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
        bad.append(('not one record per case', r['bit'])); continue
    seen[k] = r
    kind, name = cases[k]
    if k[0] == 0x4C:
        if r['rule'] != 'TVI-TIM-DMAR': bad.append(('dmar', r['rule']))
    elif kind == 'pass' or r['rule'] != 'TVI-TIM-FIELD' or r['kind'] != 'unmodeled':
        bad.append((kind, name, r['rule'], r['bit']))
    elif not (r['bit'].startswith(name + ' (') if name != 'none' else r['bit'] == 'no register at offset 0x%X' % k[0]):
        bad.append(('wrong field', name, r['bit']))
missing = [v for k, v in cases.items() if v[0] == 'refuse' and k not in seen]
print(len(cases), 'cases;', len(recs), 'refusals;', 'bad', bad[:5], 'missing', missing[:5])
sys.exit(0 if not bad and not missing and len(cases) > 300 else 1)
EOF" ;;
  ledger)
    mkdir -p "$work/ledger"
    rc=0; container -v "$src:/repo:ro,Z" -v "$work/ledger:/out:Z" "$IMAGE" sh -c '/repo/scripts/build-release /repo /out/release >/dev/null' </dev/null >"$work/ledger.stdout" 2>&1 || rc=$?
    check "ledger: the release carries the timer guard's 225 field rows over its 25 registers (DMAR has none: TVI-TIM-DMAR), with the period and the first edge as stock-wrong rows citing p1558, Hi-Z shown low stated (ruled Q1, Q4), and bench rows T1 and T2 with the (rule, bit) lock2 and lockfrozen emit (RUL-18)" \
      "[ \$rc = 0 ] && python3 -I -c '
import json, sys
rows = [json.loads(l) for l in open(sys.argv[1]).read().splitlines()[1:]]
f = [r for r in rows if r.get(\"peripheral\") == \"Tviburi.GuardedSTM32_Timer\" and r[\"row\"] == \"field\"]
by = {(r[\"register\"], r[\"field\"]): r for r in f}
ok = len(f) == 225 and len({r[\"register\"] for r in f}) == 25 and not any(r[\"offset\"] == \"0x4C\" for r in f)
ok = ok and by[(\"TIMx_ARR\", \"ARR[15:0]\")][\"state\"] == \"stock-wrong\" and \"p1558\" in by[(\"TIMx_ARR\", \"ARR[15:0]\")][\"detail\"]
ok = ok and by[(\"TIMx_CR1\", \"CEN\")][\"state\"] == \"stock-wrong\" and \"p1558\" in by[(\"TIMx_CR1\", \"CEN\")][\"detail\"]
ok = ok and \"driven low\" in by[(\"TIMx_CCER\", \"CC1E\")][\"behaves_as\"] and by[(\"TIMx_CCER\", \"CC1NE\")][\"allowed\"] == [0]
bench = sorted((r[\"field\"], r[\"rule\"], r[\"bit\"]) for r in rows if r.get(\"peripheral\") == \"Tviburi.GuardedSTM32_Timer\" and r[\"row\"] == \"bench\")
ok = ok and bench == [(\"T1\", \"TVI-TIM-LOCK\", \"TIMx_CCER bits 0x00000002 under LOCK level 2\"), (\"T2\", \"TVI-TIM-LOCK\", \"TIMx_BDTR bits 0x00000100 under LOCK level 0\")]
sys.exit(0 if ok else 1)
' '$work/ledger/release/LEDGER.jsonl'" ;;
  registrations)
    run_case registrations - "$clk" 'peripherals' 'python "g = self.Machine[\"sysbus.timer1\"]; print(\"OUT0 \" + str(g.Connections[0].Endpoints.Count) + \" STOCK0 \" + str(g.GetType().BaseType.GetProperty(\"Connections\").GetValue(g)[0].Endpoints.Count))"'
    check "registrations: peripherals lists timer1 and timer8 as the guard; TIM1 CH1's 7 routes (stm32h7.repl L584) and the observer's are on the guard's output, and the stock line feeds only the guard's tap" \
      "passed registrations && grep -q 'timer1 (GuardedSTM32_Timer)' '$work/registrations/renode.out' && grep -q 'timer8 (GuardedSTM32_Timer)' '$work/registrations/renode.out' && tr -d '\\r' < '$work/registrations/renode.out' | grep -qx 'OUT0 7 STOCK0 1'" ;;
  config)
    PLATFORM_SED='$a\
tviburiTimer8:\
    initialLimit: 0xffffffff' run_case config - "$clk" 'sysbus ReadDoubleWord 0x40010400'
    check "config: a timer guard given a 32-bit initialLimit refuses every access, TVI-TIM-CONFIG, unmodeled (TIM1/TIM8 are 16-bit, p1609)" \
      "[ \$rc = 3 ] && first config 'r[\"rule\"] == \"TVI-TIM-CONFIG\" and r[\"kind\"] == \"unmodeled\" and r[\"peripheral\"] == \"sysbus.timer8\"'" ;;
  recorded)
    PLATFORM_SED="$observers"'
/^tviburiTimer1:$/{n;s/enforcing/recorded/}' run_case recorded - "$clk" 'sysbus WriteDoubleWord 0x4001004C 0x0' 'sysbus WriteDoubleWord 0x40010044 0x4000' 'echo "R-BDTR"' 'sysbus ReadDoubleWord 0x40010044'
    check "recorded: timer1 recorded: a DMAR write and AOE = 1 each record a would-refuse and pass on (RUL-19); BDTR reads 0x4000; never a pass at the check" \
      "[ \$rc = 1 ] && grep -c '\"tviburi\":\"would-refuse\".*TVI-TIM-' '$work/recorded/renode-log.txt' | grep -qx 2 && [ \"\$(readout recorded BDTR)\" = 0x00004000 ]" ;;
  reset)
    run_case reset - "$clk" 'sysbus WriteDoubleWord 0x40010044 0x8300' 'machine Reset' "$clk" 'sysbus WriteDoubleWord 0x40010018 0x58' 'sysbus WriteDoubleWord 0x40010044 0x8100' 'echo "R-BDTR"' 'sysbus ReadDoubleWord 0x40010044' 'echo "R-CCR1"' 'sysbus ReadDoubleWord 0x40010034'
    check "reset: LOCK 3, then machine Reset: LOCK clears (p1615: frozen until the next reset), so a CCMR1 change and a new LOCK pass; CCR1 reads 0 (p1610)" \
      "passed reset && [ \"\$(readout reset BDTR)\" = 0x00008100 ] && [ \"\$(readout reset CCR1)\" = 0x00000000 ]" ;;
  rccreset)
    # TIM1RST (RCC_APB2RSTR bit 0, RM0433 Rev 9 §8.7.35 p442), set then cleared through the RCC guard, as rcc-reset does.
    run_case rccreset - "$clk" 'sysbus WriteDoubleWord 0x40010020 0x1' 'sysbus WriteDoubleWord 0x40010044 0x8300' \
      'sysbus WriteDoubleWord 0x58024498 0x1' 'sysbus WriteDoubleWord 0x58024498 0x0' 'echo "R-CCER"' 'sysbus ReadDoubleWord 0x40010020' \
      'echo "R-BDTR"' 'sysbus ReadDoubleWord 0x40010044' 'sysbus WriteDoubleWord 0x40010044 0x8100' 'sysbus WriteDoubleWord 0x40010020 0x3'
    check "rccreset: TIM1RST through the RCC resets the guard: CCER and BDTR read 0, LOCK clears, so a new LOCK and a CC1P change pass (p1615: frozen until the next reset)" \
      "passed rccreset && [ \"\$(readout rccreset CCER)\" = 0x00000000 ] && [ \"\$(readout rccreset BDTR)\" = 0x00000000 ]" ;;
  gconce)
    # The load-time collection (ruled 10:30Z (A), 10:55Z): once per instance at the machine's first start; a pause and
    # resume, and a machine Reset followed by a start, do not collect again.
    n='python "print(\"R-GC %d %d\" % (self.Machine[\"sysbus.timer1\"].Collections, self.Machine[\"sysbus.timer8\"].Collections))"'
    run_case gconce pwm "$n" 'emulation RunFor "0.0001"' "$n" 'emulation RunFor "0.0001"' "$n" 'machine Reset' 'emulation RunFor "0.0001"' "$n"
    check "gconce: each timer guard forces one full collection, at the machine's first start: 0 0 before it, 1 1 after it, after a pause and resume, and after a reset and a start" \
      "[ \"\$(tr -d '\\r' < '$work/gconce/renode.out' | grep -o '^R-GC [0-9]* [0-9]*' | tr '\\n' ,)\" = 'R-GC 0 0,R-GC 1 1,R-GC 1 1,R-GC 1 1,' ]" ;;
  regen)
    rc=0; python3 -I "$src/tests/1302/fields.py" "$src/tests/1302/data/rm0433-rev9-tim18-fields.tsv" > "$work/fields.cs" || rc=$?
    check "regen: the guard's Fields table regenerates from tests/1302/data by tests/1302/fields.py, byte for byte (RUL-16)" \
      "[ \$rc = 0 ] && sed -n '/^        private static readonly Row\[\] Fields =$/,/^        };$/p' '$src/model/GuardedSTM32_Timer.cs' | sed '1,2d;\$d' | cmp -s - '$work/fields.cs'" ;;
  *) echo "unknown case: $t" >&2; exit 2 ;;
  esac
done

echo "$fails failed"
[ $fails = 0 ]
