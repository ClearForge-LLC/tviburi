#!/usr/bin/env bash
# ci/release.sh — TVI-WO-2100 (1): the release job's steps, the same on a CI runner (TVIBURI_CI=1, run by
# .github/workflows/release.yml) and on a workstation (under ~/bin/capped-run). It never tags and never publishes.
#
#   ci/release.sh all COMMIT [MAIN]   COMMIT must be on MAIN (default origin/main) and the checkout clean. Two builds of
#                                     COMMIT, each from its own `git archive` extraction (umask 022 and 077, mtimes 2000
#                                     and 2030, as tests/spike.sh's `reproducible` makes them), compared byte for byte;
#                                     `verify` on the first against the hash it printed (or RELEASE_PIN if set); then the
#                                     tarball, MANIFEST and LEDGER.jsonl kept in RELEASE_OUT with summary.md.
#   ci/release.sh tree TREE OUT       one build of the source tree TREE into OUT/release (the hash on stdout)
#   ci/release.sh compare A B         byte-compare two builds' files; on a difference, the file and its first differing byte
#   ci/release.sh verify DIR PIN      `tviburi-release verify` on DIR against PIN (sha256:<hex>)
# Exit 0 only when every step held; 1 on a failed check (named); 2 on a usage error.
set -euo pipefail
. "$(cd "$(dirname "${BASH_SOURCE[0]}")/.." && pwd)/tests/ci/common.sh"   # here, IMAGE, container(), scratch
fail() { echo "RELEASE FAIL: $*"; exit 1; }
jobs() { [ -n "${CARGO_BUILD_JOBS:-}" ] && printf -- '-e\nCARGO_BUILD_JOBS=%s\n' "$CARGO_BUILD_JOBS"; return 0; }

build_tree() { # build_tree TREE OUT: the tree's own scripts/build-release, in the pinned image, the tree read-only
  local tree=$1 out=$2 opts
  mkdir -p "$out"
  mapfile -t opts < <(jobs)
  container "${opts[@]}" -v "$tree:/repo:ro,Z" -v "$out:/out:Z" "$IMAGE" sh /repo/scripts/build-release /repo /out/release \
    2>"$out/build.err" | tail -n 1
}

compare() { # compare A B: the two release directories' listings, then every file, byte for byte, the tarball last (it
  # holds the others, so a difference inside it is named by the file that carries it)
  local a=$1/release b=$2/release f d=""
  [ "$(cd "$a" && ls -A)" = "$(cd "$b" && ls -A)" ] || fail "the two builds hold different files: $(cd "$a" && ls -A | tr '\n' ' ')/ $(cd "$b" && ls -A | tr '\n' ' ')"
  for f in $(cd "$a" && ls -A | grep -vx tviburi-release.tar) tviburi-release.tar; do
    cmp "$a/$f" "$b/$f" >/dev/null || d="$d $f ($(cmp "$a/$f" "$b/$f" | sed 's/^[^ ]* [^ ]* //'));"
  done
  [ -z "$d" ] || fail "the two builds differ:$d"
  echo "the two builds are byte-identical: $(cd "$a" && ls -A | tr '\n' ' ')"
}

