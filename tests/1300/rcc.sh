#!/usr/bin/env bash
# tests/1300/rcc.sh [SRC] — TVI-WO-1300's acceptance suite for the RCC guard, run on the host, as tests/spike.sh is.
# SRC defaults to a `git archive HEAD` export, so what is tested is what is committed. Each case is one container in
# the pinned tools image (tests/spike.sh's IMAGE), with no network and the source mounted read-only.
#   TVIBURI_TESTS="scar clocked ..."  run only these cases.
#   TVIBURI_CI=1                      plain `podman run`, for a disposable CI runner only; elsewhere ~/bin/capped-run.
# Exit 0 only if every selected case passed.
#
# The kernel-clock query is asked at SPI1's start by a test-only bus hook on spi1 (a CR1 write with SPE set calls
# rcc.RequireKernelClock), because wiring it into the SPI guard is TVI-WO-1300a's. A hook is not a guard (RUL-07); here
# it only exercises the query, with the store's own PC.
set -euo pipefail

here=$(cd "$(dirname "$0")/../.." && pwd)
IMAGE=$(sed -n 's/^IMAGE=//p' "$here/tests/spike.sh")
CASES="scar switch stuck clocked switched alias field pll divq pllref divm0 sw width read usart2 uncovered fields reset resetfwd spi1rst alias17c registrations hse16 recorded recordedfield ardupilot gen-s03 gen-hc16"
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
    if [ -n "${PLATFORM:-}" ]; then   # a planted platform in place of the prelude's board
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
        --check-cfg "cfg(path, values(\"clocked\",\"scar\",\"switch\",\"stuck\",\"switched\",\"alias\",\"field\",\"pll\",\"divq\",\"pllref\",\"divm0\",\"sw\",\"width\",\"read\",\"usart2\",\"ardupilot\",\"gen-s03\",\"gen-hc16\"))" \
        -C link-arg=-T/repo/firmware/link.x -o /out/fw.elf /repo/firmware/rcc.rs || exit 91
    fi
    timeout 120 renode --disable-gui --console --plain /out/run.resc </dev/null >/out/renode.out 2>&1
    echo "renode-exit=$?"
    /out/target/debug/tviburi-check "$v" /out/renode-log.txt spi1 rcc
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

hook='sysbus SetHookBeforePeripheralWrite sysbus.spi1 "offset == 0 and (value & 1) and machine[\"sysbus.rcc\"].RequireKernelClock(\"spi1\", \"CR1.SPE\")"'
run='emulation RunFor "0.001"'
after='sysbus ReadDoubleWord 0x20000104'

first_kernel() { # first_kernel <name> <python expr over r, the first refusal>
  records "$1" "len(recs) >= 1 and (lambda r: $2)(recs[0])"
}

