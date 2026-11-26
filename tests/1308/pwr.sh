#!/usr/bin/env bash
# tests/1308/pwr.sh [SRC] — TVI-WO-1308's acceptance cases for the PWR guard, run on the host as tests/1308/dmamux.sh is: each case
# one container in the pinned tools image (tests/spike.sh's IMAGE), no network, the source read-only. SRC defaults to a `git archive
# HEAD` export. tests/spike.sh runs each case as pwr-<case> in its acceptance group.
#   TVIBURI_TESTS="second ap ..."  run only these cases.
#   TVIBURI_CI=1                   plain `podman run`, for a disposable CI runner only; elsewhere ~/bin/capped-run.
# Exit 0 only if every selected case passed. The board keeps the stock pwr stub (ruled 2026-11-26T10:08Z Q17 (a)): each case's
# platform is the board with model/stm32h7-pwr-restate.repl appended and `tviburiPwr` given its mode and revision "V", as the
# replay declares it; NOPWR=1 leaves the board as it is. Pages: RM0433 Rev 9 §6.8 (PWR, pp. 304-313), §8.7.26 p427 (RCC_BDCR),
# Table 17 p160; DS12117 Rev 10 Table 111 p210; the rulings of 2026-11-26T09:31Z (Q5-Q13), 10:08Z (Q17) and 11:06Z (Q19-Q21).
set -euo pipefail

here=$(cd "$(dirname "$0")/../.." && pwd)
IMAGE=$(sed -n 's/^IMAGE=//p' "$here/tests/spike.sh")
CASES="second same ap gen order clockfirst invalid vosearly voslow cr2dbp bdcr bdcrok flashvos3 apbvos3 vos0 revy200 hse400 hse400v3 hse25 bothrec flashlow clockearly vos0byp bdcrbyte noclock nopwr notjudged reset recorded locked nibble norev revy byte misaligned read addr norcc registrations fields ledger regen"
MONITOR="reset recorded locked nibble norev revy byte misaligned read addr norcc registrations fields ledger regen"
PATHS=$(for c in $CASES; do case " $MONITOR " in *" $c "*) ;; *) printf '"%s",' "$c" ;; esac; done | sed 's/,$//')
scratch=${TVIBURI_SCRATCH:-$HOME/.cache/tviburi}
mkdir -p "$scratch"
work=$(mktemp -d "$scratch/pwr.XXXXXX")

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
BOARD_GUARDS=$("$src/scripts/guards")   # the one guard list (scripts/guards; TVI-WO-1204 (6))   # the board has no PWR guard (Q17 (a)): run_case adds pwr

fails=0
check() { if eval "$2"; then echo "ok   $1"; else echo "FAIL $1"; fails=$((fails + 1)); fi; }

