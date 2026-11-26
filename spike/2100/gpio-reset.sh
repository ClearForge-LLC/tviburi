#!/usr/bin/env bash
# spike/2100/gpio-reset.sh — TVI-WO-2100 (Q23: a required row cites the case that shows it; "where no case exists, add one
# under spike/2100/"). GPIO's outputSpeedResetValue and pullUpPullDownResetValue default to 0, RM0433's for ports C-K;
# on port A (OSPEEDR 0x0C000000, PUPDR 0x64000000, p542) the guard refuses every access without the stock value, as
# tests/1301/gpio.sh's `config` shows for modeResetValue. This runs that suite's harness, unchanged, from a scratch copy of
# this tree in which two cases are added beside `config`: `ospeedrreset` and `pupdrreset`, each giving port A the default 0.
# The committed tree and tests/ are not edited. Exit 0 only if both cases refuse TVI-GPIO-CONFIG, unmodeled, naming the
# parameter.
set -euo pipefail
here=$(cd "$(dirname "$0")/../.." && pwd)
scratch=${TVIBURI_SCRATCH:-$HOME/.cache/tviburi}; mkdir -p "$scratch"
w=$(mktemp -d "$scratch/gpio-reset.XXXXXX")
mkdir "$w/tree"; git -C "$here" archive HEAD | tar -C "$w/tree" -xf -
python3 -I - "$w/tree/tests/1301/gpio.sh" <<'EOF'
import sys
p = sys.argv[1]; s = open(p).read()
anchor = "  query)\n"
assert s.count(anchor) == 1
cases = ""
for case, param, value in (("ospeedrreset", "outputSpeedResetValue", "0x0C000000"), ("pupdrreset", "pullUpPullDownResetValue", "0x64000000")):
    cases += (f"  {case})\n"
              f"    PLATFORM_SED='$a\\\ntviburiGpioA:\\\n    {param}: 0x0' run_case {case} - \"$clk\" 'sysbus ReadDoubleWord 0x58020000'\n"
              f"    check \"{case}: port A given {param} 0, not RM0433's {value} (p542), refuses every access, TVI-GPIO-CONFIG, unmodeled\" \\\n"
              f"      \"[ \\$rc = 3 ] && first {case} 'r[\\\"rule\\\"] == \\\"TVI-GPIO-CONFIG\\\" and r[\\\"kind\\\"] == \\\"unmodeled\\\" and \\\"{param}\\\" in r[\\\"condition\\\"]'\" ;;\n")
s = s.replace(anchor, cases + anchor)


c = s.index('CASES="'); s = s[:c] + 'CASES="ospeedrreset pupdrreset ' + s[c + len('CASES="'):]
open(p, "w").write(s)
EOF
bash -n "$w/tree/tests/1301/gpio.sh"
TVIBURI_TESTS="config ospeedrreset pupdrreset" TVIBURI_SCRATCH="$w/s" "$w/tree/tests/1301/gpio.sh" "$w/tree"
