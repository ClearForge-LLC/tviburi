#!/usr/bin/env bash
# tests/1304/flash.sh [SRC] — TVI-WO-1304's acceptance cases for the flash guard, run on the host as tests/1302/timer.sh is:
# each case one container in the pinned tools image (tests/spike.sh's IMAGE), no network, the source read-only. SRC
# defaults to a `git archive HEAD` export. tests/spike.sh runs each case as flash-<case> in its acceptance group.
#   TVIBURI_TESTS="order clockfirst ..."  run only these cases.
#   TVIBURI_CI=1                          plain `podman run`, for a disposable CI runner only; elsewhere ~/bin/capped-run.
# Exit 0 only if every selected case passed. Pages: RM0433 Rev 9 chapter 4 (FLASH), Table 17 p160; the rulings of
# 2026-11-24T15:50Z (Q1-Q7) and the measurement in docs/work-orders/TVI-WO-1304.measure.md.
set -euo pipefail

here=$(cd "$(dirname "$0")/../.." && pwd)
IMAGE=$(sed -n 's/^IMAGE=//p' "$here/tests/spike.sh")
CASES="order clockfirst lowerfast prescaler alias divp1zero divp1odd vosunknown vosunknownlow vos3 over225 edge vos2 d1cpre wrhrcc lockedcr lockedsame locking badkey keytwice keymid keyhalf wrhlow wrhpg worse ap gen optsr optsrnone optwrite bank1 readreg fields ledger registrations recorded reset config regen"
# The planted firmware's paths (firmware/flash.rs): every case but optsrnone (optsr's firmware) and the monitor-driven ones.
PATHS=$(for c in $CASES; do case $c in optsrnone|fields|ledger|registrations|recorded|reset|config|regen) ;; *) printf '"%s",' "$c" ;; esac; done | sed 's/,$//')
scratch=${TVIBURI_SCRATCH:-$HOME/.cache/tviburi}
mkdir -p "$scratch"
work=$(mktemp -d "$scratch/flash.XXXXXX")

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
GUARDS=$("$src/scripts/guards")   # the one guard list (scripts/guards; TVI-WO-1204 (6))

fails=0
check() { if eval "$2"; then echo "ok   $1"; else echo "FAIL $1"; fails=$((fails + 1)); fi; }