# One container: build the check and (for a firmware case) the planted firmware, run Renode on the case's monitor script, then the
# check on the log, naming every guarded source. Outputs in $work/<name>. The platform: the board, PLATFORM_SED applied to it, then
# (unless NOPWR=1) the PWR restatement and `tviburiPwr` (enforcing, revision "V"); PLATFORM, another platform file instead, whose
# guards are PLATFORM_GUARDS.
run_case() { # run_case <name> <firmware path or -> <monitor lines...>
  local name=$1 fw=$2 guards="$BOARD_GUARDS pwr"; shift 2
  mkdir -p "$work/$name"
  if [ -n "${PLATFORM:-}" ]; then
    sed 's#^using "\./#using "/repo/model/#' "$src/${PLATFORM#/repo/}" > "$work/$name/platform.repl"
    guards=$PLATFORM_GUARDS
  else
    {
      sed -e 's#^using "\./#using "/repo/model/#' "$src/model/tviburi-nucleo-h753zi.repl"
      if [ -z "${NOPWR:-}" ]; then
        echo; cat "$src/model/stm32h7-pwr-restate.repl"; printf '\ntviburiPwr:\n    mode: "enforcing"\n    revision: "V"\n'
      fi
    } | sed -e "${PLATFORM_SED:-}" > "$work/$name/platform.repl"
    [ -z "${NOPWR:-}" ] || guards=$BOARD_GUARDS
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
        --check-cfg "cfg(path, values($3))" -C link-arg=-T/repo/firmware/link.x -o /out/fw.elf /repo/firmware/pwr.rs || exit 91
    fi
    timeout 120 renode --disable-gui --console --plain /out/run.resc </dev/null >/out/renode.out 2>&1
    echo "renode-exit=$?"
    /out/target/debug/tviburi-check "$v" /out/renode-log.txt $2
  ' _ "$fw" "$guards" "$PATHS" </dev/null >"$work/$name.stdout" 2>&1 || rc=$?
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
refuses() { # refuses <name> <rule> <kind> <python expr over r> [peripheral]: the first record, located (a PC), and the only one
  [ "$rc" = 3 ] && records "$1" "len(recs) == 1" && first "$1" "r[\"rule\"] == \"$2\" and r[\"kind\"] == \"$3\" and r[\"peripheral\"] == \"sysbus.${5:-pwr}\" and r[\"access\"][\"pc\"] != \"none\" and $4"
}
logged() { # logged <name> <python expr over `recs`, the records under <source> in the log, in order> [source]
  python3 -I -c '
import json, sys
s = sys.argv[3] + ": {\"tviburi\":\""
recs = [json.loads(l.split(sys.argv[3] + ": ", 1)[1]) for l in open(sys.argv[1]) if s in l and "\"rule\"" in l]
sys.exit(0 if eval("(" + sys.argv[2] + ")") else 1)
' "$work/$1/renode-log.txt" "$2" "${3:-pwr}"
}
marks=('echo "TVIBURI-MARK-AFTER"' 'sysbus ReadDoubleWord 0x20000104' 'echo "R-READ"' 'sysbus ReadDoubleWord 0x20000108' 'echo "R-READ2"' 'sysbus ReadDoubleWord 0x2000010C')
reads=('echo "R-CR3"' 'sysbus ReadDoubleWord 0x5802480C' 'echo "R-CSR1"' 'sysbus ReadDoubleWord 0x58024804' 'echo "R-D3CR"' 'sysbus ReadDoubleWord 0x58024818' 'echo "R-CR2"' 'sysbus ReadDoubleWord 0x58024808')
rec='/^tviburiPwr:$/{n;s/enforcing/recorded/}'
hse16='s/^    hseFrequency: 8000000$/    hseFrequency: 16000000/'

for t in ${TVIBURI_TESTS:-$CASES}; do
  case " $MONITOR " in *" $t "*) mon=1 ;; *) mon=0 ;; esac
  if [ $mon = 0 ]; then
    if [ "$t" = gen ]; then
      PLATFORM_SED=$hse16 run_case gen gen 'emulation RunFor "0.01"' "${marks[@]}"
    elif [ "$t" = revy200 ]; then
      PLATFORM_SED='/^tviburiPwr:$/,${s/^    revision: "V"$/    revision: "Y"/}' run_case revy200 revy200 'emulation RunFor "0.01"' "${marks[@]}"
    elif [ "$t" = noclock ]; then
      PLATFORM_SED='/^tviburiFlash:$/{n;s/enforcing/recorded/}' run_case noclock noclock 'emulation RunFor "0.01"' "${marks[@]}" "${reads[@]}"
    elif [ "$t" = nopwr ]; then
      NOPWR=1 PLATFORM_SED='$a pwr: @none' run_case nopwr nopwr 'emulation RunFor "0.01"' "${marks[@]}"
    elif [ "$t" = hse400 ] || [ "$t" = hse400v3 ]; then
      PLATFORM_SED=$hse16 run_case "$t" "$t" 'emulation RunFor "0.01"' "${marks[@]}"
    elif [ "$t" = hse25 ]; then
      PLATFORM_SED='s/^    hseFrequency: 8000000$/    hseFrequency: 25000000/' run_case hse25 hse25 'emulation RunFor "0.01"' "${marks[@]}"
    elif [ "$t" = bothrec ]; then
      PLATFORM_SED='/^tviburiRcc:$/,/^    mode:/{s/enforcing/recorded/}' run_case bothrec bothrec 'emulation RunFor "0.01"' "${marks[@]}"
    elif [ "$t" = notjudged ]; then
      NOPWR=1 run_case notjudged notjudged 'emulation RunFor "0.01"' "${marks[@]}"
    else
      run_case "$t" "$t" 'emulation RunFor "0.01"' "${marks[@]}" "${reads[@]}"
    fi
  fi
  case $t in
  second) check "second (WO acceptance 2): CR3 = 0x02, then 0x06, which changes the lower byte after its first write, refuses TVI-PWR-SUPPLY, predicted-fault, located, and is ignored as silicon ignores it (CR3 reads 0x02; p308)" \
      "refuses second TVI-PWR-SUPPLY predicted-fault 'r[\"register\"] == \"PWR_CR3\" and r[\"bit\"] == \"lower byte = 0x06\" and \"written once after POR\" in r[\"condition\"] and \"p307-308\" in r[\"citation\"]' && [ \"\$(readout second CR3)\" = 0x00000002 ]" ;;
  same) check "same (WO acceptance 2): ArduPilot's CR3 = 0x02 then 0x01000002 (the lower byte unchanged, USB33DEN set) passes; CR3 reads 0x05000002 (USB33RDY at once, ruled Q5)" \
      "passes same && [ \"\$(readout same READ)\" = 0x05000002 ]" ;;
  ap) check "ap: ArduPilot's PWR sequence exactly (CR3 0x2, ACTVOSRDY, CR1 0xF000C000, CR2 0x1 with DBP 0, CR3 0x1000002, CPUCR 0, VOS 11, VOSRDY, CR1 with DBP), its RCC to 400/200/100 MHz at Scale 1's limits, and RCC_BDCR with DBP 1, pass on revision V; CR2 reads 0 (BREN not taken, ruled Q12), D3CR 0xE000" \
      "passes ap && [ \"\$(readout ap READ)\" = 0x00000000 ] && [ \"\$(readout ap READ2)\" = 0x0000E000 ]" ;;
  gen) check "gen: the 14 generated boots' PWR sequence (CR3 read 0x46, CR3 = 0x42, read 0x42, ACTVOSRDY, VOS 10, VOSRDY) and hse-crystal-16's RCC to 64 MHz pass (inside Scale 2, p210)" \
      "passes gen && [ \"\$(readout gen READ)\" = 0x00000046 ] && [ \"\$(readout gen READ2)\" = 0x00000042 ]" ;;
  order) check "order (WO acceptance 3, the passing order): VOS1 and VOSRDY, then 400/200/100 MHz; then sys_ck back to HSI and VOS3 (p310's order both ways): passes; D3CR reads 0xE000 at Scale 1, CSR1 0x6000 at the end" \
      "passes order && [ \"\$(readout order READ)\" = 0x0000E000 ] && [ \"\$(readout order READ2)\" = 0x00006000 ]" ;;
  clockfirst) check "clockfirst (WO acceptance 3): sys_ck raised to 400 MHz at Scale 3 refuses TVI-RCC-VOS, predicted-fault, at RCC_CFGR, located, naming the CPU clock, VOS3's 200 MHz and p210" \
      "refuses clockfirst TVI-RCC-VOS predicted-fault 'r[\"register\"] == \"RCC_CFGR\" and r[\"bit\"] == \"CPU (rcc_c_ck) above VOS3\" and \"400000000 Hz, above 200 MHz at VOS3\" in r[\"condition\"] and \"p210\" in r[\"citation\"] and \"p310\" in r[\"citation\"]' rcc" ;;
  invalid) check "invalid: CR3 = 0x03 (LDOEN = BYPASS = 1, illegal, Table 34 p261) refuses TVI-PWR-SUPPLY, predicted-fault, and is ignored: CR3 keeps 0x46, ACTVOSRDY stays 0 (CSR1 0x4000; p308)" \
      "refuses invalid TVI-PWR-SUPPLY predicted-fault 'r[\"bit\"] == \"PWR_CR3 = 0x00000003\" and \"illegal combination\" in r[\"condition\"]' && [ \"\$(readout invalid CR3)\" = 0x00000046 ] && [ \"\$(readout invalid CSR1)\" = 0x00004000 ]" ;;
  vosearly) check "vosearly: VOS = 11 before any supply configuration (ACTVOSRDY 0) refuses TVI-PWR-VOS, unmodeled (p262); D3CR keeps 0x6000" \
      "refuses vosearly TVI-PWR-VOS unmodeled 'r[\"bit\"] == \"VOS (15:14) = 11\" and \"p262\" in r[\"citation\"]' && [ \"\$(readout vosearly D3CR)\" = 0x00006000 ]" ;;
  voslow) check "voslow: VOS lowered from Scale 1 to Scale 3 under 400 MHz refuses TVI-PWR-VOS, predicted-fault (p310's second sentence, p286, p210); D3CR keeps 0xE000" \
      "refuses voslow TVI-PWR-VOS predicted-fault '\"lowered from Scale 1 to Scale 3\" in r[\"condition\"] and \"CPU (rcc_c_ck) at 400000000 Hz, above 200 MHz at VOS3\" in r[\"condition\"] and \"p210\" in r[\"citation\"]' && [ \"\$(readout voslow D3CR)\" = 0x0000E000 ]" ;;
  cr2dbp) check "cr2dbp: CR2.MONEN = 1 with DBP = 0 refuses TVI-PWR-DBP, unmodeled (write-protected, p306; what it does is not stated); CR2 reads 0" \
      "refuses cr2dbp TVI-PWR-DBP unmodeled 'r[\"register\"] == \"PWR_CR2\" and \"p306\" in r[\"citation\"]' && [ \"\$(readout cr2dbp CR2)\" = 0x00000000 ]" ;;
  bdcr) check "bdcr (ruled Q10): RCC_BDCR.RTCEN = 1 with the PWR guard's DBP = 0 refuses TVI-RCC-DBP, unmodeled (p427), located" \
      "refuses bdcr TVI-RCC-DBP unmodeled 'r[\"register\"] == \"RCC_BDCR\" and \"p427\" in r[\"citation\"]' rcc" ;;
  bdcrok) check "bdcrok: DBP = 1, then RCC_BDCR.RTCEN = 1 passes; BDCR reads 0x8000" \
      "passes bdcrok && [ \"\$(readout bdcrok READ)\" = 0x00008000 ]" ;;
  flashvos3) check "flashvos3 (ruled Q21): with VOS 00 (Scale 3, p310) the flash guard asks the PWR guard: LATENCY 1 at an AXI clock of 100 MHz (enough at VOS1) refuses TVI-FLASH-WAIT, predicted-fault, at RCC_CFGR, naming VOS3 (Table 17 p160 needs 2); the clocks are within VOS3's limits, so no TVI-RCC-VOS" \
      "refuses flashvos3 TVI-FLASH-WAIT predicted-fault '\"100000000 Hz at VOS3: Table 17 needs 2\" in r[\"condition\"] and r[\"access\"][\"offset\"] == \"0x58024410\"' flashController" ;;
  apbvos3) check "apbvos3: at Scale 3, APB4 (RTCAPBEN enabled from reset, p471) brought to 32 MHz by D3PPRE (passes), APB1 at 64 MHz with no APB1 peripheral enabled (not judged), then USART3EN refuses TVI-RCC-VOS, predicted-fault, at RCC_APB1LENR: VOS3's 50 MHz holds \"when at least one peripheral is enabled\" (DS12117 p210 note 6)" \
      "refuses apbvos3 TVI-RCC-VOS predicted-fault 'r[\"register\"] == \"RCC_APB1LENR\" and r[\"bit\"] == \"APB1 (rcc_pclk1) above VOS3\"' rcc" ;;
  vos0) check "vos0: at Scale 1, 480 MHz, AHB 240 and every APB 120 (above VOS1's limits, within VOS0's, p210) refuse TVI-RCC-VOS, unmodeled, at RCC_CFGR: VOS0 is VOS1 with SYSCFG_PWRCR.ODEN (p280-281, p561), which the twin cannot read" \
      "refuses vos0 TVI-RCC-VOS unmodeled 'r[\"register\"] == \"RCC_CFGR\" and \"480000000 Hz, above 400 MHz at VOS1\" in r[\"condition\"] and \"within VOS0\" in r[\"condition\"]' rcc" ;;
  revy200) check "revy200: on revision Y at Scale 3, 200 MHz with AHB and every APB at 200 MHz passes: rev Y's datasheet states only a CPU limit per scale (DS12117 Rev 10 p129-130), so AHB and APB are not judged; CFGR reads SWS = PLL1 (0x1B)" \
      "passes revy200 && [ \"\$(readout revy200 READ)\" = 0x0000001B ]" ;;
  flashlow) check "flashlow: VOS lowered from Scale 1 to Scale 2 with LATENCY 0 at 64 MHz (Table 17 p160 needs 1 at VOS2) refuses TVI-FLASH-WAIT, predicted-fault, located at the PWR_D3CR write (p310), not at a later write" \
      "refuses flashlow TVI-FLASH-WAIT predicted-fault 'r[\"access\"][\"offset\"] == \"0x58024818\" and \"64000000 Hz at VOS2: Table 17 needs 1\" in r[\"condition\"] and \"at the PWR_D3CR write\" in r[\"condition\"] and \"p310\" in r[\"citation\"]' flashController" ;;
  clockearly) check "clockearly: sys_ck changed to 160 MHz before any supply configuration (ACTVOSRDY 0) refuses TVI-RCC-VOS, unmodeled, at RCC_CFGR, citing p308 and p262" \
      "refuses clockearly TVI-RCC-VOS unmodeled 'r[\"register\"] == \"RCC_CFGR\" and r[\"bit\"] == \"sys_ck before the supply configuration\" and \"p308\" in r[\"citation\"] and \"p262\" in r[\"citation\"]' rcc" ;;
  vos0byp) check "vos0byp: the LDO bypassed (CR3 0x01), Scale 1, 480/240/120 MHz refuse TVI-RCC-VOS, predicted-fault: VOS0 needs the LDO (DS12117 Rev 10 p210 note 4)" \
      "refuses vos0byp TVI-RCC-VOS predicted-fault '\"VOS0 is available only when the LDO regulator is ON\" in r[\"condition\"] and \"480000000 Hz, above 400 MHz at VOS1\" in r[\"condition\"]' rcc" ;;
  bdcrbyte) check "bdcrbyte: a byte write of RCC_BDCR's RTCEN byte with DBP = 0 refuses TVI-RCC-DBP, unmodeled, an 8-bit access" \
      "refuses bdcrbyte TVI-RCC-DBP unmodeled 'r[\"access\"][\"width\"] == 8 and r[\"access\"][\"offset\"] == \"0x71\"' rcc" ;;
  noclock) check "noclock: (flash recorded) VOS lowered under a sys_ck the twin cannot compute (DIVP1 /3, p404) refuses TVI-PWR-VOS, unmodeled; D3CR keeps 0xE000" \
      "[ \$rc = 3 ] && logged noclock 'len(recs) == 1 and recs[0][\"rule\"] == \"TVI-PWR-VOS\" and recs[0][\"kind\"] == \"unmodeled\" and \"cannot be computed\" in recs[0][\"condition\"] and recs[0][\"access\"][\"pc\"] != \"none\"' && [ \"\$(readout noclock D3CR)\" = 0x0000E000 ]" ;;
  nopwr) check "nopwr: with nothing at PWR_D3CR (no pwr at all), LATENCY 1 at 200 MHz refuses TVI-FLASH-WAIT, decided at VOS1, its record saying no peripheral is at PWR_D3CR (not that it reads VOS 00)" \
      "refuses nopwr TVI-FLASH-WAIT predicted-fault '\"no peripheral at PWR_D3CR\" in r[\"condition\"] and \"reads VOS 00\" not in r[\"condition\"]' flashController" ;;
  hse400) check "hse400 (B1): HSE 16 MHz / 7 x 350 / 2 = 400 MHz exactly at Scale 1, AHB 200, APB 100 (Scale 1's limits, p210) passes: each bus clock is compared in whole hertz; CFGR reads SWS = PLL1" \
      "passes hse400 && [ \"\$(readout hse400 READ)\" = 0x0000001B ]" ;;
  hse400v3) check "hse400v3 (B1): the same sys_ck with D1CPRE /2 at Scale 3: CPU 200, AHB 100, every APB 50 with RTCAPBEN enabled (Scale 3's limits, p210) passes" \
      "passes hse400v3 && [ \"\$(readout hse400v3 READ)\" = 0x0000001B ]" ;;
  hse25) check "hse25 (B1): HSE 25 MHz / 11 x 352 / 2 = 400 MHz exactly at Scale 1, AHB 200, APB 100 passes" \
      "passes hse25 && [ \"\$(readout hse25 READ)\" = 0x0000001B ]" ;;
  bothrec) check "bothrec (TVI-WO-1204's contract): rcc recorded, one RCC_APB1LENR write clearing USART3EN under UE = 1 records two would-refuses from that one access, TVI-RCC-VOS (APB4 at 64 MHz above VOS3's 50, the new first excess) then TVI-RCC-CLOCK (USART3's kernel clock stopped): every rule evaluated; never a pass" \
      "[ \$rc = 1 ] && logged bothrec '[r[\"rule\"] for r in recs if r[\"access\"][\"offset\"] == \"0xE8\" and r[\"access\"][\"value\"] == \"0x0\"] == [\"TVI-RCC-VOS\", \"TVI-RCC-CLOCK\"] and all(r[\"tviburi\"] == \"would-refuse\" for r in recs)' rcc" ;;
  notjudged) check "notjudged (ruled Q20 (A)): on the board with no PWR guard, sys_ck 400 MHz, AHB 200 and every APB 200 MHz (above every scale's limit, p210) are not judged: no TVI-RCC-VOS, no record; CFGR reads SWS = PLL1 (0x1B), D1CFGR 0x08 (read by the firmware, not a record)" \
      "passes notjudged && logged notjudged 'len(recs) == 0' rcc && [ \"\$(readout notjudged READ)\" = 0x0000001B ] && [ \"\$(readout notjudged READ2)\" = 0x00000008 ]" ;;
  reset)
    run_case reset - 'sysbus WriteDoubleWord 0x5802480C 0x2' 'machine Reset' "${reads[@]}" 'sysbus WriteDoubleWord 0x5802480C 0x6' 'echo "R-AFTER"' 'sysbus ReadDoubleWord 0x5802480C'
    check "reset (ruled Q6): CR3 = 0x02, then machine Reset: CR3 still 0x02 and ACTVOSRDY 1 (\"reset only by POR\", p307), D3CR back to Scale 3 (0x6000, p280); CR3 = 0x06 then refuses TVI-PWR-SUPPLY, predicted-fault, and CR3 stays 0x02" \
      "[ \$rc = 3 ] && records reset 'len(recs) == 1 and recs[0][\"rule\"] == \"TVI-PWR-SUPPLY\" and recs[0][\"kind\"] == \"predicted-fault\" and recs[0][\"bit\"] == \"lower byte = 0x06\"' && [ \"\$(readout reset CR3)\" = 0x00000002 ] && [ \"\$(readout reset CSR1)\" = 0x00006000 ] && [ \"\$(readout reset D3CR)\" = 0x00006000 ] && [ \"\$(readout reset AFTER)\" = 0x00000002 ]" ;;
  recorded)
    PLATFORM_SED=$rec run_case recorded - 'sysbus WriteDoubleWord 0x5802480C 0x2' 'sysbus WriteDoubleWord 0x5802480C 0x1000006' "${reads[@]}"
    check "recorded (ruled Q6): pwr recorded: CR3 = 0x02, then 0x01000006 records one would-refuse (TVI-PWR-SUPPLY) and the lower byte is still ignored, the upper byte taken: CR3 reads 0x05000002; never a pass at the check" \
      "[ \$rc = 1 ] && logged recorded 'len(recs) == 1 and recs[0][\"tviburi\"] == \"would-refuse\" and recs[0][\"rule\"] == \"TVI-PWR-SUPPLY\"' && [ \"\$(readout recorded CR3)\" = 0x05000002 ]" ;;
  locked)
    PLATFORM_SED=$rec run_case locked - 'sysbus WriteDoubleWord 0x5802480C 0x3' 'sysbus WriteDoubleWord 0x5802480C 0x2' "${reads[@]}" 'sysbus WriteDoubleWord 0x58024818 0xC000'
    check "locked (p308): pwr recorded: CR3 = 0x03 (illegal) then 0x02: two would-refuses (TVI-PWR-SUPPLY), CR3 keeps 0x46, ACTVOSRDY 0 (CSR1 0x4000); a VOS write then records TVI-PWR-VOS (ACTVOSRDY 0)" \
      "[ \$rc = 1 ] && logged locked '[r[\"rule\"] for r in recs] == [\"TVI-PWR-SUPPLY\", \"TVI-PWR-SUPPLY\", \"TVI-PWR-VOS\"] and \"after an invalid LDOEN/BYPASS pair locked it\" in recs[1][\"condition\"]' && [ \"\$(readout locked CR3)\" = 0x00000046 ] && [ \"\$(readout locked CSR1)\" = 0x00004000 ]" ;;
  nibble)
    run_case nibble - 'sysbus WriteDoubleWord 0x58024800 0xF000C000' 'echo "R-F"' 'sysbus ReadDoubleWord 0x58024800' 'sysbus WriteDoubleWord 0x58024800 0x0000C100' 'echo "R-0"' 'sysbus ReadDoubleWord 0x58024800' \
      'sysbus WriteDoubleWord 0x58024800 0x5000C000' 'echo "R-5"' 'sysbus ReadDoubleWord 0x58024800'
    check "nibble (ruled Q11): CR1 bits 31:28 = 1111 (p304's printed reset value) and 0000 (p313's map) pass and read back; 0101 refuses TVI-PWR-FIELD, unmodeled, and CR1 keeps 0x0000C100" \
      "[ \$rc = 3 ] && records nibble 'len(recs) == 1 and recs[0][\"rule\"] == \"TVI-PWR-FIELD\" and recs[0][\"bit\"] == \"Res31_28 (31:28) = 0101\" and \"p304\" in recs[0][\"citation\"]' && [ \"\$(readout nibble F)\" = 0xF000C000 ] && [ \"\$(readout nibble 0)\" = 0x0000C100 ] && [ \"\$(readout nibble 5)\" = 0x0000C100 ]" ;;
  norev)
    PLATFORM_SED='/^tviburiPwr:$/,${/^    revision: "V"$/d}' run_case norev - 'sysbus ReadDoubleWord 0x58024804'
    check "norev (ruled Q7): a PWR guard with no revision refuses at load, TVI-PWR-CONFIG, unmodeled, by name, and every access after" \
      "[ \$rc = 3 ] && records norev 'len(recs) == 2 and all(r[\"rule\"] == \"TVI-PWR-CONFIG\" for r in recs) and recs[0][\"bit\"] == \"revision\" and \"no revision declared\" in recs[0][\"condition\"]'" ;;
  revy)
    PLATFORM_SED='/^tviburiPwr:$/,${s/^    revision: "V"$/    revision: "Y"/}' run_case revy - 'echo "R-CR3"' 'sysbus ReadDoubleWord 0x5802480C' 'sysbus WriteDoubleWord 0x5802480C 0x42'
    check "revy: on revision Y CR3 reads 0x06 from reset (p307); bit 6 written 1 refuses TVI-PWR-FIELD, unmodeled (reserved, reset 0 on rev Y)" \
      "[ \$rc = 3 ] && [ \"\$(readout revy CR3)\" = 0x00000006 ] && records revy 'len(recs) == 1 and recs[0][\"rule\"] == \"TVI-PWR-FIELD\" and recs[0][\"bit\"] == \"Res6 (bit 6) = 1\"'" ;;
  byte)
    run_case byte - 'sysbus WriteByte 0x5802480C 0x2' 'sysbus WriteByte 0x5802480F 0x1' 'sysbus WriteWord 0x58024818 0x8000' "${reads[@]}"
    check "byte (p304): a byte write of the lower byte (0x02, the supply configuration), a byte write of USB33DEN and a half-word write of VOS 10 are taken: CR3 reads 0x05000002, D3CR 0xA000, ACTVOSRDY 1" \
      "passed byte && [ \"\$(readout byte CR3)\" = 0x05000002 ] && [ \"\$(readout byte D3CR)\" = 0x0000A000 ] && [ \"\$(readout byte CSR1)\" = 0x0000A000 ]" ;;
  misaligned)
    run_case misaligned - 'sysbus WriteWord 0x5802480D 0x100' "${reads[@]}"
    check "misaligned: a half-word write at offset 0xD refuses TVI-PWR-WIDTH, unmodeled, and changes nothing (CR3 0x46)" \
      "[ \$rc = 3 ] && records misaligned 'len(recs) == 1 and recs[0][\"rule\"] == \"TVI-PWR-WIDTH\" and recs[0][\"bit\"] == \"misaligned 16-bit write\"' && [ \"\$(readout misaligned CR3)\" = 0x00000046 ]" ;;
  read)
    run_case read - 'sysbus ReadDoubleWord 0x58024814'
    check "read: a read of 0x14 (Reserved, Table 49 p313) refuses TVI-PWR-READ, unmodeled" \
      "[ \$rc = 3 ] && records read 'len(recs) == 1 and recs[0][\"rule\"] == \"TVI-PWR-READ\" and recs[0][\"access\"][\"offset\"] == \"0x14\"'" ;;
  addr)
    PLATFORM_SED='s/^tviburiPwr: Tviburi.GuardedSTM32H7_PWR @ sysbus 0x58024800 /tviburiPwr: Tviburi.GuardedSTM32H7_PWR @ sysbus 0x58025800 /' run_case addr - 'sysbus ReadDoubleWord 0x58025804'
    check "addr: a PWR guard declared at 0x58025800, not PWR's 0x58024800 (Table 8 p132), refuses its access, TVI-PWR-CONFIG, unmodeled" \
      "[ \$rc = 3 ] && records addr 'len(recs) == 1 and recs[0][\"rule\"] == \"TVI-PWR-CONFIG\" and \"bus address 0x58025800\" in recs[0][\"condition\"]'" ;;
  norcc)
    PLATFORM=/repo/tests/1308/pwr-norcc.repl PLATFORM_GUARDS=pwr run_case norcc - 'sysbus WriteDoubleWord 0x5802480C 0x2' 'sysbus WriteDoubleWord 0x58024818 0xC000' 'sysbus WriteDoubleWord 0x58024818 0x4000'
    check "norcc: a PWR guard with no RCC guard on the machine: VOS lowered from Scale 1 to Scale 3 refuses TVI-PWR-VOS, unmodeled: the clocks cannot be read" \
      "[ \$rc = 3 ] && records norcc 'len(recs) == 1 and recs[0][\"rule\"] == \"TVI-PWR-VOS\" and recs[0][\"kind\"] == \"unmodeled\" and \"no RCC guard\" in recs[0][\"condition\"]'" ;;
  registrations)
    run_case registrations - 'peripherals'
    check "registrations (ruled Q13): peripherals lists pwr once, as the guard; its witness names revision V" \
      "passed registrations && [ \$(grep -c '(GuardedSTM32H7_PWR)' '$work/registrations/renode.out') = 1 ] && grep -q ' pwr (GuardedSTM32H7_PWR)' '$work/registrations/renode.out' \
       && grep -q 'pwr: {\"tviburi\":\"witness\".*\"guard\":\"GuardedSTM32H7_PWR\".*\"revision\":\"V\"' '$work/registrations/renode-log.txt'" ;;
  fields)
    # Gate clause 6 (RUL-17), the cases generated from the release's ledger, pwr recorded (every rule evaluated): each field row's
    # disallowed value records TVI-PWR-FIELD naming the field; each allowed value records none; every offset with no register
    # records one. Other rules' records (TVI-PWR-SUPPLY, -VOS, -DBP: the once-only CR3, VOS before ACTVOSRDY, CR2 under DBP 0)
    # are not the allow-list's and are left out; BREN = 1 under DBP 0 is ArduPilot's acknowledged write (ruled Q12), not taken.
    mkdir -p "$work/fields-cases"
    container -v "$src:/repo:ro,Z" -v "$work/fields-cases:/out:Z" "$IMAGE" sh -c '/repo/scripts/build-release >/dev/null &&
      /out/release.target/debug/tviburi-release cases /out/release/LEDGER.jsonl Tviburi.GuardedSTM32H7_PWR 0x58024800 0x400 >/out/cases.txt' </dev/null >"$work/fields-cases.stdout" 2>&1
    mapfile -t lines < "$work/fields-cases/cases.txt"
    PLATFORM_SED=$rec run_case fields - "${lines[@]}"
    check "fields: every ledger row's disallowed value records TVI-PWR-FIELD naming it, unmodeled; every allowed value records none; no-register offsets record one (gate clause 6)" \
      "[ \$rc = 1 ] && python3 -I - '$work/fields-cases/cases.txt' '$work/fields/renode-log.txt' <<'EOF'