verify() { # verify DIR PIN [TREE]: the verifier built from TREE (the released commit's own tree; default this checkout)
  local dir=$1 pin=$2 tree=${3:-$here} v
  v=$(container -v "$dir:/rel:ro,Z" -v "$tree:/repo:ro,Z" -v "$ci:/w:Z" "$IMAGE" sh -c "export PATH=/opt/cargo/bin:\$PATH \
    RUSTUP_HOME=/opt/rustup CARGO_HOME=/w/cargo-home ${CARGO_BUILD_JOBS:+CARGO_BUILD_JOBS=$CARGO_BUILD_JOBS}
    cargo build --quiet --offline --locked --manifest-path /repo/tools/tviburi-release/Cargo.toml --target-dir /w/target &&
    /w/target/debug/tviburi-release verify /rel/release $pin" 2>&1) && { echo "$v" | tail -n 1; return 0; }
  echo "$v" | tail -n 5
  fail "verify against $pin"
}

extract() { # extract COMMIT DIR UMASK STAMP: the commit's tree, as a consumer's checkout would differ from this one
  mkdir -p "$2"
  (umask "$3" && git -C "$here" archive "$1" | tar -C "$2" -xf - -m --no-same-permissions)
  find "$2" -exec touch -h -t "$4" {} +
}

all() { # all COMMIT [MAIN]
  local commit main sha h1 h2 out=${RELEASE_OUT:-$ci/out}
  commit=$(git -C "$here" rev-parse --verify -q "$1^{commit}") || fail "$1 is not a commit"
  main=${2:-origin/main}
  git -C "$here" merge-base --is-ancestor "$commit" "$main" || fail "$commit is not on $main"
  [ -z "$(git -C "$here" status --porcelain --untracked-files=normal)" ] || fail "the checkout is not clean: $(git -C "$here" status --porcelain | head -n 3 | tr '\n' ' ')"
  extract "$commit" "$ci/a.tree" 022 200001010000
  extract "$commit" "$ci/b.tree" 077 203012312359
  h1=$(build_tree "$ci/a.tree" "$ci/a") || fail "the first build failed: $(tail -n 3 "$ci/a/build.err")"
  h2=$(build_tree "$ci/b.tree" "$ci/b") || fail "the second build failed: $(tail -n 3 "$ci/b/build.err")"
  [ "$h1" = "$h2" ] || fail "the two builds printed different hashes: $h1, $h2"
  compare "$ci/a" "$ci/b"
  verify "$ci/a" "${RELEASE_PIN:-$h1}" "$ci/a.tree"
  mkdir -p "$out"
  for f in tviburi-release.tar MANIFEST LEDGER.jsonl; do cp "$ci/a/release/$f" "$out/$f"; done
  sha=$(sha256sum <"$out/tviburi-release.tar" | cut -c1-64)
  { echo "## Tviburi release (not tagged, not published)"; echo
    echo "| | |"; echo "|---|---|"
    echo "| commit | \`$commit\` (on \`$main\` at \`$(git -C "$here" rev-parse "$main")\`) |"
    echo "| release hash (sha256 of MANIFEST; the witnesses' model_version) | \`$h1\` |"
    echo "| ledger sha256 (LEDGER.jsonl) | \`sha256:$(sha256sum <"$out/LEDGER.jsonl" | cut -c1-64)\` |"
    echo "| ledger rows (MANIFEST \`ledger-rows\`) | \`sha256:$(sed -n 's/^ledger-rows \([0-9a-f]*\) .*/\1/p' "$out/MANIFEST")\` |"
    echo "| tarball sha256 | \`sha256:$sha\` |"
    echo "| Renode build (MANIFEST) | \`$(sed -n 's/^build renode //p' "$out/MANIFEST")\` |"
    echo "| image | \`$IMAGE\` |"
    echo; echo "Two builds byte-identical (every file in each); \`verify\` passed against \`${RELEASE_PIN:-$h1}\`."; } >"$out/summary.md"
  cat "$out/summary.md"
}

case "${1:-}" in
  all) [ $# -ge 2 ] && [ $# -le 3 ] || exit 2; all "${@:2}" ;;
  tree) [ $# -eq 3 ] || exit 2; build_tree "$(cd "$2" && pwd)" "$3" ;;
  compare) [ $# -eq 3 ] || exit 2; compare "$2" "$3" ;;
  verify) [ $# -eq 3 ] || exit 2; verify "$(cd "$2" && pwd)" "$3" ;;
  *) echo "usage: ci/release.sh all COMMIT [MAIN] | tree TREE OUT | compare A B | verify DIR PIN" >&2; exit 2 ;;
esac
