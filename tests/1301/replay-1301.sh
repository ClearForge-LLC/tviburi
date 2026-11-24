#!/bin/bash
# TVI-WO-1301: replay one captured `aethelgard simulate` Renode invocation inside the pinned tools image, --network
# none, in the CLI's environment (HOME and TMPDIR = the tool directory, the two DOTNET variables), without the CLI's
# sandbox and reaper, on the guard built from /repo (WO §2). From tests/1300/replay-1300.sh's `accept` mode. Used by
# tests/1301/ci-memory.sh, TVI-WO-1201's memory budget. (§1.1's other modes, which measured the prototype, are in this
# file's git history before the 19:47Z ruling deleted them.)
# Mounts: /cap = capture directory (ro), /repo = this repository (ro), /out (rw). Usage: replay-1301.sh N accept
#   accept  the release built from /repo, its one file included before `mach create`; spi1/spi2 re-typed to the
#           guarded SPI and recorded; the RCC restated and recorded (its properties moved onto `tviburiRcc`); the
#           committed model/stm32h7-gpio-restate.repl appended, each port on LQFP144 in ACCEPT_MODE (default recorded;
#           spi1, spi2 and the RCC stay recorded); the consumer's own references to a port moved onto the guard's
#           variable (its untyped `gpioPortX:` blocks re-appended after the restatement; `@ gpioPortX` and
#           `-> gpioPortX` repointed); a tree without the GPIO restate file runs with its ports stock. Writes
#           /out/N-accept.{argv.txt,platform-booted.repl,console.txt,cost.txt,peak-proc.txt}; peak-proc.txt is the
#           Renode process's /proc status and smaps_rollup at its peak sample.
#           ACCEPT_EXTERNAL ("PB6:up PB7:up"): the board's declared levels, each on its port's guard as `external`
#           (TVI-WO-1301a, ruled 2026-11-23T23:52Z), for a tree whose guard has that property. It stands in for the
#           consumer, whose platforms declare none yet.
set -u
n=$1
[ "${2:-}" = accept ] || { echo "replay-1301: usage: replay-1301.sh N accept" >&2; exit 2; }
mode=accept
rm -rf /var/lib/aethelgard; cp -r /cap/$n/varlib /var/lib/aethelgard
mapfile -d '' args < /cap/$n/argv.bin
args=("${args[@]:1}")
d=""; cfg=""
for i in "${!args[@]}"; do [ "${args[$i]}" = "--config" ] && { cfg=${args[$((i + 1))]}; d=$(dirname "$cfg"); }; done
plat=$d/platform-booted.repl
for i in "${!args[@]}"; do
  [ "${args[$i]}" = "-e" ] || continue
  c=${args[$((i + 1))]}
  [ -s /tmp/release/Tviburi.cs ] || /repo/scripts/build-release /repo /tmp/release > /out/$n-$mode.release.txt 2>&1 || { echo "replay-1301: release build failed" >&2; exit 3; }
  sed -i 's/^\(spi[0-9]*\): SPI\.STM32H7_SPI @ {/\1: Tviburi.GuardedSTM32H7_SPI @ {/' "$plat"
  sed -i '/RegistrationPeripheralAlias\.SPI[0-9]*$/{n;s/^    }$/    }\n    mode: "recorded"/}' "$plat"
  sed -i 's/^\( *\)rcc RegistrationPeripheralAlias/\1tviburiRcc RegistrationPeripheralAlias/' "$plat"
  props=$(awk '/^rcc:$/{f=1; next} f && /^ /{print; next} {f=0}' "$plat")
  awk '/^rcc:$/{f=1; next} f && /^ /{next} {f=0; print}' "$plat" > "$plat.new" && cat "$plat.new" > "$plat" && rm "$plat.new"
  { cat /repo/model/stm32h7-rcc-restate.repl; printf '\ntviburiRcc:\n    mode: "recorded"\n'; [ -n "$props" ] && printf '%s\n' "$props"; } >> "$plat"
  # A tree without a guard's restate file (main before 1301) replays without that guard: one harness wires both
  # trees of TVI-WO-1201's budget (ruled 2026-11-23T23:12Z).
  if [ -f /repo/model/stm32h7-gpio-restate.repl ]; then
    awk '/^gpioPort[A-K]:$/{f=1; sub(/gpioPort/, "tviburiGpio"); print; next} f && /^ /{print; next} {f=0}' "$plat" > /out/$n-$mode.moved-gpio.txt
    awk '/^gpioPort[A-K]:$/{f=1; next} f && /^ /{next} {f=0; print}' "$plat" > "$plat.new" && cat "$plat.new" > "$plat" && rm "$plat.new"
    grep -n '@ gpioPort[A-K]\|-> gpioPort[A-K]' "$plat" > /out/$n-$mode.repointed-gpio.txt
    sed -i 's/@ gpioPort\([A-K]\)/@ tviburiGpio\1/; s/-> gpioPort\([A-K]\)/-> tviburiGpio\1/g' "$plat"
    { echo; cat /repo/model/stm32h7-gpio-restate.repl; echo
      ext=; grep -q 'string external = null' /repo/model/GuardedSTM32_GPIOPort.cs && ext=${ACCEPT_EXTERNAL:-}
      for x in A B C D E F G H I J K; do
        printf 'tviburiGpio%s:\n    mode: "%s"\n    package: "LQFP144"\n' $x "${ACCEPT_MODE:-recorded}"
        e=$(printf '%s\n' $ext | grep "^P$x" | tr '\n' ' ' | sed 's/ $//')
        [ -z "$e" ] || printf '    external: "%s"\n' "$e"
        echo
      done
      cat /out/$n-$mode.moved-gpio.txt; } >> "$plat"
  fi
  # TVI-WO-1302: a tree with the timer restatement (TIM1 and TIM8 guarded) gets it after the GPIO one, which names the
  # guards' variables. The consumer's untyped `timer1:`/`timer8:` blocks (its frequency) move onto `tviburiTimer1`/`8`
  # (an untyped update binds to the cancelled entry: measured, TVI-WO-1302.measure.md §2.3), and its `timerN@` inputs
  # are repointed. A tree without it replays as before.
  if [ -f /repo/model/stm32h7-timer-restate.repl ]; then
    awk '/^timer[18]:$/{f=1; sub(/timer/, "tviburiTimer"); print; next} f && /^ /{print; next} {f=0}' "$plat" > /out/$n-$mode.moved-timer.txt
    awk '/^timer[18]:$/{f=1; next} f && /^ /{next} {f=0; print}' "$plat" > "$plat.new" && cat "$plat.new" > "$plat" && rm "$plat.new"
    sed -i 's/\btimer\([18]\)@/tviburiTimer\1@/g' "$plat"
    { echo; cat /repo/model/stm32h7-timer-restate.repl; echo
      printf 'tviburiTimer1:\n    mode: "%s"\n\ntviburiTimer8:\n    mode: "%s"\n\n' "${ACCEPT_MODE:-recorded}" "${ACCEPT_MODE:-recorded}"
      cat /out/$n-$mode.moved-timer.txt; } >> "$plat"
  fi
  # TVI-WO-1303: a tree with the DMA restatement (DMA1 and DMA2 guarded) gets it last, with each controller's mode.
  # The consumer's platforms declare no `dma1:`/`dma2:` of their own (measured on the 16 captured platforms); their
  # references to `dmamux1` stay bound, since DMAMUX1 is not cancelled (ruled 2026-11-24T14:10Z Q1).
  if [ -f /repo/model/stm32h7-dma-restate.repl ]; then
    { echo; cat /repo/model/stm32h7-dma-restate.repl; echo
      printf 'tviburiDma1:\n    mode: "%s"\n\ntviburiDma2:\n    mode: "%s"\n\n' "${ACCEPT_MODE:-recorded}" "${ACCEPT_MODE:-recorded}"; } >> "$plat"
  fi
  args[$((i + 1))]=${c/'mach create; '/'include @/tmp/release/Tviburi.cs; mach create; '}
