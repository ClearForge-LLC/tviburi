#!/bin/bash
# TVI-WO-1100 capture run, inside the pinned tools image with --network none (a throwaway container).
# Nothing in the image is changed. An unsandboxed watcher polls /proc for each Renode process whose
# argv holds `sysbus LoadELF` (a boot), and while it runs copies its argv (/proc/<pid>/cmdline, exact
# bytes), its working directory's path, and a snapshot of the CLI's scratch (/var/lib/aethelgard,
# ownership not kept) into /out/capture/<n>. Then plain `aethelgard verify` runs on each specification
# path given as an argument (relative to the Aethelgard checkout), as run-verify.sh does; the verdict of
# spec X is /out/capture/verify-<basename X>.json.
set -u
mkdir -p /out/capture
(
  declare -A done_pids
  n=0
  while :; do
    for c in /proc/[0-9]*/cmdline; do
      p=${c#/proc/}; p=${p%/cmdline}
      [ -n "${done_pids[$p]:-}" ] && continue
      grep -qa 'sysbus LoadELF' "$c" 2>/dev/null || continue
      grep -qa -- '--console' "$c" 2>/dev/null || continue
      done_pids[$p]=1
      n=$((n + 1))
      mkdir -p "/out/capture/$n"
      cp "$c" "/out/capture/$n/argv.bin"
      d=$(readlink "/proc/$p/cwd")
      cp -r --no-preserve=ownership /var/lib/aethelgard "/out/capture/$n/varlib" 2>"/out/capture/$n/cp.err"
      alive=$([ -e "/proc/$p" ] && echo alive || echo gone)
      echo "$n $d $p $alive $(date +%s.%N)" >> /out/capture/index.txt
    done
    sleep 0.02
  done
) &
watcher=$!
cd /repo
for spec in "$@"; do
  s=$(basename "$spec" .json)
  echo "BEGIN $s $(date +%s.%N)" >> /out/capture/index.txt
  /target/debug/aethelgard verify --spec "$spec" > /out/capture/verify-$s.json 2>/out/capture/verify-$s.err
  echo "END $s exit $? $(date +%s.%N)" >> /out/capture/index.txt
done
kill $watcher