import json, sys
cases = {}
for l in open(sys.argv[1]):
    if l.startswith('# CASE '):
        kind, name, off, val = l.split()[2:6]
        cases.setdefault((int(off, 16), int(val, 16)), (kind, name))
recs = [json.loads(l.split('pwr: ', 1)[1]) for l in open(sys.argv[2], errors='replace') if 'pwr: {\"tviburi\":\"would-refuse\"' in l]
field = [r for r in recs if r['rule'] == 'TVI-PWR-FIELD']
seen, bad = {}, []
for r in field:
    k = (int(r['access']['offset'], 16), int(r['access']['value'], 16))
    if k not in cases or k in seen:
        bad.append(('not one record per case', r['bit'])); continue
    seen[k] = r
    kind, name = cases[k]
    if kind == 'pass' or r['kind'] != 'unmodeled':
        bad.append((kind, name, r['bit']))
    elif not (r['bit'].startswith(name.split('.')[1] + ' (') if name != 'none' else r['bit'] == 'write to offset 0x%X' % k[0]):
        bad.append(('wrong field', name, r['bit']))
missing = [v for k, v in cases.items() if v[0] == 'refuse' and k not in seen and v[1] != 'PWR_CR2.BREN']
other = sorted({r['rule'] for r in recs} - {'TVI-PWR-FIELD'})
passes = sum(1 for v in cases.values() if v[0] == 'pass')
print(len(cases), 'cases;', passes, 'pass;', len(field), 'field records; other rules', other, 'bad', bad[:5], 'missing', missing[:5])
sys.exit(0 if not bad and not missing and len(cases) > 280 and passes > 20 and set(other) <= {'TVI-PWR-SUPPLY', 'TVI-PWR-VOS', 'TVI-PWR-DBP'} else 1)
EOF" ;;
  ledger)
    mkdir -p "$work/ledger"
    rc=0; container -v "$src:/repo:ro,Z" -v "$work/ledger:/out:Z" "$IMAGE" sh -c '/repo/scripts/build-release /repo /out/release >/dev/null' </dev/null >"$work/ledger.stdout" 2>&1 || rc=$?
    check "ledger (ruled Q19): the manifest restates pwr onto the PWR guard (its Python stub named by the class row), and the ledger says so: pwr is a model row guarded by it, not the stub's canned row (TVI-WO-2100 Q26), and fmc stays canned; the release carries the guard's 69 field rows over its 9 registers (DBP, VOS, the supply configuration and USB33DEN modeled; CR1 31:28 {0, 15}; the tags at their reset values) and bench rows P1-P5, and RCC's R12 for TVI-RCC-VOS, judged only with a PWR guard" \
      "[ \$rc = 0 ] && grep -qx 'class Python.PythonPeripheral Tviburi.GuardedSTM32H7_PWR' '$work/ledger/release/MANIFEST' && [ \"\$(grep -c '^restate Python.PythonPeripheral ' '$work/ledger/release/MANIFEST')\" = 1 ] && grep -q '^restate Python.PythonPeripheral pwr ' '$work/ledger/release/MANIFEST' && python3 -I -c '
