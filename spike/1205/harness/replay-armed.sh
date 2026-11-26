#!/bin/bash
# TVI-WO-1205: TVI-WO-1100a's armed-run harness (spike/1100a/harness), with cargo at 6 jobs (architect, 2026-11-26T13:53Z).
# The armed run on a tree's release, every guard recorded, as TVI-WO-1300c ran it.
# Runs on the host: one container, ~/bin/capped-run (8G, swap 0), nice 10, --network none; inner.sh runs inside.
# Usage: replay-armed.sh TREE CAP INJ OUT NAME [log]
#   TREE  the repository tree to release (mounted ro at /repo)
#   CAP   the capture directory holding s03/{argv.bin,varlib}: TVI-WO-1305's capture of Aethelgard 4400h's s03-arm-s
#         (Sentinel-03, ELF dbf598d8...), mounted ro at /cap
#   INJ   the directory holding inject.resc: 1305's unpadded injection of 4400h's plan, mounted ro at /inj
#   OUT   the output directory (rw): NAME.{console.txt.gz,cost.txt,platform-booted.repl,argv.txt,patch.diff,...}
#   log   run B: `sysbus LogAllPeripheralsAccess true` added after the consumer's write hooks, before RunFor, and the
#         configuration's collapse-repeated-log-entries set false. Without it: run A, the control, nothing added.
# The flags are 1300c's: ACCEPT_USART_DMA="usart1:41" and ACCEPT_EXTERNAL="PB6:up PB7:up"; ACCEPT_MODE stays recorded;
# and TVI-WO-1307's declared battery pin, ACCEPT_ADC_LEVELS="adcM1S2:10:1.40" (Sentinel-03 at 4 S x 3.85 V / 11, a test
# value: no consumer property declares it). Without the level, main's ADC guard records TVI-ADC-LEVEL at every DR read.
set -eu
tree=$(cd "$1" && pwd); cap=$(cd "$2" && pwd); inj=$(cd "$3" && pwd); out=$4; name=$5; log=${6:-}
mkdir -p "$out"; out=$(cd "$out" && pwd); h=$(cd "$(dirname "$0")/../../1100a/harness" && pwd)   # its inner.sh, unchanged
IMAGE=$(sed -n 's/^IMAGE=//p' "$tree/tests/spike.sh")
extra='include @/inj/inject.resc;'
[ "$log" = log ] && extra="sysbus LogAllPeripheralsAccess true; $extra"
free -g | sed -n 2p
nice -n 10 "$HOME/bin/capped-run" --mem 8G -- --rm --network none \
  -v "$tree:/repo:ro" -v "$cap:/cap:ro" -v "$inj:/inj:ro" -v "$out:/out" -v "$h:/h:ro" \
  -e EXTRA="$extra" -e LOG="$log" -e NAME="$name" \
  -e CARGO_BUILD_JOBS=6 -e RUST_TEST_THREADS=6 -e ACCEPT_USART_DMA="usart1:41" -e ACCEPT_EXTERNAL="PB6:up PB7:up" -e ACCEPT_ADC_LEVELS="adcM1S2:10:1.40" \
  "$IMAGE" bash /h/inner.sh
