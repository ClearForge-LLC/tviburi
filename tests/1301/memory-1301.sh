#!/bin/bash
# TVI-WO-1301 (ruled 17:52Z): the one file's peak on ArduPilot's full boot, per GPIO variant, interleaved, each replay
# pinned to 4 CPUs (taskset -c 0-3: a GitHub ubuntu-24.04 runner's 4 vCPUs) on a shared host. Inside the pinned image.
# Mounts: /cap (ArduPilot's capture, ro), /repo (the tree whose release is the base: SPI and RCC guards, ro),
# /h = tests/1301 (ro), /vars = one directory per variant holding GpioProto1301.cs and gpio-restate-cost.repl (ro), /out.
# Usage: memory-1301.sh ROUNDS VARIANT...   (VARIANT `base` = the release alone; any other = /vars/VARIANT)
# Writes /out/runs.tsv: round, variant, peak tree VmRSS (KiB), VmHWM, wall, host load, whether the boot reached its end.
set -u
rounds=$1; shift
for r in $(seq 1 "$rounds"); do
  for v in "$@"; do
    rm -f /ovl; ln -s /vars/$v /ovl
    rm -f /tmp/onefile-cost.cs /out/2-*.console.txt /out/2-*.cost.txt
    mode=cost; [ "$v" = base ] && mode=base
    ( cd / && taskset -c 0-3 bash /h/replay-1301.sh 2 $mode ) > /out/replay-last.txt 2>&1
    end=$(grep -a -c '^AETHELGARD-BOOT-END' /out/2-$mode.console.txt 2>/dev/null)
    wit=$(grep -a -c '"witness"' /out/2-$mode.console.txt 2>/dev/null)
    err=$(grep -a -c 'rror E[0-9]\|error executing' /out/2-$mode.console.txt 2>/dev/null)
    printf '%s\t%s\t%s\tboot-end=%s witnesses=%s load-errors=%s\n' "$r" "$v" "$(cat /out/2-$mode.cost.txt)" "${end:-0}" "${wit:-0}" "${err:-0}" >> /out/runs.tsv
  done
done
