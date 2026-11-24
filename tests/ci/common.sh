# tests/ci/common.sh — sourced by tests/ci/*.sh (TVI-WO-1202): the pinned image (tests/spike.sh's IMAGE), the
# container runner, and tools/tviburi-route built from this checkout into a scratch directory and run in the image,
# with the checkout read-only at /repo (its working directory) and the scratch at /w.
here=$(cd "$(dirname "${BASH_SOURCE[0]}")/../.." && pwd)
IMAGE=$(sed -n 's/^IMAGE=//p' "$here/tests/spike.sh")
scratch=${TVIBURI_SCRATCH:-$HOME/.cache/tviburi}
mkdir -p "$scratch"
ci=$(mktemp -d "$scratch/ci.XXXXXX")
if [ "${TVIBURI_CI:-}" = 1 ]; then
  container() { podman run --rm --network none "$@"; }
else
  [ -x "$HOME/bin/capped-run" ] || { echo "no ~/bin/capped-run; TVIBURI_CI=1 is for CI runners only" >&2; exit 2; }
  container() { nice -n 10 "$HOME/bin/capped-run" --mem 4G -- --rm --network none "$@"; }
fi
in_image() { container -v "$here:/repo:ro,Z" -v "$ci:/w:Z" -w /repo "$IMAGE" sh -c \
  "export PATH=/opt/cargo/bin:\$PATH RUSTUP_HOME=/opt/rustup CARGO_HOME=/w/cargo-home; $1"; }
cargo_route() { in_image "cargo $1 --quiet --offline --locked --manifest-path tools/tviburi-route/Cargo.toml --target-dir /w/target"; }
cases_list() { TVIBURI_TESTS=list "$here/tests/spike.sh" >"$ci/cases.txt"; }   # the tree's own case ids, in order
route() { container -v "$here:/repo:ro,Z" -v "$ci:/w:Z" -w /repo "$IMAGE" /w/target/debug/tviburi-route "$@"; }
