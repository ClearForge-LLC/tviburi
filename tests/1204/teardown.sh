#!/bin/bash
# tests/1204/teardown.sh — TVI-WO-1204 (2): the teardown race, made deterministic (architect 2026-11-26T15:48Z). Runs INSIDE the
# pinned image, the tree at /repo and an empty /out. The runner's own run (scripts/tviburi-spike modeled) builds the firmware,
# the check and the release and leaves its run.resc in /out; then N quits of that script, each with tests/1204/TeardownHold.cs
# holding Renode's asynchronous logging thread from the start until the logger stops it. Renode runs with no --config, as the
# runner does: asynchronous logging, the configuration the race needs (TVI-WO-1204 measure §1). Held, the queue is still full at
# the old emulation's logger's Dispose unless Records.End()'s flush wrote it: without the flush every quit loses its end
# records, with it none does (no load, no probability). Writes /out/teardown.tsv (run, renode exit, end records, witnesses).
# Exit 0 only if the runner's run passes and every held quit exits 0 and writes, in its log file and on its console, one
# witness and one end record for each guard in scripts/guards.
set -u
N=${N:-3}
/repo/scripts/tviburi-spike modeled >/tmp/runner.txt 2>&1   # not in /out: the runner needs it empty
cp /tmp/runner.txt /out/runner.txt
grep -q '^VERDICT PASS' /out/runner.txt || first=1
{ echo 'include @/repo/tests/1204/TeardownHold.cs'; echo 'emulation HoldLoggingUntilStopped'; cat /out/run.resc; } >/out/held.resc
guards=$(/repo/scripts/guards | wc -w)
printf 'run\texit\tends_log\tends_console\twitnesses_log\n' > /out/teardown.tsv
bad=${first:-0}
for i in $(seq "$N"); do
  rm -f /out/renode-log.txt
  timeout 120 renode --disable-gui --console --plain /out/held.resc </dev/null >/out/renode.out 2>&1; rc=$?
  el=$(grep -ac '{"tviburi":"end"' /out/renode-log.txt); ec=$(grep -ac '{"tviburi":"end"' /out/renode.out)
  wl=$(grep -ac '{"tviburi":"witness"' /out/renode-log.txt)
  printf '%s\t%s\t%s\t%s\t%s\n' "$i" "$rc" "$el" "$ec" "$wl" >> /out/teardown.tsv
  if [ "$rc" != 0 ] || [ "$el" != "$guards" ] || [ "$ec" != "$guards" ] || [ "$wl" != "$guards" ]; then
    bad=$((bad + 1)); cp /out/renode.out "/out/lost-$i.console.txt"; cp /out/renode-log.txt "/out/lost-$i.log.txt" 2>/dev/null
  fi
done
echo "teardown: $bad of $((N + 1)) clean quits lost an end record or a witness ($guards guards; the runner's run, then $N held)"
[ "$bad" = 0 ]