for t in ${TVIBURI_TESTS:-$CASES}; do
  case $t in
  scar)
    run_case scar scar "$hook" "$run"
    check "scar: SPI1 started with SPI123SEL on pll1_q_ck and PLL1 off refuses, predicted-fault, naming the selector, the source and the pages" \
      "[ \$rc = 3 ] && first_kernel scar 'r[\"rule\"] == \"TVI-RCC-KERNEL\" and r[\"kind\"] == \"predicted-fault\" and \"SPI123SEL = 000\" in r[\"bit\"] and \"pll1_q_ck\" in r[\"condition\"] and \"PLL1ON = 0\" in r[\"condition\"] and \"p414\" in r[\"citation\"] and \"p371-372\" in r[\"citation\"] and r[\"access\"][\"pc\"] != \"none\"'" ;;
  switch)
    run_case switch switch "$hook" "$run"
    check "switch: SPI123SEL to pll2_p_ck with PLL2 off: the mux cannot switch, predicted-fault (p371)" \
      "[ \$rc = 3 ] && first_kernel switch 'r[\"kind\"] == \"predicted-fault\" and \"has not switched from pll1_q_ck\" in r[\"condition\"] and \"pll2_p_ck\" in r[\"condition\"]'" ;;
  stuck)
    run_case stuck stuck "$hook" "$run"
    check "stuck: SPI123SEL to per_ck (running) while the input pll1_q_ck never ran: no clock, predicted-fault (p371-372)" \
      "[ \$rc = 3 ] && first_kernel stuck 'r[\"kind\"] == \"predicted-fault\" and \"has not switched from pll1_q_ck\" in r[\"condition\"] and \"per_ck\" in r[\"condition\"]'" ;;
  clocked)
    run_case clocked clocked "$hook" "$run" 'echo "TVIBURI-MARK-AFTER"' "$after"
    check "clocked: PLL1 started, SPI1 starts on pll1_q_ck: a pass, the firmware past its last write" \
      "[ \$rc = 0 ] && grep -qx 'CHECK PASS' '$work/clocked.stdout' && console clocked | grep -A1 -x 'TVIBURI-MARK-AFTER' | tail -n1 | grep -qx '0xAF7E0002'" ;;
  switched)
    run_case switched switched "$hook" "$run" 'echo "TVIBURI-MARK-AFTER"' "$after"
    check "switched: PLL1 running, then SPI123SEL to per_ck: the mux switches, a pass" \
      "[ \$rc = 0 ] && grep -qx 'CHECK PASS' '$work/switched.stdout' && console switched | grep -A1 -x 'TVIBURI-MARK-AFTER' | tail -n1 | grep -qx '0xAF7E0002'" ;;
  alias)
    run_case alias alias "$hook" "$run" 'echo "TVIBURI-MARK-AFTER"' "$after"
    check "alias: SPI1EN through RCC_C1_APB2ENR (0x150) is the same register (p382): a pass" \
      "[ \$rc = 0 ] && grep -qx 'CHECK PASS' '$work/alias.stdout' && console alias | grep -A1 -x 'TVIBURI-MARK-AFTER' | tail -n1 | grep -qx '0xAF7E0002'" ;;
  field)
    run_case field field "$hook" "$run"
    check "field: SPI123SEL = 011 (I2S_CKIN, not modeled) refuses, TVI-RCC-FIELD, unmodeled" \
      "[ \$rc = 3 ] && first_kernel field 'r[\"rule\"] == \"TVI-RCC-FIELD\" and r[\"kind\"] == \"unmodeled\" and \"SPI123SEL\" in r[\"bit\"]'" ;;
  pll)
    run_case pll pll "$hook" "$run"
    check "pll: DIVQ1EN cleared while PLL1 is on refuses, TVI-RCC-PLL, predicted-fault (p401)" \
      "[ \$rc = 3 ] && first_kernel pll 'r[\"rule\"] == \"TVI-RCC-PLL\" and r[\"kind\"] == \"predicted-fault\" and \"p400-401\" in r[\"citation\"]'" ;;
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
      'sysbus WriteDoubleWord 0x580244F0 0x0' 'sysbus WriteDoubleWord 0x40013010 0x40'
    check "reset: TIM1RST through the guard resets TIM1 (PSC 5 -> 0; RM0433 Rev 9 §8.7.35 p442); a hook and logging on the guard do not displace it; an unclocked SPI1 locks" \
      "[ \$rc = 0 ] && console reset | grep -A1 -x 'R-TIM1-BEFORE' | tail -n1 | grep -qx '0x00000005' && console reset | grep -A1 -x 'R-TIM1-AFTER' | tail -n1 | grep -qx '0x00000000' && console reset | grep -A1 -x 'R-AFTER-HOOK' | tail -n1 | grep -qx '0x00000100' && grep -q 'R-HOOK' '$work/reset/renode.out' && grep -q 'Tried to write a locked peripheral' '$work/reset/renode.out'" ;;
  divq)
    run_case divq divq "$hook" "$run"
    check "divq: PLL1 started with DIVQ1EN = 0: pll1_q_ck is disabled, SPI1 starts with no kernel clock, predicted-fault (p401)" \
      "[ \$rc = 3 ] && first_kernel divq 'r[\"kind\"] == \"predicted-fault\" and \"DIVQ1EN = 0\" in r[\"condition\"]'" ;;
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
    PLATFORM=/repo/tests/1300/recorded.repl run_case recorded scar "$hook" "$run" 'echo "TVIBURI-MARK-AFTER"' "$after"
    check "recorded: the guard in recorded mode logs the scar as a would-refuse, the firmware runs on to its marker, and the check never passes it (RUL-19)" \
      "[ \$rc = 1 ] && grep -q '\"tviburi\":\"would-refuse\".*TVI-RCC-KERNEL' '$work/recorded/renode-log.txt' && console recorded | grep -A1 -x 'TVIBURI-MARK-AFTER' | tail -n1 | grep -qx '0xAF7E0002'" ;;
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
    check "$t: the measured RCC sequence, after the consumer's cold writes (pc none), passes enforcing (WO §2)" \
      "[ \$rc = 0 ] && grep -qx 'CHECK PASS' '$work/$t.stdout' && console $t | grep -A1 -x 'TVIBURI-MARK-AFTER' | tail -n1 | grep -qx '0xAF7E0002'"
    case $t in
      ardupilot) check "ardupilot: the query finds spi1, spi2, i2c1, usart1 and rng running (§5)" "[ \$(grep -c '^Q [a-z0-9]*: running' '$work/$t/renode.out') = 5 ]" ;;
      gen-s03) check "gen-s03: SPI1's mux has not left pll1_q_ck for per_ck: no kernel clock (§5, p371-372; not an RCC refusal)" "grep -q '^Q spi1: NOT running; the mux has not switched from pll1_q_ck' '$work/$t/renode.out'" ;;
      gen-hc16) check "gen-hc16: SPI1 runs on pll1_q_ck (§5)" "grep -q '^Q spi1: running; pll1_q_ck' '$work/$t/renode.out'" ;;
    esac ;;
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
  *) echo "unknown case $t" >&2; exit 2 ;;
  esac
done

echo "$fails failed"
[ $fails = 0 ]
