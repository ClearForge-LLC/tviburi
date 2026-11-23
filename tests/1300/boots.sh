#!/usr/bin/env bash
# tests/1300/boots.sh — the consumer's boots on the guards (TVI-WO-1300a (6); 1300's §1.1 harness, runnable). Run on the host.
# Every container runs under ~/bin/capped-run with --network none, one at a time.
#
#   capture <aethelgard checkout> <dir> <board>...
#       Build Aethelgard's CLI in the image its checkout pins (container/IMAGE), then capture each board's `aethelgard
#       verify` boot with spike/1100/harness/capture.sh into <dir>/<board>/capture. The board's specification is the path
#       tests/1300/boots.tsv gives; the checkout must be at the commit boots.tsv names (checked). Kani runs inside
#       verify: give it memory (BOOTS_MEM, default 24G) and time (about 5-17 minutes a board).
#   replay <dir> [board...]
#       Replay each captured boot (tests/1300/replay-1300.sh accept: the release built from this checkout, every guard in
#       recorded mode, the consumer's platform with the RCC restated) into <dir>/<board>/replay, with its peak memory.
#   check <dir>
#       Compare every board of boots.tsv with its replay: each guard's witness (recorded) and end, and exactly the
#       would-refuse records boots.tsv expects (peripheral, rule, kind, bit), each TVI-SPI-KERNEL citing p414 and p371-372.
#       A board with no replay fails; it is never skipped. Exit 0 only if every board matches.
# ArduPilot's boot is captured by `aethelgard simulate --foreign`, not verify: its capture is 1300's (see boots.tsv); replay
# and check take it like any other.
set -uo pipefail
here=$(cd "$(dirname "$0")/../.." && pwd)
table=$here/tests/1300/boots.tsv
aet_commit=$(sed -n 's/^# aethelgard \([0-9a-f]\{40\}\)$/\1/p' "$table")
container() { nice -n 10 "$HOME/bin/capped-run" --mem "${BOOTS_MEM:-24G}" -- --rm --network none "$@"; }
spec_of() { awk -F'\t' -v b="$1" '$1 == b {print $2}' "$table"; }
boards() { awk -F'\t' '!/^#/ && NF {print $1}' "$table"; }

