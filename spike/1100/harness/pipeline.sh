#!/bin/bash
# TVI-WO-1100: from captures to the ranked queue. A thin harness: the work is in ../reader/*.awk, *.jq.
# Usage: pipeline.sh WORK CAPDIR...
#   WORK    scratch directory (outputs land in WORK/replay and WORK/*.tsv)
#   CAPDIR  a capture.sh output directory (holding capture/index.txt, capture/<n>/, capture/verify-*.json)
# Needs on the host: gawk, jq, podman, ~/bin/capped-run; the Aethelgard clone at $AET (default
# ~/tvi-1100-aet) for the CMSIS header and ledger.rs; WORK/table-entries.tsv (ledger.rs table() entries).
set -euo pipefail
here=$(cd "$(dirname "$0")" && pwd); R=$here/../reader
work=$1; shift
AET=${AET:-$HOME/tvi-1100-aet}
IMG=$(cat "$AET/container/IMAGE")
mkdir -p "$work/replay"
: > "$work/boards.tsv"; : > "$work/replays.txt"

# 1. Per board: the verdict's ledger, the Renode capture (argv[0] .../renode), the spec's motor labels.
for cap in "$@"; do
  board=""
  while read -r a b _; do
    case $a in
      BEGIN) board=$b ;;
      END) board="" ;;
      [0-9]*)
        [ -n "$board" ] || continue
        first=$(tr '\0' '\n' < "$cap/capture/$a/argv.bin" | head -1)
        [[ $first == */renode ]] || continue
        v=$cap/capture/verify-$board.json
        jq -e '.ledger.rows' "$v" > /dev/null || { echo "pipeline: $board has no ledger" >&2; continue; }
        echo "$board $cap $a" >> "$work/replays.txt"
        jq -r --arg board "$board" -f "$R/ledger-rows.jq" "$v" > "$work/ledger-$board.tsv"
        ;;
    esac
  done < "$cap/capture/index.txt"
done

# 2. The replays, one capped container, no network: pure and logged-with-map for each board.
{
  echo '#!/bin/bash'
  while read -r board cap n; do
    echo "mkdir -p /out/$board; ln -sfn /caps/$(basename "$cap")/capture /cap"
    echo "/harness/replay.sh $n pure && /harness/replay.sh $n dump /harness/dumps.txt"
    echo "mv /out/replay-$n-* /out/$board/"
  done < "$work/replays.txt"
} > "$work/replay-all.sh"
chmod +x "$work/replay-all.sh"
caps=$(for c in "$@"; do printf -- '-v %s:/caps/%s:ro ' "$c" "$(basename "$c")"; done)
nice -n 10 ~/bin/capped-run --mem 8G -- --rm --network none $caps \
  -v "$work/replay:/out" -v "$here:/harness:ro" -v "$work/replay-all.sh:/replay-all.sh:ro" "$IMG" /replay-all.sh

# 3. The readers.
awk -f "$R/cmsis-regs.awk" "$AET/crates/aethelgard-svd-hal/data/st-cmsis-device-h7/stm32h753xx.h" | LC_ALL=C sort > "$work/cmsis-regs.tsv"
awk -F'\t' -f "$R/view1.awk" "$work/cmsis-regs.tsv" "$work/table-entries.tsv" "$work"/ledger-*.tsv > "$work/view1.tsv"
: > "$work/boards.tsv"
while read -r board cap n; do
  d=$work/replay/$board
  tr -d '\r' < "$d/replay-$n-dump.log" | grep -a '^TVIMAP\|^TVIBASE' > "$d/regmap.tsv"
  tr -d '\r' < "$d/replay-$n-dump.log" | gawk -f "$R/view2-writes.awk" "$board" > "$d/writes.tsv"
  : > "$d/only2.tsv"
  gawk -F'\t' -v OFS='\t' -v board="$board" -v out2="$d/only2.tsv" -f "$R/view2-join.awk" \
    "$d/regmap.tsv" "$d/writes.tsv" "$work/view1.tsv" > "$d/joined.tsv"
  bd=$(ls -d "$cap"/capture/"$n"/varlib/aethelgard-*/tool/tree/board 2>/dev/null | head -1 || true)
  [ -z "$bd" ] && bd=$(ls -d "$cap"/capture/"$n"/varlib/aethelgard-*/tool/tree/board | head -1)
  gawk -v board="$board" -f "$R/pins.awk" "$bd/src/board.rs" > "$d/pins.tsv"
  specjson=$(ls "$cap"/capture/"$n"/varlib/aethelgard-*/tool/spec/specification.json | head -1)
  labels=$(jq -r '[.motor_outputs[]?.label] | if length == 0 then "-" else join(",") end' "$specjson")
  printf '%s\t%s\t%s\n' "$board" "$specjson" "$labels" >> "$work/boards.tsv"
  # The positive control: the boot block of the pure and the logged replay must be identical.
  for m in pure dump; do tr -d '\r' < "$d/replay-$n-$m.log" | awk '/^AETHELGARD-BOOT-BEGIN$/{f=1} f && !/^\[/{print} /^AETHELGARD-BOOT-END$/{f=0}' > "$d/boot-$m.txt"; done
  if cmp -s "$d/boot-pure.txt" "$d/boot-dump.txt" && [ -s "$d/boot-pure.txt" ]; then echo "$board control: identical ($(wc -l < "$d/boot-pure.txt") lines)"; else echo "$board control: DIFFERS or empty"; fi
done < "$work/replays.txt"
gawk -F'\t' -f "$R/rank.awk" "$work/boards.tsv" "$work"/replay/*/pins.tsv "$work"/replay/*/joined.tsv > "$work/queue.tsv"
cat "$work"/replay/*/only2.tsv > "$work/view2-only.tsv"
echo "queue: $(wc -l < "$work/queue.tsv") rows; view (2) only: $(wc -l < "$work/view2-only.tsv")"
