#!/bin/bash
# tests/1204/teardown.sh — TVI-WO-1204 (2): N clean quits of the runner's own run.resc (scripts/tviburi-spike's, variant
# "modeled") under load, each required to write every guard's end record. Runs INSIDE the pinned image, the tree at /repo and an
# empty /out; the release and firmware are built once. LOAD busy loops run beside the quits (default 3 x nproc). Renode runs
# with no --config, as the runner does: asynchronous logging, Renode's default, the configuration the race needs (TVI-WO-1204
# measure §1). Writes /out/teardown.tsv (run, renode exit, end records, witnesses, load). Exit 0 only if every run exits 0 and
# writes, in its log file and on its console, one witness and one end record for each guard in scripts/guards.
set -u
N=${N:-15}; LOAD=${LOAD:-$((3 * $(nproc)))}
export PATH=/opt/cargo/bin:$PATH RUSTUP_HOME=/opt/rustup CARGO_HOME=/out/cargo-home
v=$(/repo/scripts/build-release) || { echo "teardown: release build failed"; exit 9; }
vs="modeled modf cfg2first iolock forge spray rmw ardupilot generated field"
rustc --edition 2021 --target thumbv7em-none-eabihf -C opt-level=s -C panic=abort -C debuginfo=0 --cfg 'path="modeled"' \
  --check-cfg "cfg(path, values(\"${vs// /\",\"}\"))" -C link-arg=-T/repo/firmware/link.x -o /out/fw.elf /repo/firmware/spi1.rs \
  || { echo "teardown: firmware build failed"; exit 8; }
cat > /out/run.resc <<R
include @/repo/scripts/prelude.resc
showAnalyzer usart3
sysbus LogPeripheralAccess spi1 true
sysbus LoadELF @/out/fw.elf
cpu VectorTableOffset 0x08000000
emulation RunFor "0.001"
echo "TVIBURI-MARK-AFTER"
sysbus ReadDoubleWord 0x20000104
echo "TVIBURI-END"
quit
R
guards=$(/repo/scripts/guards | wc -w)
for k in $(seq "$LOAD"); do ( while :; do :; done ) & done
printf 'run\texit\tends_log\tends_console\twitnesses_log\tload\n' > /out/teardown.tsv
bad=0
for i in $(seq "$N"); do
  rm -f /out/renode-log.txt
  timeout 120 renode --disable-gui --console --plain /out/run.resc </dev/null >/out/renode.out 2>&1; rc=$?
  el=$(grep -ac '{"tviburi":"end"' /out/renode-log.txt); ec=$(grep -ac '{"tviburi":"end"' /out/renode.out)
  wl=$(grep -ac '{"tviburi":"witness"' /out/renode-log.txt)
  printf '%s\t%s\t%s\t%s\t%s\t%s\n' "$i" "$rc" "$el" "$ec" "$wl" "$(cut -d' ' -f1 /proc/loadavg)" >> /out/teardown.tsv
  if [ "$rc" != 0 ] || [ "$el" != "$guards" ] || [ "$ec" != "$guards" ] || [ "$wl" != "$guards" ]; then
    bad=$((bad + 1)); cp /out/renode.out "/out/lost-$i.console.txt"; cp /out/renode-log.txt "/out/lost-$i.log.txt"
  fi
done
kill $(jobs -p) 2>/dev/null
echo "teardown: $bad of $N clean quits lost an end record or a witness ($guards guards, load $LOAD busy loops); release $v"
[ "$bad" = 0 ]
