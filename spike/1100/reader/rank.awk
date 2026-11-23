# TVI-WO-1100 scratch reader: merge the per-board joins and rank them by spike/1100/predictions.md §2.
# Usage: gawk -F'\t' -f rank.awk boards.tsv pins-*.tsv joined-*.tsv > queue.tsv
#   boards.tsv: board <TAB> spec <TAB> motor labels (comma-separated, "-" for none)
#   pins*.tsv: pins.awk output     joined*.tsv: view2-join.awk output (21 columns)
# A row enters the queue when, on any board, the ledger classes it ignored-by-model or same-source, or
# its note says "also ignored-by-model", or the executed model drops a written value there.
# Output: rank cls clsname nboards(in class)/nboards(all) boards(in class) id ledger_classes also_ign access values model view2 drop
#         fw_writes one_sided entry_line source
BEGIN { OFS = "\t"; split("motor output,clocks,power,sensor buses,the rest", CN, ",") }
FILENAME ~ /boards\.tsv$/ { motorlab[$1] = $3; next }
FILENAME ~ /pins(-[^/]*)?\.tsv$/ { role[$1, $3, $4] = $5 "|" $7; if ($7 ~ /^TIM/ && index("," motorlab[$1] ",", "," $5 ",")) mtim[$1, $7] = 1; next }
function pinrole(b, reg, field,   port, idx, r, net, per) {
  port = reg; sub(/\..*/, "", port); idx = field; gsub(/[^0-9]/, "", idx)
  if (!((b, port, idx) in role)) return 5
  split(role[b, port, idx], r, "|"); net = r[1]; per = r[2]
  if (index("," motorlab[b] ",", "," net ",") || per ~ /^TIM/) return 1
  if (per ~ /^(SPI|I2C)/ || net ~ /_CS$/) return 4
  return 5
}
function klass(b, fam, reg, field,   inst) {
  inst = reg; sub(/\..*/, "", inst)
  if (inst ~ /^TIM[0-9]+$/) return ((b, inst) in mtim) ? 1 : 5
  if (inst ~ /^(DMA[0-9]|DMAMUX|BDMA)/) return (motorlab[b] != "-") ? 1 : 5
  if (fam == "GPIO") return pinrole(b, reg, field)
  if (inst == "RCC") {
    if (motorlab[b] != "-" && field ~ /^(TIM[0-9]+|DMA[0-9]+|DMAMUX[0-9]*|BDMA)(EN|RST|LPEN)$/) {
      t = field; sub(/(EN|RST|LPEN)$/, "", t)
      if (t ~ /^TIM/ ? ((b, t) in mtim) : 1) return 1
    }
    return 2
  }
  if (inst == "FLASH") return (reg ~ /ACR$/) ? 2 : 3
  if (inst == "PWR") return 3
  if (inst ~ /^(SPI|I2C)[0-9]+$/) return 4
  return 5
}
function crank(c, a) { return (c == "ignored-by-model") ? 0 : (c == "same-source") ? 1 : a ? 2 : 3 }
{
  b = $1; id = $2
  qual = ($10 == "ignored-by-model" || $10 == "same-source" || $11 == "1" || $21 == "yes")
  if (!qual) next
  k = klass(b, $3, $4, $5)
  if (!(id in cls) || k < cls[id]) cls[id] = k
  nbc[id, k]++; bk[id, k] = bk[id, k] (bk[id, k] ? "," : "") b; nall[id]++
  if (index("," lc[id] ",", "," $10 ",") == 0) lc[id] = lc[id] (lc[id] ? "," : "") $10
  if ($11 == "1") ai[id] = 1
  acc[id] = $8; val[id] = val[id] (val[id] ? " " : "") b "=" $9
  model[id] = $16 " (" $17 ")"
  if ($20 ~ /tag|undefined|no register|not in the register collection|no register collection/ || $21 == "yes") v2[id] = 1
  cover[id] = $20
  d = $21; if (index("|" drop[id] "|", "|" b ":" d "|") == 0) drop[id] = drop[id] (drop[id] ? "|" : "") b ":" d
  fw[id] += $18; fwb[id] = fwb[id] (fwb[id] ? " " : "") b "=" $18
  if ($18 == 0) unw[id] = 1
  if ($21 == "yes" && $10 != "ignored-by-model" && $10 != "same-source" && $11 != "1") judgeddrop[id] = 1
  el[id] = $13; src[id] = $15
  rk = crank($10, $11 == "1"); if (!(id in cr) || rk < cr[id]) cr[id] = rk
}
END {
  for (id in cls) {
    os = "-"
    if (unw[id]) os = (acc[id] == "reset") ? "R-reset" : (id ~ /^(DMA|DMAMUX|BDMA)/ ? "R-never-issued" : "R-window")
    if (judgeddrop[id]) os = "R-judged-class (consumer-side finding)"
    # Boards are counted within the row's class: a GPIO field that is a motor pin on one board and a
    # sensor-bus pin on twelve counts 1 in the motor class (the rest are in nall).
    nb = nbc[id, cls[id]]
    key = sprintf("%d %03d %d %d %d %s", cls[id], 999 - nb, v2[id] ? 0 : 1, acc[id] == "written" ? 0 : 1, cr[id], id)
    line[key] = cls[id] OFS CN[cls[id]] OFS nb "/" nall[id] OFS bk[id, cls[id]] OFS id OFS lc[id] OFS (ai[id] ? 1 : 0) OFS acc[id] OFS val[id] OFS model[id] OFS cover[id] OFS drop[id] OFS fwb[id] OFS os OFS el[id] OFS src[id]
  }
  n = asorti(line, ks)
  for (i = 1; i <= n; i++) print i, line[ks[i]]
}
