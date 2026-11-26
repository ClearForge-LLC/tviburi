#!/bin/sh
# spike/2100/consumer-boot.sh CAPDIR PLATFORM RELDIR OUT [INJECT] -- TVI-WO-2100 rehearsal: boot one captured Aethelgard
# `simulate` invocation guarded, as the consumer would. CAPDIR holds argv.bin and varlib/ (read-only, copied);
# PLATFORM is consumer-platform.py's output for that capture; RELDIR holds the verified tarball's files; OUT is fresh.
# The copied varlib is /var/lib/aethelgard in the container; the emitted platform replaces platform-booted.repl there
# (the captured one is kept beside it), so the argv's paths stay the capture's. Renode runs under env -i with HOME and
# TMPDIR the tool dir, in the consumer's pinned image, with no network, one container at a time.
set -eu
cap=$1 plat=$2 rel=$3 out=$4 inj=${5:-}
img=ghcr.io/clearforge-llc/aethelgard-tools@sha256:1e0ab105ec42bdba66b773464f0b6679d622e4afdafef30ec4931e4326f49c63
here=$(cd "$(dirname "$0")" && pwd)
[ ! -e "$out" ] || { echo "consumer-boot: $out exists" >&2; exit 2; }
mkdir -p "$out"
cp -a "$cap/varlib" "$out/varlib"
id=$(ls "$out/varlib")
[ "$(echo "$id" | wc -l)" = 1 ] || { echo "consumer-boot: varlib holds $id" >&2; exit 2; }
tool=$out/varlib/$id/tool vtool=/var/lib/aethelgard/$id/tool
grep -qx 'compiler-cache-enabled = False' "$tool/renode.config" || { echo "consumer-boot: compiler cache not off" >&2; exit 2; }
mv "$tool/platform-booted.repl" "$tool/platform-booted.captured.repl"
cp "$plat" "$tool/platform-booted.repl"
cp "$rel/Tviburi.cs" "$tool/Tviburi.cs"
# A copy check only (not the binding check, which is consumer-verify.sh on the tarball, cross-checked by the
# witnesses' model_version).
[ "$(sha256sum <"$tool/Tviburi.cs")" = "$(sha256sum <"$rel/Tviburi.cs")" ] || { echo "consumer-boot: overlay copy" >&2; exit 2; }
if [ -n "$inj" ]; then
  cp "$inj" "$tool/inject.resc"
  python3 -I "$here/consumer-argv.py" "$cap/argv.bin" "$out/argv.bin" "$vtool/Tviburi.cs" "$vtool/inject.resc"
else
  python3 -I "$here/consumer-argv.py" "$cap/argv.bin" "$out/argv.bin" "$vtool/Tviburi.cs"
fi
export XDG_RUNTIME_DIR=/run/user/$(id -u)
start=$(date +%s)
set +e
timeout 5400 /home/forge/.cache/tvi-1308/lockrun nice -n 10 "$HOME/bin/capped-run" --mem 12G -- --rm --network none \
  -e CARGO_BUILD_JOBS=6 -e RUST_TEST_THREADS=6 \
  -v "$out/varlib:/var/lib/aethelgard:Z" -v "$out/argv.bin:/argv.bin:ro,Z" "$img" \
  sh -c "cd / && xargs -0 -a /argv.bin env -i HOME=$vtool TMPDIR=$vtool </dev/null" >"$out/console.log" 2>&1
rc=$?
set -e
echo "consumer-boot: exit $rc after $(( $(date +%s) - start )) s; console $(wc -l <"$out/console.log") lines" | tee "$out/boot.status"
