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
# A boot counts only if Renode reaches the boot's end, a peak was sampled, every guard's witness reports the release
# hash of the tree that boot is labelled with (and none another), and, on the branch, no GPIO would-refuse.
# The verdict (`judge`, below; `ci-memory.sh --judge RUNS.tsv` judges a table alone) refuses by name:
#   failed-boot     any boot that does not count, on either side; it is never dropped from a median
#   too-few-boots   fewer than 5 boots a side
#   ceiling         any branch boot over 1,572,864 KiB (the consumer's ceiling, Aethelgard RUL-13 as amended 2026-11-23)
#   budget          the branch's median more than 32 MiB over main's
# Exit 0 only on PASS. In CI (TVIBURI_CI=1) containers run bare on the unshared runner; elsewhere under capped-run.
set -euo pipefail

here=$(cd "$(dirname "$0")/../.." && pwd)
IMAGE=$(sed -n 's/^IMAGE=//p' "$here/tests/spike.sh")
AP_IMAGE=ghcr.io/clearforge-llc/aethelgard-ardupilot@sha256:e1809b909b02c7fd01b593a65f32d191169a5027ef61ceeae8e74cc08117c2b6
ELF_SHA=a799dfb6633537e74a8c3b7d8b6a464f185c255e377a612b7f9fcf8378dab1ef
MIN_BOOTS=5
CEILING_KIB=1572864
BUDGET_KIB=$((32 * 1024))

# judge RUNS.tsv — columns: side (main|branch), run, peak KiB, wall s, status ("ok" or why the boot does not count).
judge() {
  local t=$1 verdict=0 s n m d
  local -A med
  awk -F'\t' '$5 != "ok" {printf "REFUSE failed-boot: %s boot %s does not count (%s)\n", $1, $2, $5}' "$t" | grep . && verdict=1
  for s in main branch; do
    n=$(awk -F'\t' -v s=$s '$1 == s' "$t" | wc -l)
    [ "$n" -ge $MIN_BOOTS ] || { echo "REFUSE too-few-boots: $s has $n boots, at least $MIN_BOOTS are needed"; verdict=1; }
    m=$(awk -F'\t' -v s=$s '$1 == s && $5 == "ok" {print $3}' "$t" | sort -n \
      | awk '{a[NR] = $1} END {if (NR) print (NR % 2 ? a[(NR + 1) / 2] : int((a[NR / 2] + a[NR / 2 + 1]) / 2))}')
    med[$s]=${m:-none}
  done
  awk -F'\t' -v c=$CEILING_KIB '$1 == "branch" && $3 > c {printf "REFUSE ceiling: branch boot %s peaked at %d KiB, over %d KiB\n", $2, $3, c}' "$t" \
    | grep . && verdict=1
  if [ "${med[main]}" = none ] || [ "${med[branch]}" = none ]; then
    echo "REFUSE too-few-boots: no counted boot on one side (medians: main ${med[main]}, branch ${med[branch]})"; verdict=1
  else
    d=$((med[branch] - med[main]))
    echo "MEDIANS main ${med[main]} KiB, branch ${med[branch]} KiB, difference $d KiB ($(awk -v d=$d 'BEGIN {printf "%+.1f", d / 1024}') MiB)"
    [ $d -le $BUDGET_KIB ] || { echo "REFUSE budget: the branch's median is $d KiB over main's, more than $BUDGET_KIB KiB (32 MiB)"; verdict=1; }
  fi
  [ $verdict = 0 ] && echo "PASS: every boot counts, every branch boot at or under $CEILING_KIB KiB, the median within 32 MiB of main's"
  return $verdict
}
if [ "${1:-}" = --judge ]; then judge "$2"; exit; fi

scratch=${TVIBURI_SCRATCH:-$HOME/.cache/tviburi}
mkdir -p "$scratch"
work=$(mktemp -d "$scratch/memory.XXXXXX")
if [ "${TVIBURI_CI:-}" = 1 ]; then
  container() { podman run --rm --network none "$@"; }
else
  [ -x "$HOME/bin/capped-run" ] || { echo "no ~/bin/capped-run; TVIBURI_CI=1 is for CI runners only" >&2; exit 2; }
  container() { nice -n 10 "$HOME/bin/capped-run" --mem 12G -- --rm --network none "$@"; }
fi
runs=${TVIBURI_MEMORY_RUNS:-$MIN_BOOTS}
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
printf '' > "$work/runs.tsv"
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
    why=""
    [ -n "$peak" ] && [ "$peak" -gt 0 ] || why="$why no peak sampled;"
    [ "${end:-0}" = 1 ] || why="$why boot end ${end:-0};"
    [ "${mine:-0}" -ge 1 ] || why="$why no witness of ${hash[$s]};"
    [ "${other:-0}" = 0 ] || why="$why $other witnesses of another release;"
    [ $s = main ] || [ "${would:-0}" = 0 ] || why="$why $would GPIO would-refuse;"
    status=${why:+${why# }}; status=${status:-ok}
    # What the peak is made of (WO (4)): the Renode process's own breakdown at its peak sample, in KiB.
    parts=$(awk '/^(RssAnon|RssFile|RssShmem|AnonHugePages|Threads):/ {v[$1] = $2} /^pid / {v["n"] = $4}
      END {printf "%s\t%s\t%s\t%s\t%s\t%s", v["RssAnon:"], v["RssFile:"], v["RssShmem:"], v["AnonHugePages:"], v["Threads:"], v["n"]}' \
      "$out/2-accept.peak-proc.txt" 2>/dev/null || true)
    printf '%s\t%s\t%s\t%s\t%s\t%s\t%s\t%s\t%s\n' $s $r "${peak:-0}" "${wall:-none}" "$status" "$mine" "$avail" \
      "$(cut -d' ' -f1-3 /proc/loadavg)" "$parts" >> "$work/runs.tsv"
    echo "$s boot $r: peak ${peak:-none} KiB, wall ${wall:-none} s, witnesses of its release $mine, host MemAvailable" \
      "before $avail kB; at the peak RssAnon/RssFile/RssShmem/AnonHugePages/Threads/processes $(tr '\t' / <<<"$parts"): $status"
  done
done

verdict=0
judge "$work/runs.tsv" | tee "$work/verdict.txt" || verdict=1
echo "outputs: $work"
if [ -n "${GITHUB_STEP_SUMMARY:-}" ]; then
  {
    echo "### Memory budget: ${TVIBURI_MEMORY_LABEL:-the branch against main}"
    echo
    echo "main ${hash[main]} · branch ${hash[branch]}"
    echo
    echo '```'; cat "$work/verdict.txt"; echo '```'
    echo
    echo "| side | boot | peak (KiB) | wall (s) | counts? | witnesses | RssAnon | RssFile | RssShmem | AnonHugePages | threads | processes |"
    echo "|---|---|---|---|---|---|---|---|---|---|---|---|"
    awk -F'\t' '{printf "| %s | %s | %s | %s | %s | %s | %s | %s | %s | %s | %s | %s |\n", $1, $2, $3, $4, $5, $6, $9, $10, $11, $12, $13, $14}' \
      "$work/runs.tsv"
    echo
    echo '```'; cat "$work/runner.txt"; echo '```'
  } >> "$GITHUB_STEP_SUMMARY"
fi
exit $verdict
