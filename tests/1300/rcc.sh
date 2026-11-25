#!/usr/bin/env bash
# tests/1300/rcc.sh [SRC] — TVI-WO-1300's acceptance suite for the RCC guard, run on the host, as tests/spike.sh is.
# SRC defaults to a `git archive HEAD` export, so what is tested is what is committed. Each case is one container in
# the pinned tools image (tests/spike.sh's IMAGE), with no network and the source mounted read-only.
#   TVIBURI_TESTS="scar clocked ..."  run only these cases.
#   TVIBURI_CI=1                      plain `podman run`, for a disposable CI runner only; elsewhere ~/bin/capped-run.
# Exit 0 only if every selected case passed.
#
# TVI-WO-1300a: SPI1's guard asks the RCC guard's query at its own start (TVI-SPI-KERNEL) and for its enable at every
# access (TVI-SPI-ENABLE); the RCC guard refuses a write that stops a running SPI1's kernel clock (TVI-RCC-CLOCK). USART2
# has no guard yet, so its case still asks the query through a test-only hook (a hook is not a guard, RUL-07).
set -euo pipefail

here=$(cd "$(dirname "$0")/../.." && pwd)
IMAGE=$(sed -n 's/^IMAGE=//p' "$here/tests/spike.sh")
CASES="scar switch stuck clocked switched alias field pll divq pllref divm0 sw width read usart2 uncovered fields reset resetfwd spi1rst alias17c registrations hse16 recorded recordedfield ardupilot gen-s03 gen-hc16 oscclear oscsys rsrbyte misaligned ledger unenabled tim2 lost-pll lost-sel lost-en lost-osc lost-ckper stopped ckper-live blocked cfg2same sel5 divr pll1src kernelq spi1off cstart dma lost-alias adc3 unheld relost-sel relost-pll divr2rmw usart-lost usart-stopped usart-lost-en usart-othersel usart-unheld hse-none hse-declared adc rtcen rtcen-reset hse-zero"
scratch=${TVIBURI_SCRATCH:-$HOME/.cache/tviburi}
mkdir -p "$scratch"
work=$(mktemp -d "$scratch/rcc.XXXXXX")

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
# script, then the check on the log with both guarded sources. Outputs in $work/<name>.
run_case() { # run_case <name> <firmware path or -> <monitor lines...>
  local name=$1 fw=$2; shift 2
  mkdir -p "$work/$name"
  {
    if [ -n "${PLATFORM_SED:-}" ]; then   # the board with a sed script applied (TVI-WO-1300b; as tests/1305/usart.sh)
      sed -e 's#^using "\./#using "/repo/model/#' -e "$PLATFORM_SED" "$src/model/tviburi-nucleo-h753zi.repl" > "$work/$name/platform.repl"
      sed "s#^machine LoadPlatformDescription .*#machine LoadPlatformDescription @/out/platform.repl#" "$src/scripts/prelude.resc"
    elif [ -n "${PLATFORM:-}" ]; then   # a planted platform in place of the prelude's board
      sed "s#^machine LoadPlatformDescription .*#machine LoadPlatformDescription @$PLATFORM#" "$src/scripts/prelude.resc"
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
        --check-cfg "cfg(path, values(\"clocked\",\"scar\",\"switch\",\"stuck\",\"switched\",\"alias\",\"field\",\"pll\",\"divq\",\"pllref\",\"divm0\",\"sw\",\"width\",\"read\",\"usart2\",\"ardupilot\",\"gen-s03\",\"gen-hc16\",\"unenabled\",\"lost-pll\",\"lost-sel\",\"lost-en\",\"lost-osc\",\"lost-ckper\",\"stopped\",\"ckper-live\",\"blocked\",\"cfg2same\",\"sel5\",\"divr\",\"usart-lost\",\"usart-stopped\",\"usart-lost-en\",\"usart-othersel\",\"hse-on\",\"adc\",\"rtcen\"))" \
        -C link-arg=-T/repo/firmware/link.x -o /out/fw.elf /repo/firmware/rcc.rs || exit 91
    fi
    timeout 120 renode --disable-gui --console --plain /out/run.resc </dev/null >/out/renode.out 2>&1
    echo "renode-exit=$?"
    /out/target/debug/tviburi-check "$v" /out/renode-log.txt spi1 rcc gpioPortA gpioPortB gpioPortC gpioPortD gpioPortE gpioPortF gpioPortG gpioPortH gpioPortI gpioPortJ gpioPortK timer1 timer8 dma1 dma2 flashController usart1 usart2 usart3 uart4 uart5 usart6 uart7 uart8 i2c1 i2c2 i2c3 adcM1S2 adc3
  ' _ "$fw" </dev/null >"$work/$name.stdout" 2>&1 || rc=$?
  echo "--- $name ($fw): exit $rc"; grep -E '^(CHECK|PROBLEM)' "$work/$name.stdout" || true
}

records() { # records <name> <python expr over `recs`, the check's REFUSAL lines parsed>
  python3 -I -c '
import json, sys
recs = [json.loads(l[8:]) for l in open(sys.argv[1]) if l.startswith("REFUSAL ")]
sys.exit(0 if eval(sys.argv[2]) else 1)
' "$work/$1.stdout" "$2"
}

console() { "$src/scripts/monitor-output" "$work/$1/renode.out"; }

run='emulation RunFor "0.001"'
after='sysbus ReadDoubleWord 0x20000104'

rule_bits() { # rule_bits <name>: each refusal's rule and bit, one per line
  python3 -I -c 'import json, sys; print("\n".join(r["rule"] + " " + r["bit"] for r in (json.loads(l[8:]) for l in open(sys.argv[1]) if l.startswith("REFUSAL "))))' "$work/$1.stdout"
}

first_kernel() { # first_kernel <name> <python expr over r, the first refusal>
  records "$1" "len(recs) >= 1 and (lambda r: $2)(recs[0])"
}

for t in ${TVIBURI_TESTS:-$CASES}; do
  case $t in
  scar)
    run_case scar scar "$run"
    check "scar: SPI1 started with SPI123SEL on pll1_q_ck and PLL1 off refuses at SPI1 (TVI-SPI-KERNEL), predicted-fault, naming the selector, the source and the pages" \
      "[ \$rc = 3 ] && first_kernel scar 'r[\"rule\"] == \"TVI-SPI-KERNEL\" and r[\"peripheral\"] == \"sysbus.spi1\" and r[\"kind\"] == \"predicted-fault\" and r[\"bit\"] == \"RCC_D2CCIP1R.SPI123SEL = 000\" and \"pll1_q_ck\" in r[\"condition\"] and \"PLL1ON = 0\" in r[\"condition\"] and \"p414\" in r[\"citation\"] and \"p371-372\" in r[\"citation\"] and r[\"access\"][\"pc\"] != \"none\"'" ;;
  switch)
    run_case switch switch "$run"
    check "switch: SPI123SEL to pll2_p_ck with PLL2 off: the mux cannot switch, predicted-fault (p371)" \
      "[ \$rc = 3 ] && first_kernel switch 'r[\"rule\"] == \"TVI-SPI-KERNEL\" and r[\"kind\"] == \"predicted-fault\" and \"has not switched from pll1_q_ck\" in r[\"condition\"] and \"pll2_p_ck\" in r[\"condition\"]'" ;;
  stuck)
    run_case stuck stuck "$run"
    check "stuck: SPI123SEL to per_ck (running) while the input pll1_q_ck never ran: no clock, predicted-fault (p371-372)" \
      "[ \$rc = 3 ] && first_kernel stuck 'r[\"rule\"] == \"TVI-SPI-KERNEL\" and r[\"kind\"] == \"predicted-fault\" and r[\"bit\"] == \"RCC_D2CCIP1R.SPI123SEL = 100\" and \"has not switched from pll1_q_ck\" in r[\"condition\"] and \"per_ck\" in r[\"condition\"]'" ;;
  clocked)
    run_case clocked clocked "$run" 'echo "TVIBURI-MARK-AFTER"' "$after"
    check "clocked: PLL1 started, SPI1 starts on pll1_q_ck: a pass, the firmware past its last write" \
      "[ \$rc = 0 ] && grep -qx 'CHECK PASS' '$work/clocked.stdout' && console clocked | grep -A1 -x 'TVIBURI-MARK-AFTER' | tail -n1 | grep -qx '0xAF7E0002'" ;;
  switched)
    run_case switched switched "$run" 'echo "TVIBURI-MARK-AFTER"' "$after"
    check "switched: PLL1 running, then SPI123SEL to per_ck: the mux switches, a pass" \
      "[ \$rc = 0 ] && grep -qx 'CHECK PASS' '$work/switched.stdout' && console switched | grep -A1 -x 'TVIBURI-MARK-AFTER' | tail -n1 | grep -qx '0xAF7E0002'" ;;
  alias)
    run_case alias alias "$run" 'echo "TVIBURI-MARK-AFTER"' "$after"
    check "alias: SPI1EN through RCC_C1_APB2ENR (0x150) is the same register (p382): a pass" \
      "[ \$rc = 0 ] && grep -qx 'CHECK PASS' '$work/alias.stdout' && console alias | grep -A1 -x 'TVIBURI-MARK-AFTER' | tail -n1 | grep -qx '0xAF7E0002'" ;;
  field)
    run_case field field "$run"
    check "field: SPI123SEL = 011 (I2S_CKIN, not modeled) refuses, TVI-RCC-FIELD, unmodeled" \
      "[ \$rc = 3 ] && first_kernel field 'r[\"rule\"] == \"TVI-RCC-FIELD\" and r[\"kind\"] == \"unmodeled\" and \"SPI123SEL\" in r[\"bit\"]'" ;;
  pll)
    run_case pll pll "$run"
    check "pll: DIVQ1EN cleared while PLL1 is on refuses, TVI-RCC-PLL, predicted-fault (p401)" \
      "[ \$rc = 3 ] && first_kernel pll 'r[\"rule\"] == \"TVI-RCC-PLL\" and r[\"bit\"] == \"DIVP1EN/DIVQ1EN/DIVR1EN\" and r[\"kind\"] == \"predicted-fault\" and \"p400-401\" in r[\"citation\"]'" ;;
  fields)
    mapfile -t lines < <(grep -v '^#' "$src/tests/1300/fields.resc")
    run_case fields - "${lines[@]}"
    n=$(grep -c '^# FIELD ' "$src/tests/1300/fields.resc")
    check "every field outside the allow-list refuses by name, TVI-RCC-FIELD, unmodeled ($n cases; gate clause 6)" \
      "records fields 'len(recs) == $n and all(r[\"rule\"] == \"TVI-RCC-FIELD\" and r[\"kind\"] == \"unmodeled\" and r[\"bit\"].startswith(f) for r, f in zip(recs, [l[8:].strip() for l in open(\"$src/tests/1300/fields.resc\") if l.startswith(\"# FIELD \")]))'" ;;
  reset)
    run_case reset - \
      'sysbus WriteDoubleWord 0x580244F0 0x1001' 'sysbus WriteDoubleWord 0x40010028 0x5' 'echo "R-TIM1-BEFORE"' 'sysbus ReadDoubleWord 0x40010028' \
      'sysbus WriteDoubleWord 0x58024498 0x1' 'sysbus WriteDoubleWord 0x58024498 0x0' 'echo "R-TIM1-AFTER"' 'sysbus ReadDoubleWord 0x40010028' \
      'sysbus SetHookBeforePeripheralWrite sysbus.rcc "print(\"R-HOOK\")"' 'sysbus LogPeripheralAccess sysbus.rcc true' \
      'sysbus WriteDoubleWord 0x58024454 0x00000100' 'echo "R-AFTER-HOOK"' 'sysbus ReadDoubleWord 0x58024454' \
      'sysbus WriteDoubleWord 0x580244F0 0x0' 'sysbus WriteDoubleWord 0x40010028 0x7' 'echo "R-TIM1-LOCKED"' 'sysbus ReadDoubleWord 0x40010028'
    check "reset: TIM1RST through the guard resets TIM1 (PSC 5 -> 0; RM0433 Rev 9 §8.7.35 p442); a hook and logging on the guard do not displace it; TIM1 with TIM1EN cleared locks (reads 0, the write dropped)" \
      "[ \$rc = 0 ] && console reset | grep -A1 -x 'R-TIM1-BEFORE' | tail -n1 | grep -qx '0x00000005' && console reset | grep -A1 -x 'R-TIM1-AFTER' | tail -n1 | grep -qx '0x00000000' && console reset | grep -A1 -x 'R-AFTER-HOOK' | tail -n1 | grep -qx '0x00000100' && grep -q 'R-HOOK' '$work/reset/renode.out' && console reset | grep -A1 -x 'R-TIM1-LOCKED' | tail -n1 | grep -qx '0x00000000' && grep -q 'Tried to write a locked peripheral: machine-0.timer1' '$work/reset/renode.out'" ;;
  divq)
    run_case divq divq "$run"
    check "divq: PLL1 started with DIVQ1EN = 0: pll1_q_ck is disabled, SPI1 starts with no kernel clock, predicted-fault (p401)" \
      "[ \$rc = 3 ] && first_kernel divq 'r[\"rule\"] == \"TVI-SPI-KERNEL\" and r[\"kind\"] == \"predicted-fault\" and \"DIVQ1EN = 0\" in r[\"condition\"]'" ;;
  pllref)
    run_case pllref pllref "$run"
    check "pllref: PLL1ON with PLLSRC = 11 (no clock) refuses, TVI-RCC-PLL-REF, unmodeled, at the firmware's store" \
      "[ \$rc = 3 ] && first_kernel pllref 'r[\"rule\"] == \"TVI-RCC-PLL-REF\" and r[\"kind\"] == \"unmodeled\" and r[\"access\"][\"pc\"] != \"none\"'" ;;
  divm0)
    run_case divm0 divm0 "$run"
    check "divm0: PLL1ON with DIVM1 = 0 (prescaler disabled, p398-399) refuses, TVI-RCC-PLL-REF, unmodeled" \
      "[ \$rc = 3 ] && first_kernel divm0 'r[\"rule\"] == \"TVI-RCC-PLL-REF\" and \"DIVM1 = 0\" in r[\"condition\"]'" ;;
  sw)
    run_case sw sw "$run"
    check "sw: SW = PLL1 with PLL1 off refuses, TVI-RCC-SW, unmodeled (p350)" \
      "[ \$rc = 3 ] && first_kernel sw 'r[\"rule\"] == \"TVI-RCC-SW\" and r[\"kind\"] == \"unmodeled\" and \"p350\" in r[\"citation\"]'" ;;
  width)
    run_case width width "$run"
    check "width: a 16-bit RCC store refuses, TVI-RCC-WIDTH, unmodeled, at the firmware's store" \
      "[ \$rc = 3 ] && first_kernel width 'r[\"rule\"] == \"TVI-RCC-WIDTH\" and r[\"kind\"] == \"unmodeled\" and r[\"access\"][\"width\"] == 16 and r[\"access\"][\"pc\"] != \"none\"'" ;;
  read)
    run_case read read "$run"
    check "read: a read at an RCC offset with no register (0x014; Table 84 p496) refuses, TVI-RCC-READ, unmodeled" \
      "[ \$rc = 3 ] && first_kernel read 'r[\"rule\"] == \"TVI-RCC-READ\" and r[\"kind\"] == \"unmodeled\" and r[\"access\"][\"pc\"] != \"none\"'" ;;
  usart2)
    run_case usart2 usart2 'sysbus SetHookBeforePeripheralWrite sysbus.usart2 "offset == 0 and (value & 1) and machine[\"sysbus.rcc\"].RequireKernelClock(\"usart2\", \"CR1.UE\")"' "$run"
    check "usart2: USART234578SEL to pll2_q_ck with PLL2 off: USART2 starts with no kernel clock, predicted-fault (p417; F7)" \
      "[ \$rc = 3 ] && first_kernel usart2 'r[\"rule\"] == \"TVI-RCC-KERNEL\" and r[\"kind\"] == \"predicted-fault\" and \"USART234578SEL\" in r[\"bit\"] and \"p417\" in r[\"citation\"]'" ;;
  uncovered)
    run_case uncovered - 'python "self.Machine[\"sysbus.rcc\"].RequireKernelClock(\"spi4\", \"CR1.SPE\")"'
    check "uncovered: the query asked for a peripheral it does not cover (spi4) refuses, TVI-RCC-KERNEL, unmodeled" \
      "[ \$rc = 3 ] && first_kernel uncovered 'r[\"rule\"] == \"TVI-RCC-KERNEL\" and r[\"kind\"] == \"unmodeled\"'" ;;
  resetfwd)
    q='python "print(\"Q \" + str(self.Machine[\"sysbus.rcc\"].QueryKernelClock(\"spi1\")))"'
    run_case resetfwd - \
      'sysbus WriteDoubleWord 0x58024430 0x0103038F' 'sysbus WriteDoubleWord 0x58024400 0x01000001' \
      'sysbus WriteDoubleWord 0x58024450 0x00004000' 'sysbus WriteDoubleWord 0x580244F0 0x00001000' "$q" 'echo "R-COPY"' 'sysbus ReadDoubleWord 0x58024450' \
      'machine Reset' 'echo "R-AFTER-RESET"' 'sysbus ReadDoubleWord 0x58024450' 'sysbus ReadDoubleWord 0x58024400' \
      'sysbus WriteDoubleWord 0x580244F0 0x00001000' "$q"
    check "resetfwd: passed writes, then machine Reset: the copies and the stock RCC return to reset (D2CCIP1R 0, PLL1 off), and the mux keeps its input per_ck, so with SPI123SEL back at 000 and PLL1 off SPI1 has no clock (p371-372)" \
      "[ \$rc = 0 ] && console resetfwd | grep -A1 -x 'R-COPY' | tail -n1 | grep -qx '0x00004000' && grep -q '^Q spi1: running; per_ck: CKPERSEL = 00' '$work/resetfwd/renode.out' && console resetfwd | grep -A2 -x 'R-AFTER-RESET' | sed -n 2p | grep -qx '0x00000000' && grep -q '^Q spi1: NOT running; the mux has not switched from per_ck' '$work/resetfwd/renode.out'" ;;
  spi1rst)
    run_case spi1rst - 'sysbus WriteDoubleWord 0x580244F0 0x00001000' 'sysbus WriteDoubleWord 0x40013004 0x00000004' 'echo "R-CR2-BEFORE"' 'sysbus ReadDoubleWord 0x40013004' \
      'sysbus WriteDoubleWord 0x58024498 0x00001000' 'sysbus WriteDoubleWord 0x58024498 0x0' 'echo "R-CR2-AFTER"' 'sysbus ReadDoubleWord 0x40013004'
    check "spi1rst: SPI1RST through the guard resets SPI1 (CR2.TSIZE 4 -> 0; RM0433 Rev 9 §8.7.35 p443)" \
      "[ \$rc = 0 ] && console spi1rst | grep -A1 -x 'R-CR2-BEFORE' | tail -n1 | grep -qx '0x00000004' && console spi1rst | grep -A1 -x 'R-CR2-AFTER' | tail -n1 | grep -qx '0x00000000'" ;;
  recorded)
    PLATFORM=/repo/tests/1300/recorded.repl run_case recorded scar "$run" 'echo "TVIBURI-MARK-AFTER"' "$after"
    check "recorded: the guard in recorded mode logs the scar as a would-refuse, the firmware runs on to its marker, and the check never passes it (RUL-19)" \
      "[ \$rc = 1 ] && grep -q '\"tviburi\":\"would-refuse\".*TVI-SPI-KERNEL' '$work/recorded/renode-log.txt' && console recorded | grep -A1 -x 'TVIBURI-MARK-AFTER' | tail -n1 | grep -qx '0xAF7E0002'" ;;
  recordedfield)
    PLATFORM=/repo/tests/1300/recorded.repl run_case recordedfield field "$run" 'echo "R-SEL"' 'sysbus ReadDoubleWord 0x58024450' 'echo "TVIBURI-MARK-AFTER"' "$after"
    check "recordedfield: in recorded mode, SPI123SEL = 011 is a would-refuse and the write still reaches the copy (reads 0x3000), as if no rule fired (RUL-19)" \
      "[ \$rc = 1 ] && grep -q '\"tviburi\":\"would-refuse\".*TVI-RCC-FIELD' '$work/recordedfield/renode-log.txt' && console recordedfield | grep -A1 -x 'R-SEL' | tail -n1 | grep -qx '0x00003000'" ;;
  ardupilot|gen-s03|gen-hc16)
    cold=('sysbus WriteDoubleWord 0x580244D4 0x0' 'sysbus WriteDoubleWord 0x580244D8 0x0' 'sysbus WriteDoubleWord 0x580244DC 0x0'
      'sysbus WriteDoubleWord 0x580244E0 0x0' 'sysbus WriteDoubleWord 0x580244E4 0x0' 'sysbus WriteDoubleWord 0x580244E8 0x0'
      'sysbus WriteDoubleWord 0x580244EC 0x0' 'sysbus WriteDoubleWord 0x580244F0 0x0' 'sysbus WriteDoubleWord 0x580244F4 0x10000')
    qs=(); for p in spi1 spi2 i2c1 usart1 rng; do qs+=("python \"print(\\\"Q \\\" + str(self.Machine[\\\"sysbus.rcc\\\"].QueryKernelClock(\\\"$p\\\")))\""); done
    run_case "$t" "$t" "${cold[@]}" "$run" 'echo "TVIBURI-MARK-AFTER"' "$after" "${qs[@]}"
    if [ $t = gen-s03 ]; then
      check "gen-s03: sentinel-03's RCC sequence (Aethelgard 75da8053), then SPI1's start as generated: refused at SPI1, TVI-SPI-KERNEL, predicted-fault, naming SPI123SEL = 100 and p414, p371-372 (the per_ck scar; ruled 17:16Z)" \
        "[ \$rc = 3 ] && records $t 'len(recs) == 1 and recs[0][\"rule\"] == \"TVI-SPI-KERNEL\" and recs[0][\"kind\"] == \"predicted-fault\" and recs[0][\"bit\"] == \"RCC_D2CCIP1R.SPI123SEL = 100\" and \"p414\" in recs[0][\"citation\"] and \"p371-372\" in recs[0][\"citation\"]'"
    else
      check "$t: the measured RCC sequence, after the consumer's cold writes (pc none)$([ $t = gen-hc16 ] && echo ', then SPI1'\''s start on pll1_q_ck'), passes enforcing (WO §2)" \
        "[ \$rc = 0 ] && grep -qx 'CHECK PASS' '$work/$t.stdout' && console $t | grep -A1 -x 'TVIBURI-MARK-AFTER' | tail -n1 | grep -qx '0xAF7E0002'"
    fi
    case $t in
      ardupilot) check "ardupilot: the query finds spi1, spi2, i2c1, usart1 and rng running (§5)" "[ \$(grep -c '^Q [a-z0-9]*: running' '$work/$t/renode.out') = 5 ]" ;;
      gen-s03) check "gen-s03: SPI1's mux has not left pll1_q_ck for per_ck: no kernel clock (§5, p371-372; not an RCC refusal)" "grep -q '^Q spi1: NOT running; the mux has not switched from pll1_q_ck' '$work/$t/renode.out'" ;;
      gen-hc16) check "gen-hc16: SPI1 runs on pll1_q_ck (§5)" "grep -q '^Q spi1: running; pll1_q_ck' '$work/$t/renode.out'" ;;
    esac ;;
  oscclear)
    run_case oscclear - 'sysbus WriteDoubleWord 0x58024428 0x02020202' 'sysbus WriteDoubleWord 0x58024400 0x00010001' \
      'sysbus WriteDoubleWord 0x58024400 0x04010001' 'sysbus WriteDoubleWord 0x58024400 0x04000001'
    check "oscclear: PLL2 running on the HSE, then HSEON cleared with PLL2ON kept: PLL2 has no reference, TVI-RCC-PLL-REF, unmodeled (p384; p399)" \
      "[ \$rc = 3 ] && records oscclear 'len(recs) == 1 and recs[0][\"rule\"] == \"TVI-RCC-PLL-REF\" and recs[0][\"kind\"] == \"unmodeled\" and \"PLL2ON\" in recs[0][\"bit\"]'" ;;
  oscsys)
    run_case oscsys - 'sysbus WriteDoubleWord 0x58024430 0x0103038F' 'sysbus WriteDoubleWord 0x58024400 0x01000001' \
      'sysbus WriteDoubleWord 0x58024400 0x01000000' 'echo "R-CR"' 'sysbus ReadDoubleWord 0x58024400'
    check "oscsys: HSION cleared while the HSI is the system clock (SW = 000) and PLL1's source: the stock model keeps it (p386), so PLL1 keeps its reference and nothing refuses" \
      "[ \$rc = 0 ] && grep -qx 'CHECK PASS' '$work/oscsys.stdout' && console oscsys | grep -A1 -x 'R-CR' | tail -n1 | grep -q '^0x03000025\$'" ;;
  rsrbyte)
    run_case rsrbyte - 'sysbus WriteByte 0x580244D2 0x01' 'sysbus WriteWord 0x58024474 0x0000'
    check "rsrbyte: a byte write to RCC_RSR (RMVF) and a half-word write to RCC_CSR are valid (p450, p429: word, half-word and byte access) and pass" \
      "[ \$rc = 0 ] && grep -qx 'CHECK PASS' '$work/rsrbyte.stdout'" ;;
  misaligned)
    run_case misaligned - 'sysbus WriteDoubleWord 0x580244F2 0x0'
    check "misaligned: a 32-bit RCC write at 0xF2 refuses, TVI-RCC-WIDTH, unmodeled" \
      "[ \$rc = 3 ] && first_kernel misaligned 'r[\"rule\"] == \"TVI-RCC-WIDTH\" and \"misaligned\" in r[\"bit\"]'" ;;
  ledger)
    mkdir -p "$work/ledger"
    rc=0; container -v "$src:/repo:ro,Z" -v "$work/ledger:/out:Z" "$IMAGE" sh -c '/repo/scripts/build-release /repo /out/release >/dev/null' </dev/null >"$work/ledger.stdout" 2>&1 || rc=$?
    check "ledger: the release carries the RCC guard's 677 field rows; each acknowledged row cites TVI-WO-1300.rm0433.md §8, never 0100b's; the RCC bench rows R1-R10 are in it, with the (rule, bit) the cases emit (RUL-18)" \
      "[ \$rc = 0 ] && python3 -I -c '
