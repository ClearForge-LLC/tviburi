#!/usr/bin/env bash
# tests/1301/ci-memory.sh [SRC] — TVI-WO-1301's memory check (ruled 2026-11-23T18:30Z, 19:08Z): ArduPilot's full 5 s
# boot, as Aethelgard runs it (tests/1301/fixtures/aet-1c32c8ea-ardupilot/), on this source's one-file overlay, five
# runs, each peak at least 32 MiB under Aethelgard's bound of 1,048,576 KiB. The ELF is built first from Aethelgard's
# pinned ArduPilot image and must be a799dfb6… (SOURCE.md there). Each run is tests/1301/replay-1301.sh `accept`,
# every guard recorded, one container at a time, no network. Meant for CI (an unshared runner, TVIBURI_CI=1); on
# Furnace it runs capped, and only an unslowed run is a proxy (ruled 18:30Z).
# Exit 0 only if the ELF matches, every run reaches the boot's end with no GPIO would-refuse, and every peak is
# at most 1,015,808 KiB.
set -euo pipefail

here=$(cd "$(dirname "$0")/../.." && pwd)
IMAGE=$(sed -n 's/^IMAGE=//p' "$here/tests/spike.sh")
AP_IMAGE=ghcr.io/clearforge-llc/aethelgard-ardupilot@sha256:e1809b909b02c7fd01b593a65f32d191169a5027ef61ceeae8e74cc08117c2b6
ELF_SHA=a799dfb6633537e74a8c3b7d8b6a464f185c255e377a612b7f9fcf8378dab1ef
RUNS=${TVIBURI_MEMORY_RUNS:-5}
LIMIT=$((1048576 - 32 * 1024))
scratch=${TVIBURI_SCRATCH:-$HOME/.cache/tviburi}
mkdir -p "$scratch"
work=$(mktemp -d "$scratch/memory.XXXXXX")

if [ "${TVIBURI_CI:-}" = 1 ]; then
  container() { podman run --rm --network none "$@"; }
else
  [ -x "$HOME/bin/capped-run" ] || { echo "no ~/bin/capped-run; TVIBURI_CI=1 is for CI runners only" >&2; exit 2; }
  container() { nice -n 10 "$HOME/bin/capped-run" --mem 12G -- --rm --network none "$@"; }
fi
if [ $# -ge 1 ]; then
  src=$(cd "$1" && pwd)
else
  src=$work/src
  mkdir "$src"
  git -C "$here" archive HEAD | tar -x -C "$src"
fi
fix=$src/tests/1301/fixtures/aet-1c32c8ea-ardupilot

# The ELF, built as Aethelgard's CI builds it, then held to its hash.
mkdir -p "$work/elf"
container -v "$fix/container:/container:ro" -v "$fix/boards:/boards:ro" -v "$work/elf:/out" "$AP_IMAGE" \
  bash /container/ardupilot-build.sh boards sentinel-02 >"$work/elf.log" 2>&1 || { tail -5 "$work/elf.log"; echo "FAIL the ArduPilot build"; exit 1; }
got=$(sha256sum "$work/elf/sentinel-02/arducopter.elf" | cut -c1-64)
echo "ELF sha256 $got (want $ELF_SHA)"
[ "$got" = "$ELF_SHA" ] || { echo "FAIL the ELF built is $got, not $ELF_SHA: a question for Scotty (ruled 19:08Z)"; exit 1; }

fails=0
for r in $(seq 1 "$RUNS"); do
  cap=$work/cap$r/2 out=$work/out$r
  mkdir -p "$cap" "$out"
  cp -r "$fix/argv.bin" "$fix/varlib" "$cap/"
  tool=$(echo "$cap"/varlib/*/tool)
  mkdir -p "$tool/.config"
  cp "$work/elf/sentinel-02/arducopter.elf" "$tool/firmware.elf"
  container -e ACCEPT_MODE=recorded -v "$work/cap$r:/cap:ro" -v "$src:/repo:ro" -v "$out:/out" "$IMAGE" \
    bash /repo/tests/1301/replay-1301.sh 2 accept >"$out/replay.txt" 2>&1 || true
  peak=$(sed -n 's/.*peak_tree_vmrss_kib=\([0-9]*\).*/\1/p' "$out/2-accept.cost.txt")
  end=$(grep -a -c '^AETHELGARD-BOOT-END' "$out/2-accept.console.txt" || true)
  would=$(grep -a -c 'gpioPort[A-K]: {"tviburi":"would-refuse"' "$out/2-accept.console.txt" || true)
  line="run $r: $(cat "$out/2-accept.cost.txt"); boot end $end; GPIO would-refuse $would"
  if [ -n "$peak" ] && [ "$peak" -le $LIMIT ] && [ "$end" = 1 ] && [ "$would" = 0 ]; then echo "ok   $line"; else echo "FAIL $line"; fails=$((fails + 1)); fi
done
echo "limit $LIMIT KiB (1,048,576 less 32 MiB); outputs: $work"
[ $fails = 0 ] && echo "ALL PASS" || { echo "$fails FAILED"; exit 1; }
