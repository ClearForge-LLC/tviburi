#!/bin/bash
# TVI-WO-1301 §1.1: one generated board's boot, captured as spike/1100/harness/capture.sh captures it (the same /proc
# watcher: argv bytes, cwd, a snapshot of the CLI's scratch), but reached by `generate`, `build` and `simulate --for 100ms`
# instead of `verify`, so Kani does not run (verify boots the same firmware for the same 100 ms: RunFor "0.1000000005" in
# TVI-WO-1300's captures). Inside the pinned tools image, --network none.
# Mounts: /repo = the Aethelgard export (ro), /target = its CLI build (ro), /out (rw). Usage: capture-gen.sh SPEC
set -u
spec=$1; s=$(basename "$spec" .json)
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
      echo "$n $d $p $(date +%s.%N)" >> /out/capture/index.txt
    done
    sleep 0.02
  done
) &
watcher=$!
cd /repo
echo "BEGIN $s $(date +%s.%N)" >> /out/capture/index.txt
/target/debug/aethelgard generate --spec "$spec" --out /out/gen > /out/generate.json 2>/out/generate.err
echo "generate exit $?" >> /out/capture/index.txt
/target/debug/aethelgard build --crate /out/gen/board --out /out/fw.elf > /out/build.json 2>/out/build.err
echo "build exit $?" >> /out/capture/index.txt
plat=$(ls /out/gen/board/*.repl | head -1)
/target/debug/aethelgard simulate --platform "$plat" --spec "$spec" --firmware /out/fw.elf --for 100ms > /out/verdict.json 2>/out/simulate.err
echo "END $s simulate exit $? $(date +%s.%N)" >> /out/capture/index.txt
kill $watcher