# One container: build the check and (for a firmware case) the planted firmware, run Renode on the case's monitor
# script, then the check on the log, naming every guarded source. Outputs in $work/<name>. PLATFORM_SED: a sed script
# applied to the board; PWR=1 appends the consumer's VOS-storing pwr (tests/1304/pwr-vos.repl; ruled Q7).
run_case() { # run_case <name> <firmware path or -> <monitor lines...>
  local name=$1 fw=$2; shift 2
  mkdir -p "$work/$name"
  sed -e 's#^using "\./#using "/repo/model/#' -e "${PLATFORM_SED:-}" "$src/model/tviburi-nucleo-h753zi.repl" > "$work/$name/platform.repl"
  [ -z "${PWR:-}" ] || cat "$src/tests/1304/pwr-vos.repl" >> "$work/$name/platform.repl"
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
        --check-cfg "cfg(path, values($3))" -C link-arg=-T/repo/firmware/link.x -o /out/fw.elf /repo/firmware/flash.rs || exit 91
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
refuses() { [ "$rc" = 3 ] && first "$1" "r[\"rule\"] == \"$2\" and r[\"kind\"] == \"$3\" and r[\"peripheral\"] == \"sysbus.flashController\" and r[\"access\"][\"pc\"] != \"none\" and $4"; }
marks=('echo "TVIBURI-MARK-AFTER"' 'sysbus ReadDoubleWord 0x20000104' 'echo "R-READ"' 'sysbus ReadDoubleWord 0x20000108' 'echo "R-READ2"' 'sysbus ReadDoubleWord 0x2000010C')
hse16='s/^    hseFrequency: 8000000$/    hseFrequency: 16000000/'

for t in ${TVIBURI_TESTS:-$CASES}; do
  case $t in
  order|clockfirst|lowerfast|prescaler|alias|vos3|wrhlow|wrhpg|edge|vos2|d1cpre|wrhrcc|divp1zero|divp1odd)
    PWR=1 run_case "$t" "$t" 'emulation RunFor "0.001"' "${marks[@]}"
    case $t in
    order) check "order: VOS1, LATENCY raised before the clock to 200 MHz, the clock lowered before LATENCY (p160-161): passes; ACR reads 0" \
      "passes order && [ \"\$(readout order READ)\" = 0x00000000 ]" ;;
    clockfirst) check "clockfirst: the clock raised to an AXI clock of 200 MHz with LATENCY 0 at VOS1 refuses TVI-FLASH-WAIT, predicted-fault, at the RCC_CFGR write, naming the clock, the latency, the voltage scale and p160" \
      "refuses clockfirst TVI-FLASH-WAIT predicted-fault 'r[\"access\"][\"offset\"] == \"0x58024410\" and r[\"bit\"] == \"LATENCY = 0\" and \"200000000 Hz at VOS1: Table 17 needs 2\" in r[\"condition\"] and \"Table 17 p160\" in r[\"citation\"]'" ;;
    lowerfast) check "lowerfast: LATENCY lowered to 1 under 200 MHz at VOS1 refuses TVI-FLASH-WAIT at the ACR write" \
      "refuses lowerfast TVI-FLASH-WAIT predicted-fault 'r[\"access\"][\"offset\"] == \"0x0\" and r[\"bit\"] == \"LATENCY = 1\" and \"at the FLASH_ACR write\" in r[\"condition\"]'" ;;
    prescaler) check "prescaler: the clock raised from 100 to 200 MHz by HPRE alone, with LATENCY 1, refuses TVI-FLASH-WAIT at the RCC_D1CFGR write (p395)" \
      "refuses prescaler TVI-FLASH-WAIT predicted-fault 'r[\"access\"][\"offset\"] == \"0x58024418\" and \"HPRE = 1000\" in r[\"condition\"]'" ;;
    alias) check "alias: LATENCY lowered through ACR's bank 2 alias 0x100 (p201) refuses TVI-FLASH-WAIT there" \
      "refuses alias TVI-FLASH-WAIT predicted-fault 'r[\"access\"][\"offset\"] == \"0x100\" and r[\"bit\"] == \"LATENCY = 1\"'" ;;
    vos3) check "vos3: VOS left at Scale 3, LATENCY 2 at 200 MHz refuses TVI-FLASH-WAIT: Table 17 needs 4 at VOS3" \
      "refuses vos3 TVI-FLASH-WAIT predicted-fault '\"at VOS3: Table 17 needs 4\" in r[\"condition\"]'" ;;
    wrhlow) check "wrhlow: a sector erase started at 200 MHz with WRHIGHFREQ 01 refuses TVI-FLASH-PROG, predicted-fault, at the START2 write (p164, Table 17 p160)" \
      "refuses wrhlow TVI-FLASH-PROG predicted-fault 'r[\"access\"][\"offset\"] == \"0x10C\" and r[\"access\"][\"value\"] == \"0x7A4\" and r[\"bit\"] == \"WRHIGHFREQ = 01\" and \"p164\" in r[\"citation\"]'" ;;
    wrhpg) check "wrhpg: PG2 set at 200 MHz with WRHIGHFREQ 01 refuses TVI-FLASH-PROG at the PG2 write" \
      "refuses wrhpg TVI-FLASH-PROG predicted-fault 'r[\"access\"][\"value\"] == \"0x22\"'" ;;
    edge) check "edge: LATENCY 1 at an AXI clock of exactly 140 MHz at VOS1, the top of the band (Table 17 p160: ]70 MHz;140 MHz]), passes" \
      "passes edge && [ \"\$(readout edge READ)\" = 0x00000001 ]" ;;
    vos2) check "vos2: LATENCY 1 at 120 MHz at VOS2 refuses TVI-FLASH-WAIT: Table 17 needs 2 there (VOS1 would need 1)" \
      "refuses vos2 TVI-FLASH-WAIT predicted-fault '\"120000000 Hz at VOS2: Table 17 needs 2\" in r[\"condition\"]'" ;;
    d1cpre) check "d1cpre: the clock raised from 100 to 200 MHz by D1CPRE alone refuses TVI-FLASH-WAIT at the RCC_D1CFGR write (p394)" \
      "refuses d1cpre TVI-FLASH-WAIT predicted-fault 'r[\"access\"][\"offset\"] == \"0x58024418\" and \"D1CPRE = 0000\" in r[\"condition\"]'" ;;
    divp1zero|divp1odd) check "$t: sys_ck moved to PLL1 with DIVP1 = $([ $t = divp1zero ] && echo '0 (/1)' || echo '2 (/3)'): the RCC guard's BusHz has no frequency (\"DIVPx = 2,4,6,...128 when x = 1\", Figure 47 p346; p404; TVI-WO-1300c Q7), so TVI-FLASH-WAIT, unmodeled, at the RCC_CFGR write: the AXI clock cannot be computed" \
      "refuses $t TVI-FLASH-WAIT unmodeled 'r[\"access\"][\"offset\"] == \"0x58024410\" and \"cannot be computed\" in r[\"condition\"] and \"DIVP1 = $([ $t = divp1zero ] && echo 0 || echo 2) \" in r[\"condition\"] and \"Figure 47 p346\" in r[\"condition\"]'" ;;
    wrhrcc) check "wrhrcc: with PG2 set and WRHIGHFREQ 01, the clock raised to 200 MHz refuses TVI-FLASH-PROG at the RCC_CFGR write" \
      "refuses wrhrcc TVI-FLASH-PROG predicted-fault 'r[\"access\"][\"offset\"] == \"0x58024410\" and \"while PG is set\" in r[\"condition\"]'" ;;
    esac ;;
  vosunknown|vosunknownlow|over225|lockedcr|lockedsame|locking|badkey|keytwice|keymid|keyhalf|optsr|optwrite|bank1|readreg)
    run_case "$t" "$t" 'emulation RunFor "0.001"' "${marks[@]}"
    case $t in
    vosunknown) check "vosunknown: with the stock pwr (VOS reads 00), LATENCY 2 at 200 MHz is decided at VOS1 and passes (ruled Q1); ACR reads 0x22" \
      "passes vosunknown && [ \"\$(readout vosunknown READ)\" = 0x00000022 ]" ;;
    vosunknownlow) check "vosunknownlow: with VOS unknown, LATENCY 1 at 200 MHz is short even at VOS1: TVI-FLASH-WAIT, predicted-fault, its record saying it was decided at VOS1 (ruled Q1)" \
      "refuses vosunknownlow TVI-FLASH-WAIT predicted-fault '\"VOS unknown on this platform (PWR_D3CR reads VOS 00), decided at VOS1: Table 17 needs 2\" in r[\"condition\"]'" ;;
    over225) check "over225: an AXI clock of 400 MHz refuses TVI-FLASH-WAIT, unmodeled: above 225 MHz only VOS0, which cannot be read (ruled Q1)" \
      "refuses over225 TVI-FLASH-WAIT unmodeled '\"above 225 MHz\" in r[\"condition\"]'" ;;
    lockedcr) check "lockedcr: PG2 written while LOCK2 = 1 refuses TVI-FLASH-LOCK, predicted-fault (p182, p226-229)" \
      "refuses lockedcr TVI-FLASH-LOCK predicted-fault 'r[\"register\"] == \"FLASH_CR2\" and r[\"bit\"] == \"FLASH_CR2 bits 0x00000002 while LOCK2 = 1\" and \"p226-229\" in r[\"citation\"]'" ;;
    lockedsame) check "lockedsame: FLASH_CR2 written with the value it holds while locked passes (RUL-18 amended); it reads 0x31" \
      "passes lockedsame && [ \"\$(readout lockedsame READ)\" = 0x00000031 ]" ;;
    locking) check "locking: FLASH_CR2 written setting LOCK2 with PG2 refuses TVI-FLASH-LOCK: the other bits do not change (p229)" \
      "refuses locking TVI-FLASH-LOCK predicted-fault 'r[\"bit\"] == \"FLASH_CR2 bits 0x00000002 with LOCK2 set\"'" ;;
    badkey) check "badkey: a first key other than KEY1 refuses TVI-FLASH-KEY, predicted-fault (p182)" \
      "refuses badkey TVI-FLASH-KEY predicted-fault 'r[\"bit\"] == \"a first key other than KEY1 (0x45670123)\" and \"p182\" in r[\"citation\"]'" ;;
    keytwice) check "keytwice: a key written while FLASH_CR2 is unlocked refuses TVI-FLASH-KEY (p163)" \
      "refuses keytwice TVI-FLASH-KEY predicted-fault 'r[\"bit\"].startswith(\"a key written while FLASH_CR2 is unlocked\")'" ;;
    keymid) check "keymid: between bank 1's KEY1 and KEY2, an ACR write (common, Table 20 p172) passes and a CR1 write refuses TVI-FLASH-KEY (p182)" \
      "refuses keymid TVI-FLASH-KEY predicted-fault '\"between KEY1 and KEY2\" in r[\"bit\"] and r[\"access\"][\"offset\"] == \"0xC\" and r[\"register\"] == \"FLASH_CR1\"'" ;;
    keyhalf) check "keyhalf: KEY1 written by a 16-bit store refuses TVI-FLASH-KEY (p182: less than 32 bits)" \
      "refuses keyhalf TVI-FLASH-KEY predicted-fault 'r[\"bit\"] == \"a key written in less than 32 bits (16)\" and r[\"access\"][\"width\"] == 16'" ;;
    optsr) check "optsr: OPTSR_CUR and its alias 0x11C read the board's declared image, 0x1BC6AAF0, not the stock model's 0x0406AAF0 (ruled Q3)" \
      "passes optsr && [ \"\$(readout optsr READ)\" = 0x1BC6AAF0 ] && [ \"\$(readout optsr READ2)\" = 0x1BC6AAF0 ]" ;;
    optwrite) check "optwrite: an OPTKEYR write refuses TVI-FLASH-REG, unmodeled: option-byte programming is not modeled (ruled Q3)" \
      "refuses optwrite TVI-FLASH-REG unmodeled 'r[\"bit\"] == \"write to FLASH_OPTKEYR\" and \"Q3\" in r[\"citation\"]'" ;;
    bank1) check "bank1: PG1 (bank 1 programming) refuses TVI-FLASH-FIELD, unmodeled (WO (2), the allow-list)" \
      "refuses bank1 TVI-FLASH-FIELD unmodeled 'r[\"bit\"].startswith(\"FLASH_CR1.PG1 (bit 1) = 1\")'" ;;
    readreg) check "readreg: a FLASH_PRAR_CUR1 read refuses TVI-FLASH-READ, unmodeled" \
      "refuses readreg TVI-FLASH-READ unmodeled 'r[\"bit\"] == \"read of FLASH_PRAR_CUR1\"'" ;;
    esac ;;
  worse)
    PWR=1 PLATFORM_SED='/^tviburiFlash:$/{n;s/enforcing/recorded/}' run_case worse worse 'emulation RunFor "0.001"' "${marks[@]}"
    check "worse: (recorded) LATENCY 1 at 200 MHz, then LATENCY 0: each is a would-refuse, TVI-FLASH-WAIT, the second a new mismatch (RUL-19); ACR reads 0x10" \
      "[ \$rc = 1 ] && [ \$(grep -c '\"tviburi\":\"would-refuse\".*TVI-FLASH-WAIT' '$work/worse/renode-log.txt') = 2 ] && [ \"\$(readout worse READ)\" = 0x00000010 ]" ;;
  optsrnone)
    PLATFORM_SED='/^    optionBytes: /d' run_case optsrnone optsr 'emulation RunFor "0.001"'
    check "optsrnone: with no optionBytes declared, the OPTSR_CUR read refuses TVI-FLASH-OPTSR, unmodeled: the twin does not invent the option bytes (ruled Q3)" \
      "refuses optsrnone TVI-FLASH-OPTSR unmodeled 'r[\"access\"][\"offset\"] == \"0x1C\"'" ;;
  ap)
    run_case ap ap 'emulation RunFor "0.002"' "${marks[@]}" 'echo "R-WORD"' 'sysbus ReadDoubleWord 0x081E0000' 'echo "R-SECTOR"' 'sysbus ReadDoubleWord 0x081E0020'
    check "ap: unmodified ArduPilot's RCC sequence, PWR_D3CR (dropped by the stock pwr: VOS unknown, decided at VOS1), FLASH_ACR = 0x32 (WRHIGHFREQ 11, ruled Q2) and its bank 2 erase and program pass, enforcing; CR2 reads PSIZE2 as written (0x724, p228); SR2 is not set by its write (0, p230); the word programmed, the rest erased" \
      "passes ap && [ \"\$(readout ap READ)\" = 0x00000724 ] && [ \"\$(readout ap READ2)\" = 0x00000000 ] && [ \"\$(readout ap WORD)\" = 0x625B6851 ] && [ \"\$(readout ap SECTOR)\" = 0xFFFFFFFF ]" ;;
  gen)
    PWR=1 PLATFORM_SED="$hse16;"'s/^    optionBytes: 0x1BC6AAF0$/    optionBytes: 0x0406AAFC/' run_case gen gen 'emulation RunFor "0.001"' "${marks[@]}"
    check "gen: the generated hse-crystal-16's PWR, RCC and FLASH_ACR writes (VOS2, LATENCY 1 raised before the switch to 64 MHz) pass, enforcing, and its OPTSR_CUR read returns the declared word (the consumer's 0x0406AAFC)" \
      "passes gen && [ \"\$(readout gen READ)\" = 0x0406AAFC ]" ;;
  fields)
    # Gate clause 6 (RUL-17), the cases generated from the release's ledger: each field row's disallowed value refuses
    # TVI-FLASH-FIELD, unmodeled, naming the field; each allowed value passes; an offset with no modeled register refuses.
    # Every CR write is preceded by its bank's two keys (a write while locked is TVI-FLASH-LOCK's); a key register's
    # cases are wrong keys (TVI-FLASH-KEY); an alias (0x100, 0x11C) is its register.
    mkdir -p "$work/fields-cases"
    container -v "$src:/repo:ro,Z" -v "$work/fields-cases:/out:Z" "$IMAGE" sh -c '/repo/scripts/build-release >/dev/null &&
      /out/release.target/debug/tviburi-release cases /out/release/LEDGER.jsonl Tviburi.GuardedSTM32H7_Flash 0x52002000 0x1000 >/out/cases.txt' </dev/null >"$work/fields-cases.stdout" 2>&1
    sed -e 's/^\(sysbus WriteDoubleWord 0x5200200C .*\)$/sysbus WriteDoubleWord 0x52002004 0x45670123\nsysbus WriteDoubleWord 0x52002004 0xCDEF89AB\n\1/' \
      -e 's/^\(sysbus WriteDoubleWord 0x5200210C .*\)$/sysbus WriteDoubleWord 0x52002104 0x45670123\nsysbus WriteDoubleWord 0x52002104 0xCDEF89AB\n\1/' "$work/fields-cases/cases.txt" > "$work/fields-cases/run.txt"
    mapfile -t lines < "$work/fields-cases/run.txt"
    run_case fields - "${lines[@]}"
    check "fields: every ledger row's disallowed value refuses by name, unmodeled; allowed values pass; key registers refuse as keys; aliases pass as their registers; other offsets refuse (gate clause 6)" \
      "python3 -I - '$work/fields-cases/cases.txt' '$work/fields.stdout' <<'EOF'
