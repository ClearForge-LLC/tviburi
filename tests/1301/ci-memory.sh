#!/usr/bin/env bash
# tests/1301/ci-memory.sh [MAIN BRANCH] — TVI-WO-1201's memory budget (Scotty, 2026-11-23 evening), replacing
# TVI-WO-1301's absolute step. ArduPilot's full 5 s boot, as Aethelgard runs it (tests/1301/fixtures/
# aet-1c32c8ea-ardupilot/), on main's one-file overlay and on the branch's, in this one job, interleaved.
#   MAIN, BRANCH  each a directory holding a source tree, or a commit of this checkout (extracted by `git archive`).
#                 Default: HEAD^1 and HEAD. In CI on a pull request HEAD is GitHub's merge commit and HEAD^1 is main's
#                 tip; on a push to main HEAD^1 is main before the push. TVIBURI_MAIN_SHA, when set (CI sets it to a
#                 pull request's base), must be MAIN's commit.
# Only the release comes from each tree, built once by that tree's own scripts/build-release; the harness
# (replay-1301.sh), the captured run and the ELF are this checkout's, the same for both sides. The ELF is built from
# Aethelgard's pinned ArduPilot image and must be a799dfb6… (the fixture's SOURCE.md).
# This script only boots and hands off (RUL-14, ruled 2026-11-24T01:05Z): one row per boot of what it sampled (the
# peak, the boot's end, the witnesses of each side's release, GPIO would-refuse), under a header naming the columns.
# The judge is tools/tviburi-budget, built from this checkout and unit-tested here before it judges. It decides
# whether each boot counts and computes the two medians, and it refuses by name: failed-boot, too-few-boots (fewer
# than 5 a side), ceiling (a branch boot over 1,572,864 KiB), budget (the branch's median more than 32 MiB over
# main's). Exit 0 only on its PASS. In CI (TVIBURI_CI=1) containers run bare on the unshared runner; elsewhere
# under capped-run.
set -euo pipefail

here=$(cd "$(dirname "$0")/../.." && pwd)
IMAGE=$(sed -n 's/^IMAGE=//p' "$here/tests/spike.sh")
AP_IMAGE=ghcr.io/clearforge-llc/aethelgard-ardupilot@sha256:e1809b909b02c7fd01b593a65f32d191169a5027ef61ceeae8e74cc08117c2b6
ELF_SHA=a799dfb6633537e74a8c3b7d8b6a464f185c255e377a612b7f9fcf8378dab1ef

scratch=${TVIBURI_SCRATCH:-$HOME/.cache/tviburi}
mkdir -p "$scratch"
work=$(mktemp -d "$scratch/memory.XXXXXX")
if [ "${TVIBURI_CI:-}" = 1 ]; then
  container() { podman run --rm --network none "$@"; }
else
  [ -x "$HOME/bin/capped-run" ] || { echo "no ~/bin/capped-run; TVIBURI_CI=1 is for CI runners only" >&2; exit 2; }
  container() { nice -n 10 "$HOME/bin/capped-run" --mem 12G -- --rm --network none "$@"; }
fi
runs=${TVIBURI_MEMORY_RUNS:-5}   # the judge refuses fewer than 5 a side
fix=$here/tests/1301/fixtures/aet-1c32c8ea-ardupilot

# The two trees, then each one's release, built once by its own release program; its hash is what every witness of
# a boot on that side must report.
declare -A tree hash
for s in main branch; do
  if [ $s = main ]; then a=${1:-HEAD^1}; else a=${2:-HEAD}; fi
  if [ -d "$a" ]; then
    tree[$s]=$(cd "$a" && pwd); echo "$s: the directory ${tree[$s]}"
  else
    tree[$s]=$work/$s; mkdir "${tree[$s]}"
    git -C "$here" archive "$a" | tar -x -C "${tree[$s]}"
    c=$(git -C "$here" rev-parse "$a")
    echo "$s: commit $c ($a)"
    # A pull request's main must be the base GitHub names, not whatever HEAD^1 happens to be (WO §5).
    [ $s = branch ] || [ -z "${TVIBURI_MAIN_SHA:-}" ] || [ "$c" = "$TVIBURI_MAIN_SHA" ] \
      || { echo "FAIL main is $c, but the pull request's base is $TVIBURI_MAIN_SHA"; exit 1; }
  fi
  mkdir "$work/rel-$s"
  hash[$s]=$(container -v "${tree[$s]}:/repo:ro" -v "$work/rel-$s:/out" "$IMAGE" sh /repo/scripts/build-release /repo /out/release \
    2>"$work/rel-$s.err") || { cat "$work/rel-$s.err"; echo "FAIL $s's release did not build"; exit 1; }
  case ${hash[$s]} in sha256:????????????????????????????????????????????????????????????????) ;; *) echo "FAIL $s's release printed '${hash[$s]}', not a hash"; exit 1 ;; esac
  echo "$s release ${hash[$s]}"
