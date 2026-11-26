#!/bin/bash
# TVI-WO-1206, inside the pinned image (see gen-log.sh): one generated boot (or ArduPilot's fixture) replayed by the committed
# tests/1301/replay-1301.sh, patched at run time as spike/1100a/harness/inner.sh does (its one patch point), with every
# peripheral access logged: `sysbus LogAllPeripheralsAccess true` before the capture's own RunFor (kept), and the capture's
# collapse-repeated-log-entries set false. N is the capture's run (1, or fx for the fixture).
set -eu
src=/repo/tests/1301/replay-1301.sh
pat="args[\$((i + 1))]=\${c/'mach create; '/'include @/tmp/release/Tviburi.cs; mach create; '}"
n=$(grep -nF -- "$pat" "$src" | cut -d: -f1)
[ "$(printf '%s\n' "$n" | grep -c .)" = 1 ] || { echo "gen-inner: the patch point is not one line ($n)" >&2; exit 4; }
{ head -n $((n - 1)) "$src"
  cat <<'EOS'
  c=${c/'mach create; '/'include @/tmp/release/Tviburi.cs; mach create; '}
  c=${c/'emulation RunFor'/'sysbus LogAllPeripheralsAccess true; emulation RunFor'}
  args[$((i + 1))]=$c
EOS
  tail -n +$((n + 1)) "$src"; } > /tmp/replay.sh
mkdir -p /tmp/cap && cp -r "/cap/$N" "/tmp/cap/$N"
sed -i 's/^collapse-repeated-log-entries = True$/collapse-repeated-log-entries = False/' /tmp/cap/"$N"/varlib/*/tool/renode.config
grep -q '^collapse-repeated-log-entries = False$' /tmp/cap/"$N"/varlib/*/tool/renode.config || { echo "gen-inner: config not set" >&2; exit 5; }
sed -i 's|/cap/|/tmp/cap/|g' /tmp/replay.sh
rc=0; bash /tmp/replay.sh "$N" accept || rc=$?
grep -a -c 'LogAllPeripheralsAccess' "/out/$N-accept.argv.txt" > /out/logged.count || true
gzip -f "/out/$N-accept.console.txt"
echo "gen-inner: replay exit $rc"