import json, sys
cases = {}
for l in open(sys.argv[1]):
    if l.startswith('# CASE '):
        kind, name, off, val = l.split()[2:6]
        cases[(int(off, 16), int(val, 16))] = (kind, name)
recs = [json.loads(l[8:]) for l in open(sys.argv[2]) if l.startswith('REFUSAL ')]
seen, bad = {}, []
keys = {0x4: 0x45670123, 0x104: 0x45670123}
for r in recs:
    k = (int(r['access']['offset'], 16), int(r['access']['value'], 16))
    if k[0] in keys and k[1] in (0x45670123, 0xCDEF89AB):
        bad.append(('a right key refused', r['bit'])); continue
    if k not in cases or k in seen:
        bad.append(('not one record per case', r['rule'], r['bit'])); continue
    seen[k] = r
    kind, name = cases[k]
    if k[0] in keys:
        if r['rule'] != 'TVI-FLASH-KEY': bad.append(('key', r['rule']))
    elif k[0] in (0xC, 0x10C) and k[1] & 1 and r['rule'] == 'TVI-FLASH-LOCK':
        pass   # setting LOCK with other bits changed (p206, p229): the CR cases start from the unlocked 0x30
    elif k[0] in (0x100, 0x11C):
        bad.append(('alias refused', r['rule'], r['bit']))
    elif name == 'none':
        if r['rule'] != 'TVI-FLASH-REG' or r['kind'] != 'unmodeled': bad.append(('none', hex(k[0]), r['rule']))
    elif kind == 'pass' or r['rule'] != 'TVI-FLASH-FIELD' or r['kind'] != 'unmodeled':
        bad.append((kind, name, r['rule'], r['bit']))
    elif not r['bit'].startswith(name + ' ('):
        bad.append(('wrong field', name, r['bit']))
