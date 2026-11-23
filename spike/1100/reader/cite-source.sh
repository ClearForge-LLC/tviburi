#!/bin/bash
# TVI-WO-1100 scratch reader: append to each queue row the renode-infrastructure source line(s) that declare
# the executed tag or field covering it (queue column 12: "tag NAME [..]" / "field NAME [..]"), found by
# name in the model class's own file, then in its base class's file, at 066a7f13 (byte-identical to the
# executed cd4b002a tree under src/Emulator/Peripherals, measured). Lookups, in order:
#   "NAME" literally; NAME with each digit run as digits or an {interpolation}; an array field BASE_<i>
#   (name: "BASE"); a loop-named BASE<i> ("BASE).
# Usage: cite-source.sh SRC-ROOT queue.tsv > queue-cited.tsv
set -euo pipefail
src=$1; q=$2
files=$(mktemp); trap 'rm -f "$files"' EXIT
cut -f11 "$q" | sed -E 's/.*\(([^)]*)\)$/\1/; s/.*\.//' | sort -u | while read -r short; do
  f=$(find "$src" -name "$short.cs" \( -path '*Peripherals*' -o -path '*Cores*' \) -not -path '*/Test/*' | head -1)
  b=""
  if [ -n "$f" ]; then
    base=$(grep -oE "class $short *: *[A-Za-z0-9_]+" "$f" | head -1 | sed -E 's/.*: *//' || true)
    [ -n "$base" ] && b=$(find "$src" -name "$base.cs" -path '*Peripherals*' -not -path '*/Test/*' | head -1)
  fi
  printf '%s\t%s\t%s\n' "$short" "${f:--}" "${b:--}"
done > "$files"
gawk -F'\t' -v OFS='\t' '
  function rel(p) { sub(/.*\/src\/Emulator\//, "src/Emulator/", p); return p }
  function look(nm, f,   ln, cmd, re, b) {
    if (f == "-" || f == "") return ""
    ln = ""; cmd = "grep -n -F -- \"\\\"" nm "\\\"\" \"" f "\" | head -2 | cut -d: -f1 | paste -sd, -"; cmd | getline ln; close(cmd)
    if (ln != "") return ln
    re = nm; gsub(/[][().*+?^$|\\]/, "\\\\&", re); gsub(/[0-9]+/, "([0-9]+|\\{[^}]*\\})", re)
    cmd = "grep -n -E -- \"" re "\" \"" f "\" | head -2 | cut -d: -f1 | paste -sd, -"; cmd | getline ln; close(cmd)
    if (ln != "") return ln " (interpolated)"
    if (nm ~ /_[0-9]+$/) { b = nm; sub(/_[0-9]+$/, "", b)
      cmd = "grep -n -F -- \"name: \\\"" b "\\\"\" \"" f "\" | head -1 | cut -d: -f1"; cmd | getline ln; close(cmd)
      if (ln != "") return ln " (array " b ")" }
    if (nm ~ /[0-9]+$/) { b = nm; sub(/[0-9]+$/, "", b)
      cmd = "grep -n -F -- \"\\\"" b "\" \"" f "\" | head -1 | cut -d: -f1"; cmd | getline ln; close(cmd)
      if (ln != "") return ln " (loop " b ")" }
    return ""
  }
  FILENAME == ARGV[1] { own[$1] = $2; base[$1] = $3; next }
  {
    m = $11; sub(/.*\(/, "", m); sub(/\)$/, "", m); short = m; sub(/.*\./, "", short)
    if (short == "PythonPeripheral") { print $0, "Aethelgard'"'"'s PWR script (the board .repl, `pwr: script`), not a stock model"; next }
    f = own[short]; if (f == "" || f == "-") { print $0, "-"; next }
    if ($12 ~ /not in the register collection/) { print $0, rel(f) " (an offset handled in the model'"'"'s own switch, not its register collection)"; next }
    cite = ""; s = $12; delete seen
    while (match(s, /(tag|field) ([^[]+) \[/, mm)) {
      nm = mm[2]; s = substr(s, RSTART + RLENGTH)
      if (nm in seen) continue; seen[nm] = 1
      if (nm == "RESERVED" || nm == "ignored") { cite = cite (cite ? "; " : "") nm " (WithReservedBits/WithIgnoredBits: no name)"; continue }
      ln = look(nm, f); where = rel(f)
      if (ln == "") { ln = look(nm, base[short]); where = rel(base[short]) }
      cite = cite (cite ? "; " : "") nm " " (ln == "" ? "L?" : where " L" ln)
    }
    if ($12 ~ /undefined bits/) cite = cite (cite ? "; " : "") "undefined bits: no field or tag in " rel(f)
    print $0, cite
  }' "$files" "$q"