cmd=${1:-}; shift || true
case $cmd in
capture)
  [ $# -ge 3 ] || { echo "usage: boots.sh capture <aethelgard checkout> <dir> <board>..." >&2; exit 2; }
  aet=$(cd "$1" && pwd); dir=$2; shift 2
  [ "$(git -C "$aet" rev-parse HEAD)" = "$aet_commit" ] || { echo "boots: $aet is not at $aet_commit (boots.tsv)" >&2; exit 2; }
  image=$(cat "$aet/container/IMAGE")
  mkdir -p "$dir/target-aet"
  container -v "$aet:/repo:ro,Z" -v "$(cd "$dir" && pwd)/target-aet:/target:Z" "$image" bash -c \
    'export PATH=/opt/cargo/bin:$PATH RUSTUP_HOME=/opt/rustup CARGO_HOME=/target/cargo-home; cd /repo && cargo build --offline --locked -p aethelgard-cli --target-dir /target' \
    || { echo "boots: Aethelgard's CLI did not build" >&2; exit 1; }
  for b in "$@"; do
    spec=$(spec_of "$b"); [ -n "$spec" ] || { echo "boots: $b is not in boots.tsv" >&2; exit 2; }
    rm -rf "$dir/$b/capture"; mkdir -p "$dir/$b"
    container -v "$aet:/repo:ro,Z" -v "$(cd "$dir" && pwd)/target-aet:/target:ro,Z" -v "$here/spike/1100/harness:/h:ro,Z" \
      -v "$(cd "$dir/$b" && pwd):/out:Z" "$image" bash /h/capture.sh "$spec"
    echo "boots: captured $b: $(grep -c '^[0-9]' "$dir/$b/capture/index.txt" 2>/dev/null || echo 0) boot(s)"
  done ;;
replay)
  [ $# -ge 1 ] || { echo "usage: boots.sh replay <dir> [board...]" >&2; exit 2; }
  dir=$(cd "$1" && pwd); shift
  image=$(sed -n 's/^IMAGE=//p' "$here/tests/spike.sh")
  for b in ${*:-$(boards)}; do
    cap=$dir/$b/capture
    # this board's boots: those between its BEGIN and END in the capture's index (1300's captures hold several boards), or
    # every boot; the one that is Renode itself, not the CLI's reaper wrapping it (its argv has --aethelgard-internal-reaper)
    n=
    for k in $(awk -v b="$b" '$1 == "BEGIN" {f = ($2 == b); any = 1; next} $1 == "END" {f = 0; next} $1 ~ /^[0-9]+$/ && (f || !any) {print $1}' "$cap/index.txt" 2>/dev/null); do
      [ -s "$cap/$k/argv.bin" ] && ! grep -qa -- '--aethelgard-internal-reaper' "$cap/$k/argv.bin" && { n=$k; break; }
    done
    [ -n "$n" ] || { echo "boots: $b: no captured boot in $cap" >&2; continue; }
    rm -rf "$dir/$b/replay"; mkdir -p "$dir/$b/replay"
    BOOTS_MEM=${BOOTS_MEM:-8G} container -v "$cap:/cap:ro,Z" -v "$here:/repo:ro,Z" -v "$here/tests/1300:/bin1300:ro,Z" \
      -v "$dir/$b/replay:/out:Z" "$image" bash /bin1300/replay-1300.sh "$n" accept >"$dir/$b/replay/stdout.txt" 2>&1
    ln -sf "$n-accept.console.txt" "$dir/$b/replay/console.txt"
    echo "boots: replayed $b (boot $n): $(cat "$dir/$b/replay/$n-accept.cost.txt" 2>/dev/null || echo 'no cost line')"
  done ;;
check)
  [ $# -eq 1 ] || { echo "usage: boots.sh check <dir>" >&2; exit 2; }
  python3 -I - "$table" "$(cd "$1" && pwd)" <<'EOF'
import json, os, re, sys
table, d = sys.argv[1:3]
rec = re.compile(r'\] ([a-z0-9_.]+): (\{"tviburi".*)$')
fails = 0
for line in open(table):
    if line.startswith('#') or not line.strip():
        continue
    board, spec, guards, expected = line.rstrip('\n').split('\t')
    want = sorted(tuple(e.split(' | ')) for e in expected.split(' ; ')) if expected != 'none' else []
    path = os.path.join(d, board, 'replay', 'console.txt')
    if not os.path.exists(path):
        print('FAIL %s: no replay (%s)' % (board, path)); fails += 1; continue
    recs = [(m.group(1), json.loads(m.group(2))) for m in (rec.search(l.rstrip('\r\n')) for l in open(path, errors='replace')) if m]
    problems = []
    for g in guards.split(','):
        w = [r for s, r in recs if s == g and r['tviburi'] == 'witness']
        if len(w) != 1 or w[0].get('mode') != 'recorded':
            problems.append('%s: %d witnesses, mode %s' % (g, len(w), w[0].get('mode') if w else None))
        if len([r for s, r in recs if s == g and r['tviburi'] == 'end']) != 1:
            problems.append('%s: no single end record' % g)
    wr = [(s, r) for s, r in recs if r['tviburi'] in ('would-refuse', 'refusal')]
    got = sorted({(s, r['rule'], r['kind'], r['bit']) for s, r in wr})
    if got != want:
        problems.append('records %s, expected %s' % (got, want))
    for s, r in wr:
        if r['rule'] == 'TVI-SPI-KERNEL' and not ('p414' in r['citation'] and 'p371-372' in r['citation']):
            problems.append('%s TVI-SPI-KERNEL without p414 and p371-372' % s)
    print(('ok   %s: %d would-refuse' % (board, len(wr))) if not problems else ('FAIL %s: %s' % (board, '; '.join(problems))))
    fails += bool(problems)
print('%d failed' % fails)
sys.exit(1 if fails else 0)
EOF
  ;;
*) echo "usage: boots.sh capture|replay|check ..." >&2; exit 2 ;;
esac