done
printf '%s\n' "${args[@]}" > /out/$n-$mode.argv.txt
cp "$plat" /out/$n-$mode.platform-booted.repl
cd "$d"
start=$(date +%s.%N)
env -i HOME="$d" TMPDIR="$d" DOTNET_EnableDiagnostics=0 DOTNET_EnableWriteXorExecute=0 /opt/renode/renode "${args[@]}" < /dev/null > /out/$n-$mode.console.txt 2>&1 &
sleep 0.05
peak=0; hwm=0
while kill -0 $! 2>/dev/null; do
  s=0; k=0; top=0; topr=0
  for p in $(pgrep -f 'enode'); do r=$(awk '/^VmRSS:/{print $2}' /proc/$p/status 2>/dev/null); s=$((s + ${r:-0})); k=$((k + 1)); [ "${r:-0}" -gt $topr ] && { topr=$r; top=$p; }; h=$(awk '/^VmHWM:/{print $2}' /proc/$p/status 2>/dev/null); [ "${h:-0}" -gt $hwm ] && hwm=$h; done
  # At each new peak, the breakdown of the largest matched process, Renode (TVI-WO-1201 (4)): its status and
  # smaps_rollup. Kept only if it has RssAnon: a peak sampled as Renode exits finds its memory already gone (no Rss
  # lines, 2 threads; seen in CI run 45286469273 and on Furnace), so the last whole snapshot stays, with its own sum.
  if [ $s -gt $peak ]; then
    peak=$s
    { echo "pid $top of $k matched; rss_sum_kib $s"; cat /proc/$top/status /proc/$top/smaps_rollup 2>/dev/null; } > /tmp/peak-proc.new
    grep -q '^RssAnon:' /tmp/peak-proc.new && mv /tmp/peak-proc.new /tmp/peak-proc.txt
  fi
  sleep 0.1
done
wait $!
cp /tmp/peak-proc.txt /out/$n-$mode.peak-proc.txt 2>/dev/null
echo "mode=$mode wall_s=$(awk -v a=$start -v b=$(date +%s.%N) 'BEGIN{printf "%.2f", b-a}') peak_tree_vmrss_kib=$peak vmhwm_kib=$hwm load=$(cut -d' ' -f1-3 /proc/loadavg)" | tee /out/$n-$mode.cost.txt
