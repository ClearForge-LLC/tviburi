#!/bin/bash
# TVI-WO-1301 §1.1: replay one captured `aethelgard simulate` Renode invocation inside the pinned tools image,
# --network none, in the CLI's environment (HOME and TMPDIR = the tool directory, the two DOTNET variables), without the
# CLI's sandbox and reaper. From tests/1300/replay-1300.sh's `accept` mode.
# Mounts: /cap = capture directory (ro), /repo = this repository (ro), /ovl = the prototype directory (ro), /out (rw).
# Usage: replay-1301.sh N MODE...
#   pure       the captured argv, unchanged (the control)
#   base       today's one file (the release built from /repo: the SPI and RCC guards), as 1300's `accept`: spi1/spi2
#              re-typed to the guarded SPI, recorded; the RCC restated, recorded. No GPIO guard
#   trace      base, plus the GPIO prototype (/ovl/GpioProto1301.cs concatenated into the one file, usings hoisted) and
#              /ovl/gpio-restate-trace.repl (every port cancelled and restated as the prototype, recorded, trace on);
#              the consumer's own references to a port moved onto the guard's variable (its untyped `gpioPortX:`
#              blocks, re-appended after the restatement; `@ gpioPortX` and `-> gpioPortX` repointed); console gzipped
#   cost       as trace, with /ovl/gpio-restate-cost.repl (trace off): the one file's cost with the GPIO guard
#   traceclk   as trace, with `sysbus LogPeripheralAccess sysbus.rcc true` before RunFor: the GPIO accesses in order with the
#              RCC's (RCC_AHB4ENR's GPIOxEN, for TVI-WO-1300a's lock)
#   tracesi    as trace, with /ovl/gpio-restate-tracesi.repl: the ports reset to silicon's values (gpio-restate.awk resets=1)
set -u
n=$1; shift
for mode in "$@"; do
  rm -rf /var/lib/aethelgard; cp -r /cap/$n/varlib /var/lib/aethelgard
  mapfile -d '' args < /cap/$n/argv.bin
  args=("${args[@]:1}")
  d=""; cfg=""
  for i in "${!args[@]}"; do [ "${args[$i]}" = "--config" ] && { cfg=${args[$((i + 1))]}; d=$(dirname "$cfg"); }; done
  plat=$d/platform-booted.repl
  for i in "${!args[@]}"; do
    [ "${args[$i]}" = "-e" ] || continue
    c=${args[$((i + 1))]}
    case $mode in
      pure) ;;
      base|trace|cost|tracesi|traceclk)
        [ -s /tmp/release/Tviburi.cs ] || /repo/scripts/build-release /repo /tmp/release > /out/$n-$mode.release.txt 2>&1 || { echo "replay-1301: release build failed" >&2; exit 3; }
        sed -i 's/^\(spi[0-9]*\): SPI\.STM32H7_SPI @ {/\1: Tviburi.GuardedSTM32H7_SPI @ {/' "$plat"
        sed -i '/RegistrationPeripheralAlias\.SPI[0-9]*$/{n;s/^    }$/    }\n    mode: "recorded"/}' "$plat"
        sed -i 's/^\( *\)rcc RegistrationPeripheralAlias/\1tviburiRcc RegistrationPeripheralAlias/' "$plat"
        props=$(awk '/^rcc:$/{f=1; next} f && /^ /{print; next} {f=0}' "$plat")
        awk '/^rcc:$/{f=1; next} f && /^ /{next} {f=0; print}' "$plat" > "$plat.new" && cat "$plat.new" > "$plat" && rm "$plat.new"
        { cat /repo/model/stm32h7-rcc-restate.repl; printf '\ntviburiRcc:\n    mode: "recorded"\n'; [ -n "$props" ] && printf '%s\n' "$props"; } >> "$plat"
        one=/tmp/release/Tviburi.cs
        if [ $mode != base ]; then
          # the consumer's untyped port blocks, moved after the restatement onto the guard's variable
          awk '/^gpioPort[A-K]:$/{f=1; sub(/gpioPort/, "tviburiGpio"); print; next} f && /^ /{print; next} {f=0}' "$plat" > /out/$n-$mode.moved-gpio.txt
          awk '/^gpioPort[A-K]:$/{f=1; next} f && /^ /{next} {f=0; print}' "$plat" > "$plat.new" && cat "$plat.new" > "$plat" && rm "$plat.new"
          grep -n '@ gpioPort[A-K]\|-> gpioPort[A-K]' "$plat" > /out/$n-$mode.repointed-gpio.txt
          sed -i 's/@ gpioPort\([A-K]\)/@ tviburiGpio\1/; s/-> gpioPort\([A-K]\)/-> tviburiGpio\1/g' "$plat"
          { echo; cat /ovl/gpio-restate-${mode/traceclk/trace}.repl; echo; cat /out/$n-$mode.moved-gpio.txt; } >> "$plat"
          [ -s /tmp/onefile-$mode.cs ] || bash /repo/tests/1300/onefile.sh /tmp/onefile-$mode.cs /tmp/release/Tviburi.cs /ovl/GpioProto1301.cs
          one=/tmp/onefile-$mode.cs
        fi
        c=${c/'mach create; '/"include @$one; mach create; "}
        [ $mode = traceclk ] && c=${c/'; emulation RunFor '/'; sysbus LogPeripheralAccess sysbus.rcc true; emulation RunFor '}
        if [ ${mode#trace} != $mode ]; then
          sed -i 's/^collapse-repeated-log-entries = True/collapse-repeated-log-entries = False/' "$cfg"
        fi ;;
      *) echo "replay-1301: mode $mode?" >&2; exit 2 ;;
    esac
    args[$((i + 1))]=$c
  done
  printf '%s\n' "${args[@]}" > /out/$n-$mode.argv.txt
  cp "$plat" /out/$n-$mode.platform-booted.repl
  cd "$d"
  start=$(date +%s.%N)
  envs=(HOME="$d" TMPDIR="$d" DOTNET_EnableDiagnostics=0 DOTNET_EnableWriteXorExecute=0)
  if [ ${mode#trace} != $mode ]; then
    env -i "${envs[@]}" /opt/renode/renode "${args[@]}" < /dev/null 2>&1 | gzip -1 > /out/$n-$mode.console.txt.gz &
  else
    env -i "${envs[@]}" /opt/renode/renode "${args[@]}" < /dev/null > /out/$n-$mode.console.txt 2>&1 &
  fi
  sleep 0.05
  peak=0; hwm=0
  while kill -0 $! 2>/dev/null; do
    s=0; for p in $(pgrep -f 'enode' ); do r=$(awk '/^VmRSS:/{print $2}' /proc/$p/status 2>/dev/null); s=$((s + ${r:-0})); h=$(awk '/^VmHWM:/{print $2}' /proc/$p/status 2>/dev/null); [ "${h:-0}" -gt $hwm ] && hwm=$h; done
    [ $s -gt $peak ] && peak=$s
    sleep 0.1
  done
  wait $!
  echo "mode=$mode wall_s=$(awk -v a=$start -v b=$(date +%s.%N) 'BEGIN{printf "%.2f", b-a}') peak_tree_vmrss_kib=$peak vmhwm_kib=$hwm load=$(cut -d' ' -f1-3 /proc/loadavg)" | tee /out/$n-$mode.cost.txt
  cd /
done
