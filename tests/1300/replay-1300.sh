#!/bin/bash
# TVI-WO-1300 §1.1 (tests/1300/replay-1300.sh, mounted at /bin1300): replay one captured `aethelgard simulate --foreign` Renode invocation inside the pinned
# tools image, --network none, in the CLI's environment (HOME and TMPDIR = the tool directory, the two DOTNET variables,
# plus TVIBURI_MODEL_DIR as 1150 (ii)'s modeldir runs), without the CLI's sandbox and reaper. From spike/1150's
# replay-ap.sh.
# Mounts: /cap = capture directory (ro), /ovl = overlay directory (ro), /out (rw).
# Usage: replay-1300.sh N MODE...
#   pure        the captured argv, unchanged (the control)
#   trace       /ovl/Overlay1300.cs included before `mach create`; the platform's spi1/spi2 re-typed to the guarded SPI;
#               every `rcc RegistrationPeripheralAlias` in the consumer's platform repointed to the guard; /ovl/restate-trace.repl
#               appended (rcc @none, the guard as "rcc" in recorded mode with trace on, the 58 stock registrations restated);
#               `sysbus LogPeripheralAccess` on spi1, spi2, i2c1, i2c2, usart1, rng before RunFor; console gzipped
#   cost        as trace, with /ovl/restate-cost.repl (trace off) and no access logging: the one-file overlay's cost
#   cost-spi    /ovl/OverlaySpi.cs (the same one file without the RCC guard), spi1/spi2 re-typed, stock rcc
#   accept      the built guard (acceptance, WO §2): the release built from /repo (scripts/build-release) and its one
#               Tviburi.cs included before `mach create`; spi1/spi2 re-typed to the guarded SPI, recorded; the committed
#               model/stm32h7-rcc-restate.repl appended with `tviburiRcc: mode: "recorded"`; each `rcc RegistrationPeripheralAlias`
#               repointed, and an untyped `rcc:` block's properties moved onto `tviburiRcc` (the consumer requirement,
#               RUL-09 (b); TVI-WO-1300.rm0433.md §6.4). Needs /repo mounted.
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
      accept)
        /repo/scripts/build-release /repo /tmp/release > /out/$n-accept.release.txt 2>&1 || { echo "replay-1300: release build failed" >&2; exit 3; }
        sed -i 's/^\(spi[0-9]*\): SPI\.STM32H7_SPI @ {/\1: Tviburi.GuardedSTM32H7_SPI @ {/' "$plat"
        sed -i '/RegistrationPeripheralAlias\.SPI[0-9]*$/{n;s/^    }$/    }\n    mode: "recorded"/}' "$plat"
        grep -n '^ *rcc RegistrationPeripheralAlias' "$plat" > /out/$n-$mode.repointed.txt
        sed -i 's/^\( *\)rcc RegistrationPeripheralAlias/\1tviburiRcc RegistrationPeripheralAlias/' "$plat"
        props=$(awk '/^rcc:$/{f=1; next} f && /^ /{print; next} {f=0}' "$plat")
        awk '/^rcc:$/{f=1; next} f && /^ /{next} {f=0; print}' "$plat" > "$plat.new" && cat "$plat.new" > "$plat" && rm "$plat.new"
        { cat /repo/model/stm32h7-rcc-restate.repl; printf '\ntviburiRcc:\n    mode: "recorded"\n'; [ -n "$props" ] && printf '%s\n' "$props"; } >> "$plat"
        echo "$props" > /out/$n-$mode.moved-props.txt
        c=${c/'mach create; '/'include @/tmp/release/Tviburi.cs; mach create; '} ;;
      trace|cost|cost-spi)
        sed -i 's/^\(spi[0-9]*\): SPI\.STM32H7_SPI @ {/\1: Tviburi.GuardedSTM32H7_SPI @ {/' "$plat"
        if [ $mode = cost-spi ]; then
          c=${c/'mach create; '/'include @/ovl/OverlaySpi.cs; mach create; '}
        else
          grep -n '^ *rcc RegistrationPeripheralAlias' "$plat" > /out/$n-$mode.repointed.txt
          sed -i 's/^\( *\)rcc RegistrationPeripheralAlias/\1tviburiRcc RegistrationPeripheralAlias/' "$plat"
          cat /ovl/restate-${mode}.repl >> "$plat"
          c=${c/'mach create; '/'include @/ovl/Overlay1300.cs; mach create; '}
        fi
        if [ $mode = trace ]; then
          sed -i 's/^collapse-repeated-log-entries = True/collapse-repeated-log-entries = False/' "$cfg"
          # the CLI's CFG2 hooks on spi1/spi2 displace LogPeripheralAccess (0100b §1.1): dropped in trace mode only
          c=$(sed -E 's/sysbus SetHookBeforePeripheralWrite sysbus\.spi[12] "[^"]*"; //g' <<<"$c")
          # only peripherals the platform declares: a failed monitor command drops the rest of the chain, RunFor included
          logs=""
          for p in spi1 spi2 i2c1 i2c2 usart1 rng; do
            grep -q "^$p:" "$plat" /opt/renode/platforms/cpus/stm32h7.repl && logs="${logs}sysbus LogPeripheralAccess sysbus.$p true; "
          done
          echo "$logs" > /out/$n-$mode.logged.txt
          c=${c/'; emulation RunFor '/"; ${logs}emulation RunFor "}
        fi ;;
      *) echo "replay-1300: mode $mode?" >&2; exit 2 ;;
    esac
    args[$((i + 1))]=$c
  done
  printf '%s\n' "${args[@]}" > /out/$n-$mode.argv.txt
  cp "$plat" /out/$n-$mode.platform-booted.repl
  cd "$d"
  start=$(date +%s.%N)
  envs=(HOME="$d" TMPDIR="$d" DOTNET_EnableDiagnostics=0 DOTNET_EnableWriteXorExecute=0)
  [ $mode = pure ] || envs+=(TVIBURI_MODEL_DIR=/ovl/modeldir)
  if [ $mode = trace ]; then
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