import json, sys
rows = [json.loads(l) for l in open(sys.argv[1]).read().splitlines()[1:]]
g = \"Tviburi.GuardedSTM32H7_PWR\"
f = [r for r in rows if r.get(\"peripheral\") == g and r[\"row\"] == \"field\"]
by = {(r[\"register\"], r[\"field\"]): r for r in f}
ok = len(f) == 69 and len({r[\"register\"] for r in f}) == 9
ok = ok and all(by[(\"PWR_CR3\", x)][\"state\"] == \"guard\" for x in (\"SCUEN\", \"LDOEN\", \"BYPASS\", \"USB33DEN\", \"USBREGEN\"))
ok = ok and by[(\"PWR_D3CR\", \"VOS\")][\"state\"] == \"guard\" and by[(\"PWR_CR1\", \"DBP\")][\"allowed\"] == \"any\" and by[(\"PWR_CR2\", \"BREN\")][\"allowed\"] == [0]
ok = ok and by[(\"PWR_CR1\", \"Res31_28\")][\"allowed\"] == [0, 15] and by[(\"PWR_CR1\", \"SVOS\")][\"allowed\"] == [3]
ok = ok and \"no figure in RM0433 Rev 9 or DS12117 Rev 10\" in by[(\"PWR_D3CR\", \"VOSRDY\")][\"behaves_as\"]
b = {r[\"field\"]: r for r in rows if r[\"row\"] == \"bench\"}
ok = ok and all(b.get(x, {}).get(\"peripheral\") == g for x in (\"P1\", \"P2\", \"P3\", \"P4\", \"P5\"))
ok = ok and b[\"R12\"][\"rule\"] == \"TVI-RCC-VOS\" and \"judged only with a PWR guard on the platform, not judged otherwise\" in b[\"R12\"][\"procedure\"]
c = {r[\"register\"]: r for r in rows if r[\"row\"] == \"canned\" and r[\"peripheral\"] == \"Python.PythonPeripheral\"}
ok = ok and sorted(c) == [\"fmc\"]
m = [r for r in rows if r[\"row\"] == \"model\" and r[\"peripheral\"] == \"Python.PythonPeripheral\"]
ok = ok and len(m) == 1 and m[0].get(\"guarded_by\") == g and m[0][\"instances\"] == [\"pwr\"] and m[0][\"files\"][0].startswith(\"platforms/cpus/stm32h7.repl sha256:\")
sys.exit(0 if ok else 1)
' '$work/ledger/release/LEDGER.jsonl'" ;;
  regen)
    mkdir -p "$work/regen"
    rc=0; container -v "$src:/repo:ro,Z" -v "$work/regen:/out:Z" "$IMAGE" sh -c 'awk -v sha=$(sha256sum /opt/renode/platforms/cpus/stm32h7.repl | cut -c1-64) \
      -f /repo/model/stm32h7-pwr-restate.awk /opt/renode/platforms/cpus/stm32h7.repl > /out/restate.repl' </dev/null >"$work/regen.stdout" 2>&1 || rc=$?
    check "regen: the restatement regenerates from the pinned stm32h7.repl, byte for byte (RUL-09 (b))" \
      "[ \$rc = 0 ] && cmp -s '$work/regen/restate.repl' '$src/model/stm32h7-pwr-restate.repl'" ;;
  *) echo "unknown case: $t" >&2; exit 2 ;;
  esac
done
[ $fails -eq 0 ] && echo "ALL PASS" || echo "$fails FAILED"
[ $fails -eq 0 ]
