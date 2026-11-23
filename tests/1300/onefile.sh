#!/bin/bash
# onefile.sh OUT FILE... : RUL-09 (a)'s one compilation: every `using` line hoisted (sorted, unique), then each file's
# body in the order given. As 1150 (vi)'s hand-concatenated one-file overlay.
out=$1; shift
{ grep -h '^using [A-Za-z0-9_.]*;' "$@" | sort -u; for f in "$@"; do echo; echo "// ---- $(basename "$f")"; grep -v '^using [A-Za-z0-9_.]*;' "$f"; done; } > "$out"
