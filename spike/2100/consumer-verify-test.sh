#!/bin/sh
# spike/2100/consumer-verify-test.sh TARBALL PIN [RENODE_ROOT] — TVI-WO-2100: consumer-verify.sh passes the release and
# refuses each planted tarball by name: a wrong pin; one overlay byte edited; a ledger byte edited; a fifth, hidden member
# (`.payload.cs`); a second `Tviburi.cs` member before the real one (adversarial pass, finding 6); the template's
# placeholder twice; and, with a Renode root, an assembly whose sha256 is not the MANIFEST's (a fake root here; the
# consumer's real root passes where it is mounted, as the release job does in the image). Exit 0 only if every plant held.
set -eu
here=$(cd "$(dirname "$0")" && pwd)
tar=$(cd "$(dirname "$1")" && pwd)/$(basename "$1") pin=$2 root=${3:-}
w=$(mktemp -d)
trap 'rm -rf "$w"' EXIT
v() { sh "$here/consumer-verify.sh" "$@" 2>&1 || true; }
bad=0
expect() { # expect NAME PATTERN OUTPUT
  case "$3" in *"$2"*) echo "ok   $1: $(echo "$3" | tail -n 1)" ;; *) echo "FAIL $1: $3"; bad=1 ;; esac
}
expect pass "CONSUMER VERIFY PASS" "$(v "$tar" "$pin")"
expect wrongpin "not the pinned" "$(v "$tar" "sha256:$(printf '%064d' 0)")"
mkdir "$w/x" && tar -C "$w/x" -xf "$tar" && order=$(tar -tf "$tar")   # the release's members, in its order
edit() { # edit NAME FILE SED: a copy of the release with FILE edited, retarred in the release's order
  rm -rf "$w/e" && cp -r "$w/x" "$w/e" && sed -i "$3" "$w/e/$2" && (cd "$w/e" && tar -cf "$w/$1.tar" $order)
}
edit overlay Tviburi.cs '0,/records runtime/s//records runtimE/'
expect overlay "first" "$(v "$w/overlay.tar" "$pin")"
edit ledger LEDGER.jsonl '2s/"field"/"Field"/'
expect ledger "LEDGER.jsonl's rows" "$(v "$w/ledger.tar" "$pin")"
(cd "$w/x" && echo x >.payload.cs && tar -cf "$w/hidden.tar" $order .payload.cs && rm .payload.cs)
expect hidden "not exactly the four files" "$(v "$w/hidden.tar" "$pin")"
mkdir "$w/j" && echo junk >"$w/j/Tviburi.cs"
(cd "$w/j" && tar -cf "$w/dup.tar" Tviburi.cs) && (cd "$w/x" && tar -rf "$w/dup.tar" $order)
expect duplicate "not exactly the four files" "$(v "$w/dup.tar" "$pin")"
edit placeholder TviburiRelease.cs.template 's|"sha256:@@MANIFEST_SHA256@@"|"sha256:@@MANIFEST_SHA256@@" + "@@MANIFEST_SHA256@@"|'
expect placeholder "the template" "$(v "$w/placeholder.tar" "$pin")"
mkdir "$w/fake" && echo a >"$w/fake/Renode.dll" && echo b >"$w/fake/Infrastructure.dll"
expect assembly "is not the assembly the release names" "$(v "$tar" "$pin" "$w/fake")"
[ -z "$root" ] || expect assemblies-real "assemblies" "$(v "$tar" "$pin" "$root")"
exit $bad
