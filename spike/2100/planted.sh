#!/usr/bin/env bash
# spike/2100/planted.sh — TVI-WO-2100 acceptance: the release job (ci/release.sh) fails as planted, each by name.
#   P1 a commit not on main; P2 a dirty checkout; P3 one byte of a model changed after the first build (the two builds
#   then differ, and compare names the file and its first differing byte); P4 verify given the wrong hash.
# Runs in a throwaway clone of this checkout under $TVIBURI_SCRATCH, so the checkout itself is never touched.
# Prints one line per plant and exits 0 only if every plant failed for its own reason.
set -uo pipefail
here=$(cd "$(dirname "$0")/../.." && pwd)
scratch=${TVIBURI_SCRATCH:-$HOME/.cache/tviburi}; mkdir -p "$scratch"
w=$(mktemp -d "$scratch/planted.XXXXXX")
git clone -q --shared "$here" "$w/clone"
# The clone's origin/main is this checkout's HEAD: before this work order merges, main has no ci/release.sh, and no plant
# depends on which commit stands for main.
git -C "$w/clone" update-ref refs/remotes/origin/main "$(git -C "$here" rev-parse HEAD)"
main=$(git -C "$w/clone" rev-parse origin/main)
r() { TVIBURI_SCRATCH="$w/s" "$w/clone/ci/release.sh" "$@" 2>&1 | grep -v '^capped-run'; }
ok=0
expect() { # expect NAME PATTERN OUTPUT
  if grep -q "$2" <<<"$3"; then echo "PLANT $1 failed as planted: $(grep -m1 'RELEASE FAIL' <<<"$3")"; else echo "PLANT $1 DID NOT FAIL AS PLANTED"; echo "$3" | tail -n 5; ok=1; fi
}
# P1: a commit not on main (a commit on top of main in the clone).
git -C "$w/clone" -c user.email=p@p -c user.name=p commit -q --allow-empty -m planted
expect P1-offmain "RELEASE FAIL: .* is not on origin/main" "$(r all HEAD origin/main)"
git -C "$w/clone" checkout -q --detach "$main"
# P2: a dirty checkout (an untracked file).
echo x >"$w/clone/planted.txt"
expect P2-dirty "RELEASE FAIL: the checkout is not clean" "$(r all "$main" origin/main)"
rm "$w/clone/planted.txt"
# P3: one byte of a model changed after the first build.
git -C "$w/clone" archive "$main" | { mkdir "$w/a.tree" && tar -C "$w/a.tree" -xf -; }
r tree "$w/a.tree" "$w/a" >/dev/null
cp -r "$w/a.tree" "$w/b.tree"
sed -i '0,/records runtime/s//records runtimE/' "$w/b.tree/model/Refusal.cs"
r tree "$w/b.tree" "$w/b" >/dev/null
expect P3-modelbyte "RELEASE FAIL: the two builds differ:.* Tviburi.cs (differ: byte .*tviburi-release.tar (differ: byte" "$(r compare "$w/a" "$w/b")"
# P4: verify given the wrong hash.
expect P4-wronghash "RELEASE FAIL: verify against sha256:0000" "$(r verify "$w/a" "sha256:$(printf '%064d' 0)")"
exit $ok
