#!/usr/bin/env bash
# tests/adversarial.sh [SRC] — the TVI-WO-0100a adversarial cases (WO §5), committed so CI runs
# them. They live in tests/spike.sh, beside the acceptance tests, and share its helpers.
exec env TVIBURI_TESTS=adversarial "$(dirname "$0")/spike.sh" "$@"
