#!/bin/bash
# TVI-WO-1301 §1.1 smoke, inside the pinned image. Mounts: /repo (ro), /h = tests/1301 (ro), /proto = a directory holding
# GpioProto1301.cs (tests/1301/proto1301.py), gpio-restate.repl (tests/1301/gpio-restate.awk) and these smoke files (ro),
# /out (rw). Usage: smoke.sh <smoke|smoke-stock|bsrr|rccreg>: builds the release, the one file (release + prototype), runs <name>.resc.

set -u
/repo/scripts/build-release /repo /out/release > /out/release-hash.txt 2>/out/release.err || { echo RELEASE-FAIL; cat /out/release.err; exit 1; }
bash /repo/tests/1300/onefile.sh /out/onefile.cs /out/release/Tviburi.cs /proto/GpioProto1301.cs
cd /out && timeout 300 renode --disable-gui --console --plain /proto/${1:-smoke}.resc < /dev/null > /out/renode.out 2>&1
echo "renode exit $?"
