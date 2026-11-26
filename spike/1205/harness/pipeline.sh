#!/bin/bash
# TVI-WO-1205 (1), (2): TVI-WO-1100a's pipeline, re-run on a later tree: from the two armed runs to the row tables. On the host (python3); the release and its
# clause-6 cases are built in the pinned image, one container at a time.
# Usage: pipeline.sh TREE WORK CAP INJ LABEL
#   TREE  this repository (the tree measured: its release, its guards' sources, its tests' case lists)
#   WORK  scratch (the runs, the release, the cases); outputs go to TREE/spike/1205/data. The readers and the two
#         fixed inputs (cmsis-regs.tsv, Aethelgard 4400h's armed queue) are TVI-WO-1100a's, in spike/1100a. Cargo runs
#         at 6 jobs (the architect's Furnace load ruling, 2026-11-26T13:53Z).
#   CAP, INJ  as replay-armed.sh: TVI-WO-1305's armed capture and its unpadded injection
#   LABEL the run's name: outputs go to TREE/spike/1205/data/LABEL (main-<sha>)
# The tree must have TVI-WO-1308's DMAMUX1 and PWR guards: their sweeps' cases are built with tests/1308's own bases.
# (main-63fe557, before 1308, ran this file without those two sweeps; its outputs are in data/main-63fe557.)
set -eu
tree=$(cd "$1" && pwd); work=$2; cap=$3; inj=$4; d=$tree/spike/1100a; o=$tree/spike/1205/data/$5; h=$tree/spike/1205/harness; mkdir -p "$work" "$o"
IMAGE=$(sed -n 's/^IMAGE=//p' "$tree/tests/spike.sh")
# 1. The release and the five ledger-driven sweeps' cases, each with the base and enable its test passes.
mkdir -p "$work/rel" "$work/cases"
nice -n 10 "$HOME/bin/capped-run" --mem 8G -- --rm --network none -e CARGO_BUILD_JOBS=6 -e RUST_TEST_THREADS=6 -v "$tree:/repo:ro" -v "$work/rel:/out" "$IMAGE" \
  sh -c '/repo/scripts/build-release /repo /out/release && for a in \
    "Tviburi.GuardedSTM32H7_SPI 0x40013000 0x400 0x580244F0 0x1000" "Tviburi.GuardedSTM32_Timer 0x40010000 0x400 0x580244F0 0x3" \
    "Tviburi.GuardedSTM32DMA 0x40020000 0x400 0x580244D8 0x3" "Tviburi.GuardedSTM32F7_USART 0x40011000 0x400 0x580244F0 0x10" \
    "Tviburi.GuardedSTM32H7_Flash 0x52002000 0x1000" \
    "Tviburi.GuardedSTM32H7_DMAMUX 0x40020800 0x400 0x580244D8 0x3" "Tviburi.GuardedSTM32H7_PWR 0x58024800 0x400"; do set -- $a; /out/release.target/debug/tviburi-release cases /out/release/LEDGER.jsonl "$@" > /out/$1.txt; done'
cp "$work"/rel/Tviburi.*.txt "$work/cases/"
# The I2C and ADC sweeps (tests/1306, tests/1307 `fields`): every field allowed only at 0, from their own fields.py.
python3 -I "$tree/tests/1306/fields.py" "$tree/tests/1306/data/rm0433-rev9-i2c-fields.tsv" | grep -oP 'R\("(I2C_\w+)", (0x[0-9A-F]+), "[^"]*", "(\w+)", (\d+), (\d+), Zero,' \
  | grep -v 'I2C_ISR\|I2C_RXDR\|I2C_PECR' | sed -E 's/R\("(\w+)", (0x[0-9A-F]+), "[^"]*", "(\w+)".*/\1.\3/' > "$work/cases/i2c-zero.txt"
python3 -I "$tree/tests/1307/fields.py" "$tree/tests/1307/data/rm0433-rev9-adc-fields.tsv" | grep -oP 'R\("(ADCx?_\w+)", (0x[0-9A-F]+), "[^"]*", "(\w+)", (\d+), (\d+), Zero,' \
  | sed -E 's/R\("(\w+)", (0x[0-9A-F]+), "[^"]*", "(\w+)".*/\1.\3/' > "$work/cases/adc-zero.txt"
# 2. Run A (the control) and run B (the reach), one after the other.
"$h/replay-armed.sh" "$tree" "$cap" "$inj" "$work/arm" A
"$h/replay-armed.sh" "$tree" "$cap" "$inj" "$work/arm" B log
# 3. The arm (mav.py: the COMMAND_ACK for 400 with result 0), then B's writes per register.
zcat "$work/arm/B.console.txt.gz" | tr -d '\r' > "$work/B.txt"
arm=$(python3 -I "$d/reader/mav.py" "$work/B.txt" | awk '/COMMAND_ACK command 400 result 0/{print $1; exit}')
python3 -I "$d/reader/arm-writes.py" "$work/B.txt" "$arm" "$o/arm-periph.tsv" "$o/arm-writes.tsv" "$o/arm-unmapped.tsv"
# 4. Place the rows (1100's 722, 1150's 844, 4400h's armed-only ids), then join.
python3 -I "$d/reader/rows.py" "$tree/spike/1100/data/queue.tsv" "$tree/spike/1100/data/joined.tsv" "$tree/spike/1150/data/queue-ap.tsv" \
  "$d/data/cmsis-regs.tsv" "$o/arm-periph.tsv" "$o/rows.tsv" "$d/data/aet-4400h-queue-ap-armed-sentinel-03.tsv"
python3 -I "$tree/spike/1205/reader/join.py" "$o" "$tree" "$work/rel/release/LEDGER.jsonl" "$work/cases" "$o/states.tsv" "$o/arm-reach-outside.tsv"
rm -f "$work/B.txt"
