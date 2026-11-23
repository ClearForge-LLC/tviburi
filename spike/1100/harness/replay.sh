#!/bin/bash
# TVI-WO-1100 replay of one captured Renode invocation, inside the pinned tools image, --network none.
# Usage (in the container): replay.sh N MODE [DUMPS]
#   N      capture number (/cap/N/argv.bin = Renode's /proc/<pid>/cmdline, argv[0] first; /cap/N/varlib)
#   MODE   pure   - the captured argv, unchanged
#          logged - the same, with `sysbus LogAllPeripheralsAccess true` inserted right after `sysbus LoadELF @...`
#          dump   - logged, plus each line of DUMPS (monitor commands) inserted right before the final `quit`
# The captured scratch is restored at its original path, so every @path in the argv resolves as it did.
# Renode is run with the variables the CLI gives it (main.rs renode_env; run.rs HOME and TMPDIR = workdir),
# without the CLI's Landlock/seccomp sandbox; the container has no network.
set -euo pipefail
n=$1; mode=$2; dumps=${3:-}
[ -s /cap/$n/argv.bin ] || { echo "replay: no capture $n" >&2; exit 2; }
rm -rf /var/lib/aethelgard; cp -r /cap/$n/varlib /var/lib/aethelgard
mapfile -d '' args < /cap/$n/argv.bin
args=("${args[@]:1}")   # drop argv[0]; /opt/renode/renode is run below
d=""
for i in "${!args[@]}"; do [ "${args[$i]}" = "--config" ] && d=$(dirname "${args[$((i + 1))]}"); done
[ -n "$d" ] || { echo "replay: no --config in the argv" >&2; exit 2; }   # the CLI's tool directory
for i in "${!args[@]}"; do
  [ "${args[$i]}" = "-e" ] || continue
  c=${args[$((i + 1))]}
  case $mode in
    pure) ;;
    logged|dump)
      [[ $c =~ (sysbus LoadELF @[^;]*; ) ]] || { echo "replay: no LoadELF in the commands" >&2; exit 3; }
      c=${c/"${BASH_REMATCH[1]}"/"${BASH_REMATCH[1]}sysbus LogAllPeripheralsAccess true; "}
      if [ "$mode" = dump ]; then
        extra=$(paste -sd ';' "$dumps" | sed 's/;/; /g')
        [[ $c == *"; quit" ]] || { echo "replay: commands do not end in quit" >&2; exit 3; }
        c="${c%; quit}; ${extra}; quit"
      fi ;;
    *) echo "replay: mode $mode?" >&2; exit 2 ;;
  esac
  args[$((i + 1))]=$c
done
printf '%s\n' "${args[@]}" > /out/replay-$n-$mode.argv.txt
cd "$d"
env -i HOME="$d" TMPDIR="$d" DOTNET_EnableDiagnostics=0 DOTNET_EnableWriteXorExecute=0 \
  /opt/renode/renode "${args[@]}" > /out/replay-$n-$mode.log 2>&1 || echo "renode exit $?" >> /out/replay-$n-$mode.log
