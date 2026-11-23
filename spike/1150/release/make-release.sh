#!/bin/sh
# TVI-WO-1150 (iii) spike: make a release directory OUT from the overlay's model files.
#   make-release.sh OUT FILE...
# TEMPLATE (env) picks another template, as TviburiRelease.readonly.cs.template.
# Writes OUT/<each FILE>, OUT/MANIFEST (every file's sha256, the Renode build, and the release file named as
# excluded), then OUT/TviburiRelease.cs: the template with the sha256 of MANIFEST's bytes. Prints that hash.
set -eu
here=$(cd "$(dirname "$0")" && pwd)
out=$1; shift
mkdir -p "$out"
for f in "$@"; do cp "$f" "$out/"; done
{
  echo "tviburi-overlay-manifest 1"
  echo "renode-build 1.17.0+20260907gitf1dd1b4af.aet1605"
  echo "excluded TviburiRelease.cs generated: the template filled with this manifest's sha256"
  (cd "$out" && for f in "$@"; do b=$(basename "$f"); sha256sum "$b"; done | LC_ALL=C sort -k2)
} > "$out/MANIFEST"
h=$(sha256sum < "$out/MANIFEST" | cut -d' ' -f1)
sed "s/@@MANIFEST_SHA256@@/$h/" "${TEMPLATE:-$here/TviburiRelease.cs.template}" > "$out/TviburiRelease.cs"
echo "$h"
