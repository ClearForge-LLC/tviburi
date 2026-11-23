#!/bin/bash
# TVI-WO-1150 (v), (vi): one `aethelgard simulate --foreign` boot of the held ArduPilot ELF on the platform
# `generate` emits for SPEC, with each Renode process that loads an ELF captured as spike/1100's capture.sh
# does it (argv bytes, cwd, a snapshot of the CLI's scratch). Inside the pinned tools image, --network none.
# Mounts: /repo = the Aethelgard clone (ro), /target = its CLI build (ro), /elf = the ELF (ro), /out (rw).
# Usage: capture-foreign.sh SPEC WINDOW      e.g. crates/aethelgard-cli/tests/fixtures/sentinel-02-own.json 5000ms
set -u
spec=$1; window=$2
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
      tr '\0' '\n' < "/proc/$p/environ" > "/out/capture/$n/environ.txt" 2>/dev/null
      d=$(readlink "/proc/$p/cwd")
      cp -r --no-preserve=ownership /var/lib/aethelgard "/out/capture/$n/varlib" 2>"/out/capture/$n/cp.err"
      echo "$n $d $p $(date +%s.%N)" >> /out/capture/index.txt
    done
    sleep 0.02
  done
) &
watcher=$!
cd /repo
/target/debug/aethelgard generate --spec "$spec" --out /out/gen > /out/generate.json 2>/out/generate.err
echo "generate exit $?" >> /out/capture/index.txt
plat=$(ls /out/gen/board/*.repl | head -1)
start=$(date +%s.%N)
/target/debug/aethelgard simulate --platform "$plat" --spec "$spec" --firmware /elf --foreign "$window" > /out/verdict.json 2>/out/simulate.err
echo "simulate exit $? wall $(echo "$(date +%s.%N) - $start" | bc)" >> /out/capture/index.txt
kill $watcher
