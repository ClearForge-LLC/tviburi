#!/bin/sh
# spike/2100/consumer-verify.sh TARBALL PIN [RENODE_ROOT] — TVI-WO-2100 (2): the consumer's binding check (RUL-10 (a), (f)) from the
# tarball alone, with no Tviburi tool: POSIX sh, tar, head, tail, sed, wc and sha256sum. It checks what
# `tviburi-release verify` checks of the released bytes, from the MANIFEST's own lines:
#   - the MANIFEST's sha256 is PIN (the release hash the consumer pinned);
#   - `file <sha> TviburiRelease.cs.template`: the template's bytes;
#   - `overlay <sha> <n> Tviburi.cs`: the first n bytes of Tviburi.cs, and its tail is exactly the template with its one
#     placeholder (@@MANIFEST_SHA256@@) replaced by the MANIFEST's sha256 (RUL-10 (e));
#   - the tarball holds exactly the four files, each once (no fifth member, no dotfile, no duplicate a reader might take);
#   - LEDGER.jsonl's first line names the release, and its other lines hash to `ledger-rows`;
#   - with RENODE_ROOT (the consumer's Renode, e.g. /opt/renode): its Renode.dll and Infrastructure.dll have the sha256s the
#     MANIFEST's `build` lines name (the strings alone cannot name a rebuilt assembly, TVI-WO-2100 (3)).
# Exit 0 and "CONSUMER VERIFY PASS" only if all hold; otherwise the first that failed, and exit 1.
set -eu
tar=$1 pin=$2 root=${3:-}
d=$(mktemp -d)
trap 'rm -rf "$d"' EXIT
no() { echo "CONSUMER VERIFY FAIL: $*"; exit 1; }
four="LEDGER.jsonl MANIFEST Tviburi.cs TviburiRelease.cs.template "   # sorted, as each check below lists them
[ "$(tar -tf "$tar" | LC_ALL=C sort | tr '\n' ' ')" = "$four" ] \
  || no "the tarball's members are $(tar -tf "$tar" | tr '\n' ' '), not exactly the four files once each"
tar -C "$d" -xf "$tar"
cd "$d"
sha() { sha256sum | cut -c1-64; }
[ "$(ls -A | LC_ALL=C sort | tr '\n' ' ')" = "$four" ] || no "the tarball holds $(ls -A | tr '\n' ' ')"
m=$(sha <MANIFEST)
[ "sha256:$m" = "$pin" ] || no "the MANIFEST is sha256:$m, not the pinned $pin"
t=$(sed -n 's/^file \([0-9a-f]\{64\}\) TviburiRelease\.cs\.template$/\1/p' MANIFEST)
[ -n "$t" ] && [ "$(sha <TviburiRelease.cs.template)" = "$t" ] || no "the template's bytes"
o=$(sed -n 's/^overlay \([0-9a-f]\{64\}\) [0-9]* Tviburi\.cs$/\1/p' MANIFEST)
n=$(sed -n 's/^overlay [0-9a-f]\{64\} \([0-9]*\) Tviburi\.cs$/\1/p' MANIFEST)
[ -n "$o" ] && [ "$(head -c "$n" Tviburi.cs | sha)" = "$o" ] || no "Tviburi.cs's first $n bytes"
[ "$(grep -o '@@MANIFEST_SHA256@@' TviburiRelease.cs.template | wc -l)" -eq 1 ] || no "the template's placeholder is not there once"
sed "s/@@MANIFEST_SHA256@@/$m/" TviburiRelease.cs.template >tail.expected
[ "$(wc -c <Tviburi.cs)" -eq $((n + $(wc -c <tail.expected))) ] || no "Tviburi.cs's length is not the overlay's plus the filled template's"
[ "$(tail -c +$((n + 1)) Tviburi.cs | sha)" = "$(sha <tail.expected)" ] || no "Tviburi.cs's tail is not the template filled with sha256:$m"
[ "$(head -n 1 LEDGER.jsonl)" = "{\"ledger\":\"tviburi\",\"release\":\"sha256:$m\",\"schema\":2}" ] || no "LEDGER.jsonl's first line does not name the release"
r=$(sed -n 's/^ledger-rows \([0-9a-f]\{64\}\) LEDGER\.jsonl$/\1/p' MANIFEST)
[ -n "$r" ] && [ "$(tail -n +2 LEDGER.jsonl | sha)" = "$r" ] || no "LEDGER.jsonl's rows"
a=""
if [ -n "$root" ]; then
  for asm in renode:Renode.dll infrastructure:Infrastructure.dll; do
    want=$(sed -n "s/^build ${asm%%:*} [^ ]* \([0-9a-f]\{64\}\)$/\1/p" MANIFEST)
    [ -n "$want" ] || no "the MANIFEST names no sha256 for ${asm#*:} (a format-1 MANIFEST?)"
    [ -f "$root/${asm#*:}" ] && [ "$(sha <"$root/${asm#*:}")" = "$want" ] || no "$root/${asm#*:} is not the assembly the release names ($want)"
  done
  a=", assemblies $root/Renode.dll and Infrastructure.dll as named"
fi
echo "CONSUMER VERIFY PASS: release sha256:$m, overlay $n bytes + the filled template, ledger rows $r$a"
