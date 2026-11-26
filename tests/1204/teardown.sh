#!/bin/bash
# tests/1204/teardown.sh — TVI-WO-1204 (2): N clean quits of the runner's own run.resc (scripts/tviburi-spike's, variant
# "modeled") under load, each required to write every guard's end record. Runs INSIDE the pinned image, the tree at /repo and an
# empty /out. LOAD busy loops run beside the quits (default 12, the load measured in the FEEDBACK). Renode runs
# with no --config, as the runner does: asynchronous logging, Renode's default, the configuration the race needs (TVI-WO-1204
# measure §1). Writes /out/teardown.tsv (run, renode exit, end records, witnesses, load). Exit 0 only if the runner's run passes
# and every later run exits 0 and writes, in its log file and on its console, one witness and one end record for each guard.
set -u
N=${N:-15}; LOAD=${LOAD:-12}
# The runner's own run (unloaded) builds the firmware, the check and the release, and leaves its run.resc in /out: quit 1 of N.
/repo/scripts/tviburi-spike modeled >/tmp/runner.txt 2>&1   # not in /out: the runner needs it empty
cp /tmp/runner.txt /out/runner.txt
grep -q '^VERDICT PASS' /out/runner.txt || first=1
guards=$(/repo/scripts/guards | wc -w)
for k in $(seq "$LOAD"); do ( while :; do :; done ) & done
printf 'run\texit\tends_log\tends_console\twitnesses_log\tload\n' > /out/teardown.tsv
bad=${first:-0}
for i in $(seq 2 "$N"); do
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
echo "teardown: $bad of $N clean quits lost an end record or a witness ($guards guards, load $LOAD busy loops)"
[ "$bad" = 0 ]