import json, sys
rows = [json.loads(l) for l in open(sys.argv[1]).read().splitlines()[1:]]
rcc = [r for r in rows if r.get(\"peripheral\") == \"Tviburi.GuardedSTM32H7_RCC\"]
f = [r for r in rcc if r[\"row\"] == \"field\"]
a = [r for r in f if r[\"state\"] == \"acknowledged\"]
b = sorted(r[\"field\"] for r in rcc if r[\"row\"] == \"bench\")
pairs = sorted((r[\"rule\"], r[\"bit\"]) for r in rcc if r[\"row\"] == \"bench\")
want = sorted([(\"TVI-RCC-KERNEL\", \"RCC_D2CCIP1R.SPI123SEL = 000\"), (\"TVI-RCC-KERNEL\", \"RCC_D2CCIP1R.SPI123SEL = 100\"), (\"TVI-RCC-PLL\", \"DIVP1EN/DIVQ1EN/DIVR1EN\"),
  (\"TVI-RCC-CLOCK\", \"RCC_CR.PLL1ON (bit 24) = 0\"), (\"TVI-RCC-CLOCK\", \"RCC_D2CCIP1R.SPI123SEL (14:12) = 001\"), (\"TVI-RCC-CLOCK\", \"RCC_APB2ENR.SPI1EN (bit 12) = 0\"),
  (\"TVI-RCC-CLOCK\", \"RCC_CR.HSEON (bit 16) = 0\"), (\"TVI-RCC-CLOCK\", \"RCC_D1CCIPR.CKPERSEL (29:28) = 01\"), (\"TVI-RCC-PLL\", \"DIVN1/DIVP1/DIVQ1/DIVR1\"),
  (\"TVI-RCC-PLL-REF\", \"PLL1ON (bit 24) = 1\")])
sys.exit(0 if len(f) == 677 and a and all(r[\"reason\"].startswith(\"TVI-WO-1300.rm0433.md §8 A\") for r in a) and b == sorted(\"R%d\" % i for i in range(1, 11)) and pairs == want else 1)
' '$work/ledger/release/LEDGER.jsonl'" ;;
  alias17c)
    run_case alias17c - 'echo "R-17C"' 'sysbus ReadDoubleWord 0x5802457C'
    check "alias17c: RCC_C1_APB4LPENR (0x17C) reads RCC_APB4LPENR's reset 0x0421DEAA, where the stock model overflows its stack (p382; p493)" \
      "[ \$rc = 0 ] && grep -qx 'CHECK PASS' '$work/alias17c.stdout' && console alias17c | grep -A1 -x 'R-17C' | tail -n1 | grep -qx '0x0421DEAA'" ;;
  registrations)
    run_case registrations - 'echo "R-APB4ENR"' 'sysbus ReadDoubleWord 0x580244F4' 'python "print(\"Q \" + str(self.Machine[\"sysbus.rcc\"].QueryKernelClock(\"spi1\")))"' 'python "import System; B = System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance; g = self.Machine[\"sysbus.rcc\"]; i = g.GetType().GetField(\"inner\", B).GetValue(g); print(\"REGISTERED \" + str(i.GetType().GetField(\"RegisteredPeripherals\", B).GetValue(i).Count))"' 'peripherals'
    check "the 58 restated registrations, and the board's spi1, resolve on the guard; peripherals lists rcc as the guard" \
      "grep -q 'REGISTERED 59' '$work/registrations/renode.out' && grep -q 'rcc (GuardedSTM32H7_RCC)' '$work/registrations/renode.out'"
    check "registrations: at reset APB4ENR reads 0x00010000 (RTCAPBEN, p471), and SPI1 has no kernel clock because SPI1EN = 0 (Table 62 p376)" \
      "console registrations | grep -A1 -x 'R-APB4ENR' | tail -n1 | grep -qx '0x00010000' && grep -q '^Q spi1: NOT running; RCC_APB2ENR.SPI1EN = 0' '$work/registrations/renode.out'" ;;
  hse16)
    hse='python "import System; B = System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance; g = self.Machine[\"sysbus.rcc\"]; i = g.GetType().GetField(\"inner\", B).GetValue(g); print(\"HSE guard=\" + str(g.HseFrequency) + \" stock=\" + str(i.GetType().GetField(\"hseFrequency\", B).GetValue(i)))"'
    PLATFORM=/repo/tests/1300/hse16.repl run_case hse16 - "$hse"
    PLATFORM=/repo/tests/1300/hse16-trap.repl run_case hse16-trap - "$hse"
    check "hse16: hseFrequency set on the guard's entry reaches the owned stock RCC (16 MHz; ruled 14:35Z)" \
      "grep -q 'HSE guard=16000000 stock=16000000' '$work/hse16/renode.out'"
    check "hse16-trap: an untyped rcc: update lands on the cancelled entry and the guard keeps 8 MHz (the consumer requirement, measured)" \
      "grep -q 'HSE guard=8000000 stock=8000000' '$work/hse16-trap/renode.out'" ;;
  unenabled)
    run_case unenabled unenabled "$run"
    check "unenabled: SPI1 written with SPI1EN never set refuses at SPI1, TVI-SPI-ENABLE, predicted-fault, naming the enable bit and p373, at the firmware's store; 'clocked' (the same with the enable) passes" \
      "[ \$rc = 3 ] && records unenabled 'len(recs) >= 1 and recs[0][\"rule\"] == \"TVI-SPI-ENABLE\" and recs[0][\"kind\"] == \"predicted-fault\" and recs[0][\"peripheral\"] == \"sysbus.spi1\" and recs[0][\"bit\"] == \"RCC_APB2ENR.SPI1EN = 0\" and \"p373\" in recs[0][\"citation\"] and recs[0][\"access\"][\"pc\"] != \"none\"'" ;;
  tim2)
    run_case tim2 - 'sysbus WriteDoubleWord 0x4000002C 0x1234' 'echo "R-OFF"' 'sysbus ReadDoubleWord 0x4000002C' \
      'sysbus WriteDoubleWord 0x580244E8 0x00000001' 'echo "R-ON-RESET"' 'sysbus ReadDoubleWord 0x4000002C' 'sysbus WriteDoubleWord 0x4000002C 0x1234' 'echo "R-ON"' 'sysbus ReadDoubleWord 0x4000002C' \
      'machine Reset' 'sysbus WriteDoubleWord 0x4000002C 0x5678' 'echo "R-RESET"' 'sysbus ReadDoubleWord 0x4000002C' 'echo "R-ENR"' 'sysbus ReadDoubleWord 0x580244E8'
    check "tim2: an unguarded peripheral is locked from reset, as silicon ignores the write (p373): with TIM2EN never set, TIM2.ARR's write is dropped and it reads 0; with TIM2EN, ARR reads its reset 0xFFFFFFFF (the write never landed) and a write lands; after machine Reset it is locked again, and APB1LENR reads 0" \
      "[ \$rc = 0 ] && grep -qx 'CHECK PASS' '$work/tim2.stdout' && console tim2 | grep -A1 -x 'R-OFF' | tail -n1 | grep -qx '0x00000000' && console tim2 | grep -A1 -x 'R-ON-RESET' | tail -n1 | grep -qx '0xFFFFFFFF' && console tim2 | grep -A1 -x 'R-ON' | tail -n1 | grep -qx '0x00001234' && console tim2 | grep -A1 -x 'R-RESET' | tail -n1 | grep -qx '0x00000000' && console tim2 | grep -A1 -x 'R-ENR' | tail -n1 | grep -qx '0x00000000' && [ \$(grep -c 'Tried to write a locked peripheral: machine-0.timer2' '$work/tim2/renode.out') = 2 ]" ;;
  lost-pll|lost-sel|lost-en|lost-osc|lost-ckper)
    run_case "$t" "$t" "$run"
    case $t in
      lost-pll) bit='RCC_CR.PLL1ON (bit 24) = 0'; page=p383 ;;
      lost-sel) bit='RCC_D2CCIP1R.SPI123SEL (14:12) = 001'; page=p414 ;;
      lost-en) bit='RCC_APB2ENR.SPI1EN (bit 12) = 0'; page=p376 ;;
      lost-osc) bit='RCC_CR.HSEON (bit 16) = 0'; page=p384 ;;
      lost-ckper) bit='RCC_D1CCIPR.CKPERSEL (29:28) = 01'; page=p412 ;;
    esac
    check "$t: with SPI1 running, $bit stops its kernel clock: refused at the RCC write, TVI-RCC-CLOCK, predicted-fault, citing p371 and $page" \
      "[ \$rc = 3 ] && records $t 'len(recs) == 1 and recs[0][\"rule\"] == \"TVI-RCC-CLOCK\" and recs[0][\"kind\"] == \"predicted-fault\" and recs[0][\"peripheral\"] == \"sysbus.rcc\" and recs[0][\"bit\"] == \"$bit\" and \"p371\" in recs[0][\"citation\"] and \"$page\" in recs[0][\"citation\"] and \"spi1 is enabled\" in recs[0][\"condition\"] and recs[0][\"access\"][\"pc\"] != \"none\"'" ;;
  stopped|ckper-live|blocked|cfg2same)
    run_case "$t" "$t" "$run" 'echo "TVIBURI-MARK-AFTER"' "$after"
    case $t in
      stopped) what="SPI1 stopped (SPE = 0), then SPI123SEL changed: nothing runs, a valid order" ;;
      ckper-live) what="SPI1 running on per_ck, CKPERSEL from hsi_ker_ck to hse_ck with both running: the mux switches (p371)" ;;
      blocked) what="SPI1 running on per_ck (hsi_ker_ck), HSION cleared while HSI is the system clock: blocked (p386), the clock stays" ;;
      cfg2same) what="SPI1 running, CFG2 written with the value it holds (TVI-WO-1300a (4), p2221)" ;;
    esac
    check "$t: $what; passes enforcing, past its last write" \
      "[ \$rc = 0 ] && grep -qx 'CHECK PASS' '$work/$t.stdout' && console $t | grep -A1 -x 'TVIBURI-MARK-AFTER' | tail -n1 | grep -qx '0xAF7E0002'" ;;
  sel5)
    run_case sel5 sel5 "$run"
    check "sel5: SPI123SEL = 101 (reserved, the kernel clock disabled, p414) refuses, TVI-RCC-FIELD, unmodeled (ruled 17:16Z)" \
      "[ \$rc = 3 ] && records sel5 'len(recs) == 1 and recs[0][\"rule\"] == \"TVI-RCC-FIELD\" and recs[0][\"kind\"] == \"unmodeled\" and recs[0][\"bit\"].startswith(\"RCC_D2CCIP1R.SPI123SEL (14:12) = 101\") and \"p414\" in recs[0][\"citation\"]'" ;;
  divr)
    run_case divr divr "$run"
    check "divr: RCC_PLL1DIVR changed while PLL1 is on refuses, TVI-RCC-PLL, predicted-fault (p403-404: silicon ignores it)" \
      "[ \$rc = 3 ] && records divr 'len(recs) == 1 and recs[0][\"rule\"] == \"TVI-RCC-PLL\" and recs[0][\"kind\"] == \"predicted-fault\" and recs[0][\"bit\"] == \"DIVN1/DIVP1/DIVQ1/DIVR1\" and \"p403-404\" in recs[0][\"citation\"]'" ;;
  pll1src)
    run_case pll1src - 'sysbus WriteDoubleWord 0x58024400 0x00010001' 'sysbus WriteDoubleWord 0x58024410 0x2' 'sysbus WriteDoubleWord 0x58024400 0x01010001' \
      'sysbus WriteDoubleWord 0x58024400 0x01010000'
    check "pll1src (TVI-WO-1300a (5)): HSION cleared while HSI feeds PLL1 (on, not the system clock): RM0433 contradicts itself (p384-386 against p340-342), so TVI-RCC-PLL-REF, unmodeled, as 1300 ruled; bench row R10 asks the Nucleo" \
      "[ \$rc = 3 ] && records pll1src 'len(recs) == 1 and recs[0][\"rule\"] == \"TVI-RCC-PLL-REF\" and recs[0][\"kind\"] == \"unmodeled\" and recs[0][\"bit\"] == \"PLL1ON (bit 24) = 1\"'" ;;
  spi1off)
    run_case spi1off - 'sysbus WriteDoubleWord 0x580244F0 0x00001000' 'sysbus WriteDoubleWord 0x580244F0 0x0' 'sysbus WriteDoubleWord 0x4001300C 0x04400000'
    check "spi1off: SPI1EN set, then cleared (SPI1 stopped), then a CFG2 write: SPI1 stays unlocked on the bus, so its guard refuses, TVI-SPI-ENABLE, predicted-fault" \
      "[ \$rc = 3 ] && records spi1off 'len(recs) == 1 and recs[0][\"rule\"] == \"TVI-SPI-ENABLE\" and recs[0][\"kind\"] == \"predicted-fault\"' && ! grep -q 'locked peripheral: machine-0.spi1' '$work/spi1off/renode.out'" ;;
  cstart)
    PLATFORM=/repo/tests/1300/recorded.repl run_case cstart - 'sysbus WriteDoubleWord 0x58024400 0x01000001' 'sysbus WriteDoubleWord 0x580244F0 0x00001000' \
      'sysbus WriteDoubleWord 0x40013000 0x00001000' 'sysbus WriteDoubleWord 0x4001300C 0x04400000' 'sysbus WriteDoubleWord 0x40013000 0x00001001' \
      'sysbus WriteDoubleWord 0x58024400 0x00000001' 'sysbus WriteDoubleWord 0x40013000 0x00001201' 'sysbus WriteDoubleWord 0x580244E8 0x00000001'
    check "cstart (recorded): SPI1 started on pll1_q_ck, PLL1ON cleared (TVI-RCC-CLOCK), then CSTART with SPE (a start, TVI-SPI-KERNEL), then an unrelated RCC write, which loses nothing that ran: exactly those two would-refuse records" \
      "python3 -I -c '
