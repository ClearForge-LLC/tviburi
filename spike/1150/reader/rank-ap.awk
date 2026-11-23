# TVI-WO-1150 (v) scratch reader: 1100's queue joined with ArduPilot's access log, re-ranked by RUL-13's amended order.
# Usage: gawk -F'\t' -f rank-ap.awk cmsis-regs.tsv joined-ap.tsv only2-ap.tsv spike/1100/data/queue.tsv > queue-ap.tsv
#   cmsis-regs.tsv: spike/1100/reader/cmsis-regs.awk on Aethelgard's pinned stm32h753xx.h (INSTANCE REGISTER ADDRESS ...)
#   joined-ap.tsv : spike/1100/reader/view2-join.awk, board "ardupilot", over the union of 1100's ledger field ids
#   only2-ap.tsv  : that join's out2: ArduPilot writes the stock model drops that no board's ledger row covers
# The rule, written before it was run (results.md (v)):
#   tier 1  RCC enables (*ENR, *LPENR) and kernel-clock selectors (*CCIPR, *CCIP1R, *CCIP2R)
#   tier 2  GPIO alternate-function routing (AFRL, AFRH, MODER)
#   tier 3  timers and DMA (TIMn, DMAn, DMAMUXn, BDMA)
#   tier 4  the rest: 4a other clock-tree registers (other RCC, FLASH.ACR), 4b everything else
#   within a tier: (a) ArduPilot writes it and the stock model drops what it wrote; (b) ArduPilot writes it;
#   (c) ArduPilot does not write it; then 1100's rank; ArduPilot-only rows after 1100's rows of the same (a)/(b).
# Output: new_rank tier ap_class ap_takes ap_writes_at_register ap_field_value ap_drop old_rank id old_class
#         model_cover ap_first_pc origin
BEGIN { OFS = "\t" }
function tier(id,   inst, reg) {
  inst = id; sub(/\..*/, "", inst); reg = id; sub(/^[^.]*\./, "", reg); sub(/\..*/, "", reg)
  if (inst == "RCC" && (reg ~ /(LP)?ENR$/ || reg ~ /CCIP[12]?R$/)) return "1"
  if (inst ~ /^GPIO/ && reg ~ /^(AFRL|AFRH|MODER)$/) return "2"
  if (inst ~ /^(TIM[0-9]+|DMA[0-9]+|DMAMUX[0-9]*|BDMA)$/) return "3"
  if (inst == "RCC" || (inst == "FLASH" && reg == "ACR")) return "4a"
  return "4b"
}
function apclass(drop, n) { return (drop == "yes") ? "a" : (n + 0 > 0) ? "b" : "c" }
FILENAME == ARGV[1] { name[tolower($3)] = $1 "." $2; next }
FILENAME == ARGV[2] { j[$2] = $0; jn[$2] = $18; jv[$2] = $19; jc[$2] = $20; jd[$2] = $21; next }
FILENAME == ARGV[3] {
  a = tolower($4); r = (a in name) ? name[a] : $2 "@" $5
  f = $6; sub(/^tag /, "", f); sub(/ \[.*$/, "", f); if (f ~ /^undefined bits/) f = $6
  id = r "." f
  if (id in only) { next }
  only[id] = 1; oc[id] = $6 " (" $10 ")"; ov[id] = $7; on[id] = $8; op[id] = $9
  next
}
{
  id = $6; seen[id] = 1
  n = (id in jn) ? jn[id] : 0; d = (id in jd) ? jd[id] : "-"; ap = apclass(d, n)
  key = sprintf("%s|%s|0|%06d", tier(id), ap, $1)
  line[key] = tier(id) OFS ap OFS ((n + 0 > 0) ? "yes" : "no") OFS n OFS ((id in jv) ? jv[id] : "-") OFS d OFS $1 OFS id OFS $3 OFS $12 OFS "-" OFS "1100 queue"
}
END {
  for (id in jd) if (!(id in seen) && jd[id] == "yes") {
    key = sprintf("%s|a|1|%s", tier(id), id)
    line[key] = tier(id) OFS "a" OFS "yes" OFS jn[id] OFS jv[id] OFS "yes" OFS "-" OFS id OFS "-" OFS jc[id] OFS "-" OFS "ArduPilot only: a ledger field on some board, not in 1100's queue"
  }
  for (id in only) {
    key = sprintf("%s|a|1|%s", tier(id), id)
    line[key] = tier(id) OFS "a" OFS "yes" OFS on[id] OFS ov[id] OFS "yes" OFS "-" OFS id OFS "-" OFS oc[id] OFS op[id] OFS "ArduPilot only: no board's ledger has the field (named from Renode's map)"
  }
  nk = asorti(line, ks); for (i = 1; i <= nk; i++) print i, line[ks[i]]
}
