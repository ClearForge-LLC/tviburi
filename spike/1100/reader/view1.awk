# TVI-WO-1100 scratch reader, view (1): each ledger row with its register address and the table entry it
# was classified by (ledger.rs classify(): the first entry with the row's family and access whose register
# and field patterns match, `#` = one or more digits), and that entry's `source`, which the verdict omits.
# Usage: awk -F'\t' -f view1.awk cmsis-regs.tsv table-entries.tsv ledger-<board>.tsv...
# Output columns: board id family register field pos width access value class also_ignored address
#                 entry_line entry_class source
BEGIN { OFS = "\t" }
function rx(p) { gsub(/#/, "[0-9]+", p); return "^" p "$" }
FILENAME == ARGV[1] { addr[$1 "." $2] = $3; next }
FILENAME == ARGV[2] { n++; tf[n] = $1; tr[n] = rx($2); tfl[n] = rx($3); ta[n] = $4; tc[n] = $5; ts[n] = $6; tl[n] = $7; next }
{
  reg = $4; sub(/^[^.]*\./, "", reg)
  a = ($4 in addr) ? addr[$4] : "-"
  if ($4 == "SCB.CPACR") a = "0xE000ED88"
  if (reg == "AFRL" || reg == "AFRH") { k = $4; sub(/AFRL$/, "AFR0", k); sub(/AFRH$/, "AFR1", k); a = addr[k] }
  hit = 0
  for (i = 1; i <= n; i++)
    if (tf[i] == $3 && ta[i] == $8 && reg ~ tr[i] && $5 ~ tfl[i]) { hit = i; break }
  if (hit) print $1, $2, $3, $4, $5, $6, $7, $8, $9, $10, $11, a, tl[hit], tc[hit], (ts[hit] == "" ? "-" : ts[hit])
  else print $1, $2, $3, $4, $5, $6, $7, $8, $9, $10, $11, a, "-", "-", "NO-TABLE-ENTRY (row from a non-table builder; see note)"
}
