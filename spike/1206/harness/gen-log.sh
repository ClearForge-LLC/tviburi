#!/bin/bash
# TVI-WO-1206: every peripheral access of the 14 generated boots and ArduPilot's fixture, so that a reading's "never read"
# is measured per firmware, not inferred from the armed run alone. One capped container at a time (~/bin/capped-run,
# nice 10, --network none, cargo at 6 jobs); gen-inner.sh runs inside. Usage: gen-log.sh TREE CAPS OUT
#   TREE  the repository tree (its release and its committed replay); CAPS TVI-WO-1305's captures (cap-gen/<board>/capture,
#         cap-ap); OUT  one directory per board: the logged console (gzip) and the replay's own outputs.
set -eu
tree=$(cd "$1" && pwd); caps=$(cd "$2" && pwd); out=$3; h=$(cd "$(dirname "$0")" && pwd)
IMAGE=$(sed -n 's/^IMAGE=//p' "$tree/tests/spike.sh")
one() { # name capdir N [podman args]
  local name=$1 cap=$2 n=$3; shift 3; local o=$out/$name; mkdir -p "$o"; o=$(cd "$o" && pwd)
  nice -n 10 "$HOME/bin/capped-run" --mem 8G -- --rm --network none -e CARGO_BUILD_JOBS=6 -e RUST_TEST_THREADS=6 -e N="$n" "$@" \
    -v "$cap:/cap:ro" -v "$tree:/repo:ro" -v "$o:/out" -v "$h:/h:ro" "$IMAGE" bash /h/gen-inner.sh 2>&1 | grep -v '^capped-run' | tail -1
  echo "$(date -u +%FT%TZ) $name logged=$(cat "$o/logged.count" 2>/dev/null) reads=$(zcat "$o/$n-accept.console.txt.gz" | grep -a -c '\] Read')"
}
for b in d1-drv8876 driver-judges-nothing hse-crystal-16 hse-external-8 motors-dshot600 motors-pwm sentinel-01 sentinel-01-ext-bmp581 \
         sentinel-01-minimal sentinel-02-bench sentinel-02-bench-hse8 sentinel-02-shaped sentinel-03 serial-dshot-9600; do
  one "$b" "$caps/cap-gen/$b/capture" 1
done
one ap-fx "$caps/cap-ap" fx -e ACCEPT_EXTERNAL="PB6:up PB7:up"
echo "$(date -u +%FT%TZ) GEN-LOG-DONE"