done

# The judge, from this checkout like the harness, the same for both sides. Its unit tests pin the refusals; a judge
# that fails them judges nothing.
container -v "$here/tools/tviburi-budget:/src:ro" -v "$work:/w" "$IMAGE" sh -c 'export PATH=/opt/cargo/bin:$PATH
  export RUSTUP_HOME=/opt/rustup CARGO_HOME=/w/cargo-home
  cargo test --quiet --offline --locked --manifest-path /src/Cargo.toml --target-dir /w/judge &&
  cargo build --quiet --offline --locked --manifest-path /src/Cargo.toml --target-dir /w/judge' >"$work/judge.txt" 2>&1 \
  || { tail -30 "$work/judge.txt"; echo "FAIL the judge (tools/tviburi-budget) did not build or pass its tests"; exit 1; }
echo "judge: $(grep -a 'test result' "$work/judge.txt")"

# The ELF, built as Aethelgard's CI builds it, then held to its hash.
mkdir -p "$work/elf"
container -v "$fix/container:/container:ro" -v "$fix/boards:/boards:ro" -v "$work/elf:/out" "$AP_IMAGE" \
  bash /container/ardupilot-build.sh boards sentinel-02 >"$work/elf.log" 2>&1 || { tail -5 "$work/elf.log"; echo "FAIL the ArduPilot build"; exit 1; }
