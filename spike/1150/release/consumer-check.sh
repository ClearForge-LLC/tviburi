#!/bin/sh
# TVI-WO-1150 (iii) spike: what a consumer that hashes its sealed copies checks, before and after a run.
#   consumer-check.sh SEALED_DIR PINNED_SHA256 [RECORDS]
# SEALED_DIR: the overlay files exactly as handed to Renode. RECORDS: the reader's REC lines for the guarded
# source (reader/records.rs). Prints one line per check, then CONSUMER PASS or CONSUMER FAIL; exit 0 or 1.
set -u
here=$(cd "$(dirname "$0")" && pwd)
d=$1; pin=$2; recs=${3:-}
bad=0
say() { echo "$1"; case $1 in FAIL*) bad=1 ;; esac; }
m=$(sha256sum < "$d/MANIFEST" | cut -d' ' -f1)
[ "$m" = "$pin" ] && say "ok   manifest is the pinned $pin" || say "FAIL manifest sha256 $m is not the pinned $pin"
listed=$(awk 'NF == 2 && length($1) == 64 {print $2}' "$d/MANIFEST" | tr '\n' ' ')
for f in $listed; do
  h=$(sha256sum < "$d/$f" | cut -d' ' -f1)
  grep -q "^$h  $f\$" "$d/MANIFEST" && say "ok   $f matches the manifest" || say "FAIL $f sha256 $h is not the manifest's"
done
for f in $(ls "$d"); do
  case " $listed MANIFEST TviburiRelease.cs " in *" $f "*) ;; *) say "FAIL $f is not in the manifest" ;; esac
done
if sed "s/@@MANIFEST_SHA256@@/$pin/" "$here/TviburiRelease.cs.template" | cmp -s - "$d/TviburiRelease.cs"; then
  say "ok   TviburiRelease.cs is the template filled with the pinned hash"
else
  say "FAIL TviburiRelease.cs is not the template filled with the pinned hash"
fi
if [ -n "$recs" ]; then
  w=$(grep -c '"tviburi":"witness"' "$recs")
  [ "$w" = 1 ] || say "FAIL $w witness records, not 1"
  grep '"tviburi":"witness"' "$recs" | grep -q "\"model_version\":\"sha256:$pin\"" \
    && say "ok   the witness reports the pinned release" || say "FAIL no witness reports sha256:$pin"
  grep '"tviburi":"witness"' "$recs" | grep -q '"mode":"enforcing"' && say "ok   the witness says enforcing" || say "FAIL the witness does not say mode enforcing"
  grep -q '"tviburi":"refusal"\|"tviburi":"would-refuse"' "$recs" && say "FAIL a refusal or would-refuse record"
  grep -q '"tviburi":"end"' "$recs" || say "FAIL no end record"
fi
[ $bad = 0 ] && echo "CONSUMER PASS" || echo "CONSUMER FAIL"
exit $bad