missing = [(v, hex(k[0])) for k, v in cases.items() if v[0] == 'refuse' and k not in seen and k[0] not in (0x100, 0x11C) and k[0] not in keys]
print(len(cases), 'cases;', len(recs), 'refusals;', 'bad', bad[:5], 'missing', missing[:5])
sys.exit(0 if not bad and not missing and len(cases) > 900 else 1)
EOF" ;;
  ledger)
    mkdir -p "$work/ledger"
    rc=0; container -v "$src:/repo:ro,Z" -v "$work/ledger:/out:Z" "$IMAGE" sh -c '/repo/scripts/build-release /repo /out/release >/dev/null' </dev/null >"$work/ledger.stdout" 2>&1 || rc=$?
    check "ledger: the release carries the flash guard's 138 field rows over its 10 registers, with VOS-unknown decided at VOS1 (Q1), WRHIGHFREQ 11 inferred with no page (Q2), PSIZE2 served as written (Table 18 p164), OPTSR_CUR from the image (Q3); bench rows F1-F5; the stock controller listed as guarded, on flashController" \
      "[ \$rc = 0 ] && python3 -I -c '
import json, sys
rows = [json.loads(l) for l in open(sys.argv[1]).read().splitlines()[1:]]
g = \"Tviburi.GuardedSTM32H7_Flash\"
f = [r for r in rows if r.get(\"peripheral\") == g and r[\"row\"] == \"field\"]
by = {(r[\"register\"], r[\"field\"]): r for r in f}
ok = len(f) == 138 and len({r[\"register\"] for r in f}) == 10
ok = ok and \"decided at VOS1\" in by[(\"FLASH_ACR\", \"LATENCY\")][\"behaves_as\"] and by[(\"FLASH_ACR\", \"LATENCY\")][\"allowed\"] == list(range(8))
ok = ok and \"inferred, no RM page\" in by[(\"FLASH_ACR\", \"WRHIGHFREQ\")][\"behaves_as\"]
ok = ok and by[(\"FLASH_CR2\", \"PSIZE2\")][\"state\"] == \"guard\" and \"Table 18 p164\" in by[(\"FLASH_CR2\", \"PSIZE2\")][\"behaves_as\"]
ok = ok and by[(\"FLASH_CR1\", \"PG1\")][\"allowed\"] == [0] and by[(\"FLASH_OPTSR_CUR\", \"BOR_LEV\")][\"state\"] == \"read-only\"
bench = sorted((r[\"field\"], r[\"rule\"]) for r in rows if r.get(\"peripheral\") == g and r[\"row\"] == \"bench\")
ok = ok and bench == [(\"F1\", \"TVI-FLASH-WAIT\"), (\"F2\", \"TVI-FLASH-PROG\"), (\"F3\", \"TVI-FLASH-PROG\"), (\"F4\", \"TVI-FLASH-KEY\"), (\"F5\", \"TVI-FLASH-LOCK\")]
m = [r for r in rows if r[\"row\"] == \"model\" and r[\"peripheral\"] == \"MTD.STM32H7_FlashController\"]
ok = ok and len(m) == 1 and m[0].get(\"guarded_by\") == g and m[0][\"instances\"] == [\"flashController\"]
sys.exit(0 if ok else 1)
' '$work/ledger/release/LEDGER.jsonl'" ;;
  registrations)
    run_case registrations - 'sysbus LogPeripheralAccess sysbus.flashController true' 'sysbus ReadDoubleWord 0x52002000' 'sysbus ReadDoubleWord 0x5200201C' 'peripherals'
    check "registrations: peripherals lists flashController as the guard, and the access log names its registers as the stock model's (AccessControl, OptionStatusCurrent; TVI-WO-1304.measure.md §2.4)" \
      "passed registrations && grep -q 'flashController (GuardedSTM32H7_Flash)' '$work/registrations/renode.out' && grep -q 'flashController: .*ReadUInt32 from 0x0 (AccessControl), returned 0x37' '$work/registrations/renode-log.txt' && grep -q 'flashController: .*ReadUInt32 from 0x1C (OptionStatusCurrent), returned 0x1BC6AAF0' '$work/registrations/renode-log.txt'" ;;
  recorded)
    PLATFORM_SED='/^tviburiFlash:$/{n;s/enforcing/recorded/}' run_case recorded - 'sysbus WriteDoubleWord 0x52002104 0x12345678' \
      'sysbus WriteDoubleWord 0x52002104 0x45670123' 'sysbus WriteDoubleWord 0x52002104 0xCDEF89AB' 'sysbus WriteDoubleWord 0x52002008 0x08192A3B' 'echo "R-CR2"' 'sysbus ReadDoubleWord 0x5200210C'
    check "recorded: flashController recorded: a wrong key, then both right keys (locked until reset, p182), and an OPTKEYR write each record a would-refuse and pass on (RUL-19); CR2 stays locked; never a pass at the check" \
      "[ \$rc = 1 ] && [ \$(grep -c '\"tviburi\":\"would-refuse\".*TVI-FLASH-' '$work/recorded/renode-log.txt') = 4 ] && [ \"\$(readout recorded CR2)\" = 0x00000031 ]" ;;
  reset)
    run_case reset - 'sysbus WriteDoubleWord 0x52002104 0x45670123' 'sysbus WriteDoubleWord 0x52002104 0xCDEF89AB' 'sysbus WriteDoubleWord 0x5200210C 0x20' \
      'echo "R-BEFORE"' 'sysbus ReadDoubleWord 0x5200210C' 'machine Reset' 'echo "R-AFTER"' 'sysbus ReadDoubleWord 0x5200210C' \
      'sysbus WriteDoubleWord 0x52002104 0x45670123' 'sysbus WriteDoubleWord 0x52002104 0xCDEF89AB' 'echo "R-UNLOCKED"' 'sysbus ReadDoubleWord 0x5200210C'
    check "reset: PSIZE2 as written (0x20) before; machine Reset locks CR2 with PSIZE2 back at 11 (0x31, p226); the keys then unlock it (0x30)" \
      "passed reset && [ \"\$(readout reset BEFORE)\" = 0x00000020 ] && [ \"\$(readout reset AFTER)\" = 0x00000031 ] && [ \"\$(readout reset UNLOCKED)\" = 0x00000030 ]" ;;
  config)
    PLATFORM_SED='s/^    optionBytes: 0x1BC6AAF0$/    optionBytes: 0x1FFFFFFFF/' run_case config - 'sysbus ReadDoubleWord 0x52002000'
    check "config: a flash guard given an optionBytes wider than 32 bits refuses every access, TVI-FLASH-CONFIG, unmodeled" \
      "[ \$rc = 3 ] && first config 'r[\"rule\"] == \"TVI-FLASH-CONFIG\" and r[\"kind\"] == \"unmodeled\"'" ;;
  regen)
    rc=0; python3 -I "$src/tests/1304/fields.py" "$src/tests/1304/data/rm0433-rev9-flash-fields.tsv" > "$work/fields.cs" || rc=$?
    check "regen: the guard's Fields table regenerates from tests/1304/data by tests/1304/fields.py, byte for byte (RUL-16); its restatement is tests/spike.sh manifest's (TVI-WO-1204 (6))" \
      "[ \$rc = 0 ] && sed -n '/^        private static readonly Row\[\] Fields =$/,/^        };$/p' '$src/model/GuardedSTM32H7_Flash.cs' | sed '1,2d;\$d' | cmp -s - '$work/fields.cs'" ;;
  *) echo "unknown case: $t" >&2; exit 2 ;;
  esac
done

echo "$fails failed"
[ $fails = 0 ]