got=$(sha256sum "$work/elf/sentinel-02/arducopter.elf" | cut -c1-64)
echo "ELF sha256 $got (want $ELF_SHA)"
[ "$got" = "$ELF_SHA" ] || { echo "FAIL the ELF built is $got, not $ELF_SHA: a question for Scotty (1301, ruled 19:08Z)"; exit 1; }
cap=$work/cap/2
mkdir -p "$cap"
cp -r "$fix/argv.bin" "$fix/varlib" "$cap/"
tool=$(echo "$cap"/varlib/*/tool)
mkdir -p "$tool/.config"
cp "$work/elf/sentinel-02/arducopter.elf" "$tool/firmware.elf"

# The runner, for the spread between runs (WO (4)): recorded, not judged.
{
  echo "cpu $(sed -n 's/^model name[[:space:]]*: //p' /proc/cpuinfo | head -1); nproc $(nproc); $(lscpu 2>/dev/null | grep -m1 '^L3' | tr -s ' ')"
  echo "memory $(awk '/^MemTotal:|^MemAvailable:|^SwapTotal:/ {printf "%s %s kB; ", $1, $2}' /proc/meminfo)"
  echo "thp enabled=$(cat /sys/kernel/mm/transparent_hugepage/enabled 2>/dev/null) defrag=$(cat /sys/kernel/mm/transparent_hugepage/defrag 2>/dev/null)"
  echo "kernel $(uname -r); runner image ${ImageOS:-none} ${ImageVersion:-none}"
} | tee "$work/runner.txt"

# Interleaved, and the order alternates each round (main first, then branch first), so a drift over the job or a
# first boot's extra cost falls on both sides alike.
printf '%s\t' side run peak_kib wall_s boot_end witnesses_own witnesses_other would_refuse rss_anon_kib rss_file_kib \
  rss_shmem_kib anon_huge_kib threads processes >"$work/runs.tsv"
printf 'snapshot_sum_kib\n' >>"$work/runs.tsv"
for r in $(seq 1 "$runs"); do
  order="main branch"; [ $((r % 2)) = 0 ] && order="branch main"
  for s in $order; do
    out=$work/out-$s-$r
    mkdir -p "$out"
    avail=$(awk '/^MemAvailable:/ {print $2}' /proc/meminfo)
    container -e ACCEPT_MODE=recorded -v "$work/cap:/cap:ro" -v "${tree[$s]}:/repo:ro" -v "$here/tests/1301:/h:ro" \
      -v "$work/rel-$s/release:/tmp/release:ro" -v "$out:/out" "$IMAGE" bash /h/replay-1301.sh 2 accept >"$out/replay.txt" 2>&1 || true
    con=$out/2-accept.console.txt cost=$(cat "$out/2-accept.cost.txt" 2>/dev/null || true)
    peak=$(sed -n 's/.*peak_tree_vmrss_kib=\([0-9]*\).*/\1/p' <<<"$cost")
    wall=$(sed -n 's/.*wall_s=\([0-9.]*\).*/\1/p' <<<"$cost")
    end=$(grep -a -c '^AETHELGARD-BOOT-END' "$con" 2>/dev/null || true)
    mine=$(grep -a -c "\"tviburi\":\"witness\".*\"model_version\":\"${hash[$s]}\"" "$con" 2>/dev/null || true)
    other=$(grep -a '"tviburi":"witness"' "$con" 2>/dev/null | grep -a -c -v "\"model_version\":\"${hash[$s]}\"" || true)
    would=$(grep -a -c 'gpioPort[A-K]: {"tviburi":"would-refuse"' "$con" 2>/dev/null || true)
    # What the peak is made of (WO (4)): the Renode process's own breakdown at its peak sample, in KiB.
    # The snapshot is the last whole one, at or before the peak; its own sample's sum is the last column.
    parts=$'\t\t\t\t\t\t'   # no whole snapshot: empty fields, so the row stays whole and the judge names the boot
    [ -s "$out/2-accept.peak-proc.txt" ] && parts=$(awk '/^(RssAnon|RssFile|RssShmem|AnonHugePages|Threads):/ {v[$1] = $2} /^pid / {v["n"] = $4; v["s"] = $7}
      END {printf "%s\t%s\t%s\t%s\t%s\t%s\t%s", v["RssAnon:"], v["RssFile:"], v["RssShmem:"], v["AnonHugePages:"], v["Threads:"], v["n"], v["s"]}' \
      "$out/2-accept.peak-proc.txt")
    printf '%s\t%s\t%s\t%s\t%s\t%s\t%s\t%s\t%s\n' $s $r "$peak" "${wall:-none}" "${end:-0}" "${mine:-0}" "${other:-0}" "${would:-0}" \
      "$parts" >> "$work/runs.tsv"
    echo "$s boot $r: peak ${peak:-none} KiB, wall ${wall:-none} s, boot end ${end:-0}, witnesses of its release ${mine:-0}" \
      "and of another ${other:-0}, GPIO would-refuse ${would:-0}, host MemAvailable before $avail kB; at the peak" \
      "RssAnon/RssFile/RssShmem/AnonHugePages/Threads/processes/snapshot-sum $(tr '\t' / <<<"$parts")"
  done
done

verdict=0
container -v "$work:/w:ro" "$IMAGE" /w/judge/debug/tviburi-budget /w/runs.tsv >"$work/verdict.txt" 2>&1 || verdict=$?
cat "$work/verdict.txt"
echo "outputs: $work"
if [ -n "${GITHUB_STEP_SUMMARY:-}" ]; then
  {
    echo "### Memory budget: ${TVIBURI_MEMORY_LABEL:-the branch against main}"
    echo
    echo "main ${hash[main]} · branch ${hash[branch]}"
    echo
    echo '```'; cat "$work/verdict.txt"; echo '```'
    echo
    awk -F'\t' '{for (i = 1; i <= NF; i++) printf "| %s ", $i; print "|"} NR == 1 {for (i = 1; i <= NF; i++) printf "|---"; print "|"}' \
      "$work/runs.tsv"
    echo
    echo '```'; cat "$work/runner.txt"; echo '```'
  } >> "$GITHUB_STEP_SUMMARY"
fi
exit $verdict
