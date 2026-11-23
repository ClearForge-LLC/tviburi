# TVI-WO-1100 scratch reader: every witness-ledger row of one `aethelgard verify` verdict, as TSV.
# Usage: jq -r --arg board NAME -f ledger-rows.jq verify-<spec>.json
# Columns: board id family register field pos width access value class also_ignored acknowledged judge note
# `also_ignored` is 1 for a row whose note says "also ignored-by-model" (ledger.rs: the stronger class wins and
# the note names the other), so a judged row the stock model still drops is never lost to a class filter.
.ledger.rows[]
| [ $board, .id, .family, .register, .field, (.pos|tostring), (.width|tostring), .access,
    (if .value == null then "null" else (.value|tostring) end), .class,
    (if (.note|test("also ignored-by-model")) then "1" else "0" end),
    (if .acknowledged == null then "-" else (.acknowledged|tostring) end),
    (.judge|gsub("[\t\n]";" ")), (.note|gsub("[\t\n]";" ")) ]
| @tsv
