#!/bin/bash
# TVI-WO-1100a scratch harness, inside the pinned image (see replay-armed.sh). The replay is the committed
# tests/1301/replay-1301.sh, patched at run time, never copied, by replacing its one line
#     args[$((i + 1))]=${c/'mach create; '/'include @/tmp/release/Tviburi.cs; mach create; '}
# with the four below: the same release include; the argv's one `emulation RunFor "16.0000000005"` cut to
# 0.000000001, so the run ends with the injection's own 14 s (TVI-WO-1305's armed run, its RUNFOR); and EXTRA before
# that RunFor (the RUNFOR and EXTRA knobs of 1305a's scratch replay-a.sh). The diff is written to /out/NAME.patch.diff.
set -eu
src=/repo/tests/1301/replay-1301.sh
pat="args[\$((i + 1))]=\${c/'mach create; '/'include @/tmp/release/Tviburi.cs; mach create; '}"
n=$(grep -nF -- "$pat" "$src" | cut -d: -f1)
[ "$(printf '%s\n' "$n" | grep -c .)" = 1 ] || { echo "inner: the patch point is not one line ($n)" >&2; exit 4; }
{ head -n $((n - 1)) "$src"
  cat <<'EOF'
  c=${c/'mach create; '/'include @/tmp/release/Tviburi.cs; mach create; '}
  c=${c/'emulation RunFor "16.0000000005"'/'emulation RunFor "0.000000001"'}
  c=${c/'emulation RunFor'/"$EXTRA emulation RunFor"}
  args[$((i + 1))]=$c
EOF
  tail -n +$((n + 1)) "$src"; } > /tmp/replay.sh
diff "$src" /tmp/replay.sh > "/out/$NAME.patch.diff" || true
if [ "$LOG" = log ]; then
  mkdir -p /tmp/cap && cp -r /cap/s03 /tmp/cap/s03
  sed -i 's/^collapse-repeated-log-entries = True$/collapse-repeated-log-entries = False/' /tmp/cap/s03/varlib/*/tool/renode.config
  grep -q '^collapse-repeated-log-entries = False$' /tmp/cap/s03/varlib/*/tool/renode.config || { echo "inner: config not set" >&2; exit 5; }
  sed -i 's|/cap/|/tmp/cap/|g' /tmp/replay.sh
fi
rc=0; bash /tmp/replay.sh s03 accept || rc=$?
cd /out
for f in s03-accept.*; do mv "$f" "$NAME.${f#s03-accept.}"; done
gzip -f "$NAME.console.txt"
echo "inner: replay exit $rc"
