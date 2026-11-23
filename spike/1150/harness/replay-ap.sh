#!/bin/bash
# TVI-WO-1150 (v), (vi): replay one captured `aethelgard simulate --foreign` Renode invocation (capture-foreign.sh)
# inside the pinned tools image, --network none, in the environment the CLI gives Renode (HOME and TMPDIR = the
# tool directory, the two DOTNET variables), without the CLI's Landlock/seccomp sandbox and reaper.
# Mounts: /cap = the capture directory (ro: argv.bin, varlib), /ovl = an overlay release directory (ro),
#         /h1100 = spike/1100/harness (ro, for regmap.py), /out (rw).
# Usage: replay-ap.sh N MODE...      MODE, each run in turn on a fresh copy of the captured scratch:
#   pure        the captured argv, unchanged (the control)
#   logged      + `sysbus LogAllPeripheralsAccess true` placed right before `emulation RunFor` (after the CLI's own
#               write hooks, which a later hook would displace; measured in (vii) c2), + regmap.py before `quit`;
#               collapse-repeated-log-entries = False; read lines dropped from the stored console, which is gzipped
#   enforcing   the overlay (release /ovl: TviburiRelease.cs, Records1150.cs, GuardedSpi1150.cs) included before
#               `mach create`, and the platform's `SPI.STM32H7_SPI` entries re-typed to Tviburi1150.GuardedSpi1150
#               (the class map, applied to instances the consumer's platform declares itself)
#   recorded    as enforcing, with `mode: "recorded"` on each guarded instance
#   load-stock / load-overlay   Aethelgard's plain-load commands (renode.rs commands(): its models, mach create, the
#               platform, `peripherals`, quit), without / with the overlay: the cost to the listing
# Each run writes /out/N-MODE.{console.txt[.gz],argv.txt,cost.txt}. cost.txt: wall seconds; the peak, over samples
# every 100 ms, of the summed VmRSS of Renode's process tree (the CLI reaper's measure, run.rs L1090-1165); and the
# process's own VmHWM at its last sample.
set -u
n=$1; shift
for mode in "$@"; do
  rm -rf /var/lib/aethelgard; cp -r /cap/$n/varlib /var/lib/aethelgard
  mapfile -d '' args < /cap/$n/argv.bin
  args=("${args[@]:1}")
  d=""; cfg=""
  for i in "${!args[@]}"; do [ "${args[$i]}" = "--config" ] && { cfg=${args[$((i + 1))]}; d=$(dirname "$cfg"); }; done
  plat=$d/platform-booted.repl
  ovl="include @/ovl/TviburiRelease.cs; include @/ovl/Records1150.cs; include @/ovl/GuardedSpi1150.cs; "
  [ -f /ovl/Overlay1150.cs ] && ovl="include @/ovl/Overlay1150.cs; "   # the same three files as one compilation
  [ -f /ovl/includes.txt ] && ovl=$(sed 's#^#include @/ovl/#; s#$#; #' /ovl/includes.txt | tr -d '\n')   # an explicit list
  for i in "${!args[@]}"; do
    [ "${args[$i]}" = "-e" ] || continue
    c=${args[$((i + 1))]}
    case $mode in
      pure) ;;
      logged)
        sed -i 's/^collapse-repeated-log-entries = True/collapse-repeated-log-entries = False/' "$cfg"
        c=${c/'; emulation RunFor '/'; sysbus LogAllPeripheralsAccess true; emulation RunFor '}
        c="${c%; quit}; include @/h1100/regmap.py; quit" ;;
      enforcing|recorded)
        sed -i 's/^\(spi[0-9]*\): SPI\.STM32H7_SPI @ {/\1: Tviburi1150.GuardedSpi1150 @ {/' "$plat"
        [ $mode = recorded ] && sed -i '/RegistrationPeripheralAlias\.SPI[0-9]*$/{n;s/^    }$/    }\n    mode: "recorded"/}' "$plat"
        c=${c/'mach create; '/"${ovl}mach create; "} ;;
      load-stock|load-overlay)
        pre=${c%%mach create; *}
        models=$(grep -o 'include @[^;]*' <<<"$pre" | sed 's/$/; /' | tr -d '\n')
        c="${models}mach create; machine LoadPlatformDescription @$d/platform.repl; peripherals; quit"
        if [ $mode = load-overlay ]; then
          sed -i 's/^\(spi[0-9]*\): SPI\.STM32H7_SPI @ {/\1: Tviburi1150.GuardedSpi1150 @ {/' "$d/platform.repl"
          c="${ovl}${c}"
        fi ;;
      *) echo "replay-ap: mode $mode?" >&2; exit 2 ;;
    esac
    args[$((i + 1))]=$c
  done
  printf '%s\n' "${args[@]}" > /out/$n-$mode.argv.txt
  [ $mode = enforcing ] || [ $mode = recorded ] || [ $mode = load-overlay ] && cp "$plat" /out/$n-$mode.platform-booted.repl && cp "$d/platform.repl" /out/$n-$mode.platform.repl
  cd "$d"
  start=$(date +%s.%N)
  if [ $mode = logged ]; then
    env -i HOME="$d" TMPDIR="$d" DOTNET_EnableDiagnostics=0 DOTNET_EnableWriteXorExecute=0 \
      /opt/renode/renode "${args[@]}" < /dev/null 2>&1 | grep -av '\] Read\(UInt\|Byte\|Word\|DoubleWord\|QuadWord\)' | gzip -1 > /out/$n-$mode.console.txt.gz &
  else
    env -i HOME="$d" TMPDIR="$d" DOTNET_EnableDiagnostics=0 DOTNET_EnableWriteXorExecute=0 \
      /opt/renode/renode "${args[@]}" < /dev/null > /out/$n-$mode.console.txt 2>&1 &
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
