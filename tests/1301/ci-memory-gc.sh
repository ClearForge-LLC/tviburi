#!/usr/bin/env bash
# TEMPORARY (TVI-WO-1301, ruled 2026-11-23T20:20Z; removed before READY): five full ArduPilot boots, as
# tests/1301/ci-memory.sh runs them, for each of four configurations, interleaved by round:
#   base       main's one file (fetched, then `git archive`), the GPIO ports stock;
#   base-gc    the same, with Renode's .NET runtime told to conserve memory (GC below);
#   1301       this source's one file, the GPIO guard on;
#   1301-gc    the same, with GC.
# GC: DOTNET_gcServer=0 (workstation), DOTNET_gcConcurrent=0 (non-concurrent), DOTNET_GCConserveMemory=9 (the most
# aggressive level). Renode is a self-contained .NET 8 application (/opt/renode/Renode.runtimeconfig.json,
# Microsoft.NETCore.App 8.0.12); its runtime reads these at start. That it does is shown, not assumed: a probe prints
# System.GC.GetConfigurationVariables() (ServerGC, ConcurrentGC, GCConserveMem) and GCSettings.LatencyMode from
# inside Renode for each environment. Prints each run's peak and wall, and per configuration whether 5 of 5 stay at
# or under 1,015,808 KiB. A measurement: it exits 0 unless the setup fails.
set -euo pipefail
here=$(cd "$(dirname "$0")/../.." && pwd)
IMAGE=$(sed -n 's/^IMAGE=//p' "$here/tests/spike.sh")
AP_IMAGE=$(sed -n 's/^AP_IMAGE=//p' "$here/tests/1301/ci-memory.sh")
ELF_SHA=$(sed -n 's/^ELF_SHA=//p' "$here/tests/1301/ci-memory.sh")
GC="DOTNET_gcServer=0 DOTNET_gcConcurrent=0 DOTNET_GCConserveMemory=9"
LIMIT=$((1048576 - 32 * 1024))
RUNS=${TVIBURI_MEMORY_RUNS:-5}
scratch=${TVIBURI_SCRATCH:-$HOME/.cache/tviburi}
mkdir -p "$scratch"
work=$(mktemp -d "$scratch/memgc.XXXXXX")
if [ "${TVIBURI_CI:-}" = 1 ]; then
  container() { podman run --rm --network none "$@"; }
else
  container() { nice -n 10 "$HOME/bin/capped-run" --mem 12G -- --rm --network none "$@"; }
fi
src=${1:-$here}
fix=$src/tests/1301/fixtures/aet-1c32c8ea-ardupilot

git -C "$here" fetch -q --depth 1 origin main
mkdir -p "$work/main"
git -C "$here" archive FETCH_HEAD | tar -x -C "$work/main"
echo "main: $(git -C "$here" rev-parse FETCH_HEAD)"

mkdir -p "$work/elf"
container -v "$fix/container:/container:ro" -v "$fix/boards:/boards:ro" -v "$work/elf:/out" "$AP_IMAGE" \
  bash /container/ardupilot-build.sh boards sentinel-02 >"$work/elf.log" 2>&1
got=$(sha256sum "$work/elf/sentinel-02/arducopter.elf" | cut -c1-64)
[ "$got" = "$ELF_SHA" ] || { echo "the ELF built is $got, not $ELF_SHA"; exit 1; }
echo "ELF $got"

cat > "$work/probe.resc" <<'PROBE'
python "import System; v = System.GC.GetConfigurationVariables(); print('GCPROBE ServerGC=%s ConcurrentGC=%s GCConserveMem=%s LatencyMode=%s' % (v['ServerGC'], v['ConcurrentGC'], v['GCConserveMem'], System.Runtime.GCSettings.LatencyMode))"
quit
PROBE
for envs in "" "$GC"; do
  container -v "$work:/w:ro" "$IMAGE" bash -c "env -i HOME=/tmp TMPDIR=/tmp DOTNET_EnableDiagnostics=0 DOTNET_EnableWriteXorExecute=0 $envs /opt/renode/renode --disable-gui --console --plain /w/probe.resc </dev/null 2>&1 | grep -a GCPROBE" \
    | sed "s/^/runtime (${envs:-no GC settings}): /"
done

for r in $(seq 1 "$RUNS"); do
  for cfg in base base-gc 1301 1301-gc; do
    tree=$src; [ "${cfg%-gc}" = base ] && tree=$work/main
    env=""; [ "${cfg%-gc}" != "$cfg" ] && env=$GC
    cap=$work/$cfg-$r/cap/2 out=$work/$cfg-$r/out
    mkdir -p "$cap" "$out"
    cp -r "$fix/argv.bin" "$fix/varlib" "$cap/"
    tool=$(echo "$cap"/varlib/*/tool); mkdir -p "$tool/.config"
    cp "$work/elf/sentinel-02/arducopter.elf" "$tool/firmware.elf"
    container -e ACCEPT_MODE=recorded -e "TVIBURI_RUNTIME_ENV=$env" -v "$work/$cfg-$r/cap:/cap:ro" -v "$tree:/repo:ro" \
      -v "$src/tests/1301:/h:ro" -v "$out:/out" "$IMAGE" bash /h/replay-1301.sh 2 accept >"$out/replay.txt" 2>&1 || true
    peak=$(sed -n 's/.*peak_tree_vmrss_kib=\([0-9]*\).*/\1/p' "$out/2-accept.cost.txt")
    wall=$(sed -n 's/.*wall_s=\([0-9.]*\).*/\1/p' "$out/2-accept.cost.txt")
    end=$(grep -a -c '^AETHELGARD-BOOT-END' "$out/2-accept.console.txt" || true)
    gpio=$(grep -a -c 'gpioPort[A-K]: {"tviburi":"witness"' "$out/2-accept.console.txt" || true)
    echo "MEM $cfg run $r peak_kib=${peak:-none} wall_s=${wall:-none} boot_end=$end gpio_witnesses=$gpio"
    echo "$cfg ${peak:-99999999}" >> "$work/peaks.txt"
  done
done
for cfg in base base-gc 1301 1301-gc; do
  awk -v c=$cfg -v L=$LIMIT '$1==c {n++; if ($2 <= L) ok++; if ($2 > m) m = $2} END {printf "SUMMARY %-8s %d of %d at or under %d KiB; max %d KiB\n", c, ok, n, L, m}' "$work/peaks.txt"
done
echo "outputs: $work"
