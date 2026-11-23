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
CASES="scar switch stuck clocked switched alias field pll fields reset width read alias17c registrations hse16"
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
        --check-cfg "cfg(path, values(\"clocked\",\"scar\",\"switch\",\"stuck\",\"switched\",\"alias\",\"field\",\"pll\"))" \
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
  width)
    run_case width - 'sysbus WriteWord 0x580244F0 0x1000'
    check "width: a 16-bit RCC write refuses, TVI-RCC-WIDTH, unmodeled" \
      "[ \$rc = 3 ] && first_kernel width 'r[\"rule\"] == \"TVI-RCC-WIDTH\" and r[\"kind\"] == \"unmodeled\"'" ;;
  read)
    run_case read - 'sysbus ReadDoubleWord 0x58024414'
    check "read: a read at an RCC offset with no register (0x014; Table 84 p496) refuses, TVI-RCC-READ, unmodeled" \
      "[ \$rc = 3 ] && first_kernel read 'r[\"rule\"] == \"TVI-RCC-READ\" and r[\"kind\"] == \"unmodeled\"'" ;;
  alias17c)
    run_case alias17c - 'echo "R-17C"' 'sysbus ReadDoubleWord 0x5802457C'
    check "alias17c: RCC_C1_APB4LPENR (0x17C) reads RCC_APB4LPENR's reset 0x0421DEAA, where the stock model overflows its stack (p382; p493)" \
      "[ \$rc = 0 ] && grep -qx 'CHECK PASS' '$work/alias17c.stdout' && console alias17c | grep -A1 -x 'R-17C' | tail -n1 | grep -qx '0x0421DEAA'" ;;
  registrations)
    run_case registrations - 'python "import System; B = System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance; g = self.Machine[\"sysbus.rcc\"]; i = g.GetType().GetField(\"inner\", B).GetValue(g); print(\"REGISTERED \" + str(i.GetType().GetField(\"RegisteredPeripherals\", B).GetValue(i).Count))"' 'peripherals'
    check "the 58 restated registrations, and the board's spi1, resolve on the guard; peripherals lists rcc as the guard" \
      "grep -q 'REGISTERED 59' '$work/registrations/renode.out' && grep -q 'rcc (GuardedSTM32H7_RCC)' '$work/registrations/renode.out'" ;;
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