import json, re, sys
recs = [json.loads(m.group(1)) for m in (re.search(r\"\\] (?:rcc|spi1): (\\{\\\"tviburi\\\":\\\"would-refuse\\\".*)\$\", l.rstrip()) for l in open(sys.argv[1])) if m]
sys.exit(0 if [(r[\"rule\"], r[\"bit\"]) for r in recs] == [(\"TVI-RCC-CLOCK\", \"RCC_CR.PLL1ON (bit 24) = 0\"), (\"TVI-SPI-KERNEL\", \"RCC_D2CCIP1R.SPI123SEL = 000\")] else 1)
' '$work/cstart/renode-log.txt'" ;;
  dma)
    # DMA1 stream 0, memory to SPI1's CFG2, with DMA1EN set and SPI1EN never set (RM0433 Rev 9 §15.5.5 p662 (DMA_SxCR); SxCR 0x10,
    # SxNDTR 0x14, SxPAR 0x18, SxM0AR 0x1C; mem-to-mem DIR = 10 with 32-bit sizes, as TVI-WO-1300a.measure.md §1.1 measured)
    run_case dma - 'sysbus WriteDoubleWord 0x580244D8 0x00000001' 'sysbus WriteDoubleWord 0x24000100 0x04400000' 'sysbus WriteDoubleWord 0x40020014 0x1' \
      'sysbus WriteDoubleWord 0x40020018 0x24000100' 'sysbus WriteDoubleWord 0x4002001C 0x4001300C' 'sysbus WriteDoubleWord 0x40020010 0x00005481' \
      'echo "R-NDTR"' 'sysbus ReadDoubleWord 0x40020014'
    check "dma (adversarial): a DMA write to SPI1 with SPI1EN never set reaches SPI1's guard, which refuses, TVI-SPI-ENABLE, predicted-fault" \
      "[ \$rc = 3 ] && console dma | grep -A1 -x 'R-NDTR' | tail -n1 | grep -qx '0x00000000' && records dma 'len(recs) == 1 and recs[0][\"rule\"] == \"TVI-SPI-ENABLE\" and recs[0][\"register\"] == \"SPI_CFG2\"'" ;;
  lost-alias)
    run_case lost-alias - 'sysbus WriteDoubleWord 0x58024400 0x01000001' 'sysbus WriteDoubleWord 0x580244F0 0x00001000' \
      'sysbus WriteDoubleWord 0x40013000 0x00001000' 'sysbus WriteDoubleWord 0x4001300C 0x04400000' 'sysbus WriteDoubleWord 0x40013000 0x00001001' \
      'sysbus WriteDoubleWord 0x58024550 0x00000000'
    check "lost-alias (adversarial): SPI1EN cleared through RCC_C1_APB2ENR (0x150, the same register, p382) under a running SPI1 refuses, TVI-RCC-CLOCK, naming the alias" \
      "[ \$rc = 3 ] && records lost-alias 'len(recs) == 1 and recs[0][\"rule\"] == \"TVI-RCC-CLOCK\" and recs[0][\"register\"] == \"RCC_C1_APB2ENR\" and recs[0][\"bit\"] == \"RCC_APB2ENR.SPI1EN (bit 12) = 0\"'" ;;
  adc3)
    on='python "print(\"ADC3-ON \" + str(self.Machine.SystemBus.IsPeripheralEnabled(self.Machine[\"sysbus.adc3\"])))"'
    run_case adc3 - "$on" 'sysbus WriteDoubleWord 0x580244E0 0x01000000' "$on" 'echo "R-ENR"' 'sysbus ReadDoubleWord 0x580244E0'
    check "adc3: ADC3EN is a modeled bus enable (TVI-WO-1300b (3), ruled 2026-11-25T03:03Z Q1, superseding 1300a's 17:52Z): adc3 is locked at reset and unlocked by ADC3EN = 1, with no record; AHB4ENR reads it back" \
      "[ \$rc = 0 ] && grep -qx 'CHECK PASS' '$work/adc3.stdout' && [ \"\$(grep -o 'ADC3-ON [A-Za-z]*' '$work/adc3/renode.out' | tr '\n' ' ')\" = 'ADC3-ON False ADC3-ON True ' ] && console adc3 | grep -A1 -x R-ENR | tail -n1 | grep -qx 0x01000000" ;;
  unheld)
    PLATFORM=/repo/tests/1300/unregistered.repl run_case unheld - 'sysbus WriteDoubleWord 0x580244F0 0x00001000' 'sysbus WriteDoubleWord 0x4001300C 0x04400000'
    check "unheld (adversarial): a guarded SPI1 not registered on the RCC guard refuses its access, TVI-SPI-ENABLE, unmodeled: unwatched, it cannot pass" \
      "[ \$rc = 3 ] && records unheld 'len(recs) == 1 and recs[0][\"rule\"] == \"TVI-SPI-ENABLE\" and recs[0][\"kind\"] == \"unmodeled\" and \"holds it registered\" in recs[0][\"condition\"]'" ;;
  relost-sel|relost-pll)
    case $t in
      relost-sel) fw=lost-sel; more='sysbus WriteDoubleWord 0x58024400 0x00000001'; first='RCC_D2CCIP1R.SPI123SEL (14:12) = 001'; second='RCC_CR.PLL1ON (bit 24) = 0' ;;
      relost-pll) fw=lost-pll; more='sysbus WriteDoubleWord 0x580244F0 0x00000000'; first='RCC_CR.PLL1ON (bit 24) = 0'; second='RCC_APB2ENR.SPI1EN (bit 12) = 0' ;;
    esac
    run_case "$t" "$fw" "$run" 'echo "R-SEL"' 'sysbus ReadDoubleWord 0x58024450' "$more"
    check "$t (adversarial): after a refused loss the guard's state is the pre-write state (D2CCIP1R reads 0), so a second, different loss refuses too" \
      '[ $rc = 3 ] && console $t | grep -A1 -x R-SEL | tail -n1 | grep -qx 0x00000000 && [ "$(rule_bits $t)" = "$(printf "TVI-RCC-CLOCK %s\nTVI-RCC-CLOCK %s" "$first" "$second")" ]' ;;
  divr2rmw)
    run_case divr2rmw - 'sysbus WriteDoubleWord 0x58024438 0x0101018F' 'sysbus WriteDoubleWord 0x58024400 0x04000001' 'echo "R-DIVR2"' 'sysbus ReadDoubleWord 0x58024438' \
      'sysbus WriteDoubleWord 0x58024438 0x0101008F' 'sysbus WriteDoubleWord 0x58024438 0x0101008E'
    check "divr2rmw (adversarial): PLL2DIVR reads back with DIVN2's top bit dropped (the stock model; a finding); writing back what it read while PLL2 is on is no change and passes; a real change refuses, TVI-RCC-PLL" \
      "[ \$rc = 3 ] && console divr2rmw | grep -A1 -x 'R-DIVR2' | tail -n1 | grep -qx '0x0101008F' && records divr2rmw 'len(recs) == 1 and recs[0][\"rule\"] == \"TVI-RCC-PLL\" and recs[0][\"access\"][\"value\"] == \"0x101008E\"'" ;;
  kernelq)
    rq='python "self.Machine[\"sysbus.rcc\"].RequireKernelClock(\"spi1\", \"CR1.SPE\")"'
    run_case kernelq - 'sysbus WriteDoubleWord 0x580244F0 0x00001000' "$rq" 'sysbus WriteDoubleWord 0x58024450 0x00004000' "$rq"
    check "kernelq: the RCC guard's own query, asked from the monitor, refuses under rcc, TVI-RCC-KERNEL, predicted-fault: SPI123SEL = 000 with PLL1 off, then 100 with the mux stuck on pll1_q_ck (bench rows R1, R2)" \
      "[ \$rc = 3 ] && records kernelq '[(r[\"rule\"], r[\"kind\"], r[\"peripheral\"], r[\"bit\"]) for r in recs] == [(\"TVI-RCC-KERNEL\", \"predicted-fault\", \"sysbus.rcc\", \"RCC_D2CCIP1R.SPI123SEL = 000\"), (\"TVI-RCC-KERNEL\", \"predicted-fault\", \"sysbus.rcc\", \"RCC_D2CCIP1R.SPI123SEL = 100\")]'" ;;
  usart-lost|usart-lost-en)
    case $t in
      usart-lost) port=usart1; bit='RCC_D2CCIP2R.USART16SEL (5:3) = 100'; page='p417'; cause='has not switched from rcc_pclk2' ;;
      usart-lost-en) port=usart3; bit='RCC_APB1LENR.USART3EN (bit 18) = 0'; page='p376'; cause='USART3EN = 0' ;;
    esac
    run_case "$t" "$t" "$run" 'echo "TVIBURI-MARK-AFTER"' "$after"
    check "$t: a write that stops a running $port's kernel clock (UE = 1) refuses at the RCC write, TVI-RCC-CLOCK, predicted-fault, naming the field, the port, UE = 1, $page, p2019 and the firmware's PC (TVI-WO-1300b (2))" \
      "[ \$rc = 3 ] && records $t 'len(recs) == 1 and recs[0][\"rule\"] == \"TVI-RCC-CLOCK\" and recs[0][\"kind\"] == \"predicted-fault\" and recs[0][\"peripheral\"] == \"sysbus.rcc\" and recs[0][\"bit\"] == \"$bit\" and recs[0][\"condition\"].startswith(\"$port is enabled (UE = 1)\") and \"$cause\" in recs[0][\"condition\"] and \"$page\" in recs[0][\"citation\"] and \"p2019\" in recs[0][\"citation\"] and recs[0][\"access\"][\"pc\"] != \"none\"'" ;;
  usart-stopped|usart-othersel)
    run_case "$t" "$t" "$run" 'echo "TVIBURI-MARK-AFTER"' "$after"
    check "$t: the same selector change with UE = 0 (usart-stopped), or a change of the other mux under a running USART1 (usart-othersel), passes with no record" \
      "[ \$rc = 0 ] && grep -qx 'CHECK PASS' '$work/$t.stdout' && console $t | grep -A1 -x TVIBURI-MARK-AFTER | tail -n1 | grep -qx 0xAF7E0002" ;;
  usart-unheld)
    PLATFORM=/repo/tests/1300/usart-unheld.repl run_case usart-unheld - 'sysbus WriteDoubleWord 0x580244F0 0x00000010' 'sysbus WriteDoubleWord 0x4001100C 0x22C'
    check "usart-unheld (TVI-WO-1300b (2), ruled Q2; 1305 G6): a USART guard not registered on the RCC guard refuses its access, TVI-USART-CONFIG, unmodeled: unwatched, it cannot pass" \
      "[ \$rc = 3 ] && records usart-unheld 'len(recs) == 1 and recs[0][\"rule\"] == \"TVI-USART-CONFIG\" and recs[0][\"kind\"] == \"unmodeled\" and recs[0][\"peripheral\"] == \"sysbus.usart1\" and \"not registered on an RCC guard\" in recs[0][\"condition\"]'" ;;
  hse-none)
    PLATFORM_SED='/^    hseFrequency: 8000000$/d' run_case hse-none hse-on "$run" 'echo "TVIBURI-MARK-AFTER"' "$after"
    check "hse-none: on a platform that declares no hseFrequency, the HSE's start refuses, TVI-RCC-HSE, unmodeled, naming HSEON, p384 and the firmware's PC; no 8 MHz default (TVI-WO-1300b (4))" \
      "[ \$rc = 3 ] && ! grep -q '^    hseFrequency' '$work/hse-none/platform.repl' && records hse-none 'len(recs) == 1 and recs[0][\"rule\"] == \"TVI-RCC-HSE\" and recs[0][\"kind\"] == \"unmodeled\" and recs[0][\"bit\"] == \"RCC_CR.HSEON (bit 16) = 1\" and \"p384\" in recs[0][\"citation\"] and recs[0][\"access\"][\"pc\"] != \"none\"'" ;;
  hse-zero)
    PLATFORM_SED='s/^    hseFrequency: 8000000$/    hseFrequency: 0/' run_case hse-zero hse-on "$run" 'echo "TVIBURI-MARK-AFTER"' "$after"
    check "hse-zero (adversarial): hseFrequency declared 0 is no frequency: the HSE's start refuses, TVI-RCC-HSE, unmodeled (0 Hz would pass the flash guard's latency)" \
      "[ \$rc = 3 ] && grep -qx '    hseFrequency: 0' '$work/hse-zero/platform.repl' && records hse-zero 'len(recs) == 1 and recs[0][\"rule\"] == \"TVI-RCC-HSE\" and recs[0][\"kind\"] == \"unmodeled\"'" ;;
  hse-declared)
    hse='python "import System; B = System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance; g = self.Machine[\"sysbus.rcc\"]; i = g.GetType().GetField(\"inner\", B).GetValue(g); print(\"HSE guard=\" + str(g.HseFrequency) + \" stock=\" + str(i.GetType().GetField(\"hseFrequency\", B).GetValue(i)))"'
    PLATFORM_SED='s/^    hseFrequency: 8000000$/    hseFrequency: 16000000/' run_case hse-declared hse-on "$hse" "$run" 'echo "TVIBURI-MARK-AFTER"' "$after"
    check "hse-declared: with 16 MHz declared the same start passes, and 16 MHz reaches the guard and the owned stock RCC (usart-idlehse computes 64 MHz from it)" \
      "[ \$rc = 0 ] && grep -qx 'CHECK PASS' '$work/hse-declared.stdout' && grep -q 'HSE guard=16000000 stock=16000000' '$work/hse-declared/renode.out' && console hse-declared | grep -A1 -x TVIBURI-MARK-AFTER | tail -n1 | grep -qx 0xAF7E0002" ;;
  adc)
    run_case adc adc "$run" 'echo "TVIBURI-MARK-AFTER"' "$after"
    check "adc: the armed Sentinel-03 run's refused RCC writes, in its values (ADC12EN, ADC3EN; ADC12LPEN, ADC3LPEN, USART2LPEN, TIM1LPEN cleared) pass with no record (TVI-WO-1300b (3), ruled Q1)" \
      "[ \$rc = 0 ] && grep -qx 'CHECK PASS' '$work/adc.stdout' && console adc | grep -A1 -x TVIBURI-MARK-AFTER | tail -n1 | grep -qx 0xAF7E0002" ;;
  rtcen)
    run_case rtcen rtcen "$run" 'echo "TVIBURI-MARK-AFTER"' "$after" 'echo "R-BDCR0"' 'sysbus ReadDoubleWord 0x20000108' 'echo "R-BKP"' 'sysbus ReadDoubleWord 0x2000010C' 'echo "R-BDCR1"' 'sysbus ReadDoubleWord 0x20000110'
    check "rtcen: RCC_BDCR.RTCEN reads 0 at construction (p427); ArduPilot's set_rtc_backup order (BDCR written 0, an RTC backup register, RTCEN written 1) passes with no record; the BDCR write of 0 leaves rtc unlocked (its backup register reads back: RTCAPBEN = 1, p471); RTCEN reads 1 after (TVI-WO-1300b (5), ruled Q4)" \
      "[ \$rc = 0 ] && grep -qx 'CHECK PASS' '$work/rtcen.stdout' && console rtcen | grep -A1 -x TVIBURI-MARK-AFTER | tail -n1 | grep -qx 0xAF7E0002 && console rtcen | grep -A1 -x R-BDCR0 | tail -n1 | grep -qx 0x00000000 && console rtcen | grep -A1 -x R-BKP | tail -n1 | grep -qx 0xA5A50001 && console rtcen | grep -A1 -x R-BDCR1 | tail -n1 | grep -qx 0x00008000" ;;
  rtcen-reset)
    run_case rtcen-reset - 'echo "R-0"' 'sysbus ReadDoubleWord 0x58024470' 'sysbus WriteDoubleWord 0x58024470 0x00008000' 'machine Reset' 'echo "R-RESET"' 'sysbus ReadDoubleWord 0x58024470' \
      'sysbus WriteDoubleWord 0x58024470 0x00000000' 'sysbus WriteDoubleWord 0x58004054 0x5A5A0002' 'echo "R-0B"' 'sysbus ReadDoubleWord 0x58024470' 'echo "R-BKP"' 'sysbus ReadDoubleWord 0x58004054'
    check "rtcen-reset: RTCEN reads 0 at construction, is kept across machine Reset (p427: only a backup domain reset clears it), and written back to 0 it reads 0 while rtc stays unlocked (its backup register reads back), with no record" \
      "[ \$rc = 0 ] && grep -qx 'CHECK PASS' '$work/rtcen-reset.stdout' && console rtcen-reset | grep -A1 -x R-0 | tail -n1 | grep -qx 0x00000000 && console rtcen-reset | grep -A1 -x R-RESET | tail -n1 | grep -qx 0x00008000 && console rtcen-reset | grep -A1 -x R-0B | tail -n1 | grep -qx 0x00000000 && console rtcen-reset | grep -A1 -x R-BKP | tail -n1 | grep -qx 0x5A5A0002" ;;
  *) echo "unknown case $t" >&2; exit 2 ;;
  esac
done

echo "$fails failed"
[ $fails = 0 ]
