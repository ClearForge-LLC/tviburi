#!/usr/bin/env bash
# TVI-WO-0100 proof. Requires Renode 1.17.0 on PATH or in RENODE.
# Does not download anything and does not read credentials.
set -euo pipefail

root="$(cd "$(dirname "$0")/.." && pwd)"
cd "$root"

renode_bin="${RENODE:-renode}"
if [[ ! -x "$renode_bin" ]]; then
    echo "Renode 1.17.0 is required (set RENODE to the renode binary)." >&2
    exit 2
fi

version="$("$renode_bin" --version 2>&1 | head -n 1 || true)"
case "$version" in
    "Renode v1.17.0") ;;
    *)
        echo "Refusing to run: expected 'Renode v1.17.0', got: $version" >&2
        exit 2
        ;;
esac

export RENODE_CI_MODE=YES
shim="renode/Tviburi_STM32H7_SPI1.cs"
expected_hash="$(python3 -c 'import hashlib,sys; print(hashlib.sha256(open(sys.argv[1],"rb").read()).hexdigest())' "$shim")"

run_renode() {
    local extra=()
    if [[ "${1:-}" == "--hide-log" ]]; then
        extra+=(--hide-log)
        shift
    fi
    local script="$1"
    local out="$2"
    set +e
    # --console prints the monitor. -P -1 disables the telnet monitor
    # (port 1234) that would otherwise stay up after the script.
    timeout 180 "$renode_bin" --console --plain -P -1 "${extra[@]}" "$script" </dev/null >"$out" 2>&1
    local code=$?
    set -e
    echo "$code"
}

assert_fields() {
    local file="$1"
    python3 - "$file" "$expected_hash" <<'PY'
import sys
text = open(sys.argv[1], errors="replace").read()
need = [
    "peripheral=SPI1",
    "register=SPI_SR.MODF bit9",
    "condition=SPI_CR1.SPE set; silicon raises SPI_SR.MODF and clears MASTER; this model does not",
    "model_version=tvi-wo-0100+renode-1.17.0",
    "model_hash=" + sys.argv[2],
]
missing = [n for n in need if n not in text]
if missing:
    sys.stderr.write("missing refusal fields:\n" + "\n".join(missing) + "\n")
    sys.exit(1)
if "Tviburi access: peripheral=SPI1 offset=0x0 kind=write value=0x1 width=4" not in text:
    sys.stderr.write("refused access was not recorded\n")
    sys.exit(1)
PY
}

modf_out="$(mktemp)"
modf_code="$(run_renode renode/prove-modf-refuses.resc "$modf_out")"
if [[ "$modf_code" -eq 0 ]]; then
    echo "SPI1 MODF invocation exited 0 (silent success)" >&2
    cat "$modf_out" >&2 || true
    exit 1
fi
if [[ "$modf_code" -eq 124 ]]; then
    echo "SPI1 MODF invocation timed out" >&2
    exit 1
fi
if grep -q "UNEXPECTED SUCCESS" "$modf_out"; then
    echo "SPI1 MODF invocation reached the success sentinel" >&2
    exit 1
fi
cat "$modf_out" /tmp/tvi-wo-0100-modf.log > /tmp/tvi-wo-0100-modf.combined
assert_fields /tmp/tvi-wo-0100-modf.combined
echo "modf refusal exit=$modf_code"

modeled_out="$(mktemp)"
modeled_code="$(run_renode renode/prove-modeled-succeeds.resc "$modeled_out")"
if [[ "$modeled_code" -ne 0 ]]; then
    echo "modeled SPI4 invocation failed with exit $modeled_code" >&2
    cat "$modeled_out" >&2 || true
    exit 1
fi
if grep -q "Tviburi refusal:" "$modeled_out" /tmp/tvi-wo-0100-modeled.log; then
    echo "modeled SPI4 path refused" >&2
    exit 1
fi
python3 - "$modeled_out" <<'PY'
import sys
text = open(sys.argv[1], errors="replace").read()
# Stock STM32H7_SPI names the register Control1. SPE is bit 0 and reads back as 0x1.
if "ReadUInt32 from 0x0 (Control1), returned 0x1" not in text and "0x00000001" not in text:
    sys.stderr.write("SPI4 CR1 readback did not show SPE set\n")
    sys.stderr.write(text)
    sys.exit(1)
PY
echo "modeled SPI4 exit=0"

read_out="$(mktemp)"
read_code="$(run_renode renode/prove-spi1-read-refuses.resc "$read_out")"
if [[ "$read_code" -eq 0 || "$read_code" -eq 124 ]]; then
    echo "SPI1 read invocation did not fail closed (exit $read_code)" >&2
    cat "$read_out" >&2 || true
    exit 1
fi
if grep -q "UNEXPECTED SUCCESS" "$read_out"; then
    echo "SPI1 read reached the success sentinel" >&2
    exit 1
fi
if ! grep -q "Tviburi refusal: peripheral=SPI1" "$read_out" /tmp/tvi-wo-0100-spi1-read.log; then
    echo "SPI1 read did not refuse by name" >&2
    cat "$read_out" >&2 || true
    exit 1
fi
echo "spi1 read refusal exit=$read_code"

hide_out="$(mktemp)"
hide_code="$(run_renode --hide-log renode/prove-modf-refuses.resc "$hide_out")"
if [[ "$hide_code" -eq 0 || "$hide_code" -eq 124 ]]; then
    echo "--hide-log suppressed the refusal (exit $hide_code)" >&2
    exit 1
fi
if grep -q "UNEXPECTED SUCCESS" "$hide_out"; then
    echo "--hide-log reached the success sentinel" >&2
    exit 1
fi
assert_fields /tmp/tvi-wo-0100-modf.log
echo "hide-log still refuses exit=$hide_code"

# Removing the shim must not fall through to the tag's silent success.
noshi_out="$(mktemp)"
noshi_resc="$(mktemp)"
grep -v 'Tviburi_STM32H7_SPI1.cs' renode/prove-modf-refuses.resc | grep -v 'EnsureTypeIsLoaded' > "$noshi_resc"
noshi_code="$(run_renode "$noshi_resc" "$noshi_out")"
if grep -q "UNEXPECTED SUCCESS" "$noshi_out"; then
    echo "missing shim fell through to success" >&2
    cat "$noshi_out" >&2 || true
    exit 1
fi
if ! grep -q -F "Could not resolve type: 'SPI.Tviburi_STM32H7_SPI1'" "$noshi_out"; then
    echo "missing shim did not fail closed on the platform type (exit $noshi_code)" >&2
    cat "$noshi_out" >&2 || true
    exit 1
fi
echo "missing shim fail-closed exit=$noshi_code"
echo "TVI-WO-0100 proof ok"
echo "model_hash=$expected_hash"
