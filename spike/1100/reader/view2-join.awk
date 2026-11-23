# TVI-WO-1100 scratch reader, view (2), step 2, and the join for one board.
# Usage: gawk -F'\t' -v board=B -v out2=ONLY2.tsv -f view2-join.awk REGMAP.tsv WRITES.tsv VIEW1.tsv > JOINED.tsv
#   REGMAP: the TVIBASE/TVIMAP lines regmap.py printed after the run (the executed objects' map)
#   WRITES: view2-writes.awk output for the logged replay (only `W ... fw` rows are used)
#   VIEW1 : view1.awk output (rows of this board only are used)
# Per ledger row it appends: model peripheral, model class, firmware write count at the register, the
# field's last written value, what covers the field in the model (tag | field/mode | undefined bits | no
# register | no register collection), and `dropped`: yes when the model cannot hold what was written
# on any write, not only the last (a tag holds its reset value; undefined bits and an absent register hold
# nothing), no when it stores it, `-` when the firmware never wrote the register.
# out2 receives firmware writes the model drops (non-reset value into a tag, a non-zero value into undefined
# bits, any write to an absent register) that no ledger row of this board covers: the view-(2)-only rows.
function hx(s) { return strtonum(s) }
function mask(pos, w) { return (w >= 32) ? 0xFFFFFFFF : lshift(lshift(1, w) - 1, pos) }
function periph(a,   n, best, bs) { best = ""; for (n in base) if (a >= base[n] && a < base[n] + size[n] && (best == "" || size[n] < bs)) { best = n; bs = size[n] }; return best }
function writable(m) { return m ~ /Write|Set|Toggle|Clear/ }
BEGIN { OFS = "\t" }
FILENAME == ARGV[1] { sub(/^sysbus\./, "", $2) }
FILENAME == ARGV[1] && $1 == "TVIBASE" { if ($4 != "?") { base[$2] = hx($4); size[$2] = hx($5); cls[$2] = $3 }; next }
FILENAME == ARGV[1] && $1 == "TVIMAP" {
  if ($4 == "none") { nomap[$2] = 1; next }
  hasmap[$2] = 1; k = $2 SUBSEP hx($4); regs[k] = 1
  e = ++ne[k]; ek[k, e] = $6; en[k, e] = $7; ep[k, e] = $8 + 0; ew[k, e] = $9 + 0; em[k, e] = $10
  defined[k] = or(defined[k], mask($8 + 0, $9 + 0)); next
}
FILENAME == ARGV[1] { next }
FILENAME == ARGV[2] && $1 == "U" { if (match($6, /offset (0x[0-9A-Fa-f]+)/, wo)) warned[$4 SUBSEP hx(wo[1])] = 1; next }
FILENAME == ARGV[2] {
  if ($1 != "W" || $4 != "fw") next
  off = hx($7); sh = 8 * and(off, 3); off = off - and(off, 3)
  wm = ($6 == "Byte") ? 0xFF : ($6 == "Word" || $6 == "UInt16") ? 0xFFFF : 0xFFFFFFFF
  k = $5 SUBSEP off; m = lshift(wm, sh); v = lshift(hx($8), sh)
  last[k] = or(and(last[k], compl(m) % 4294967296), and(v, m)); wmask[k] = or(wmask[k], m); cnt[k]++
  # Every write counts, not only the last: a tag set by one write and cleared by a later one (ADC_CR.ADCAL)
  # was still dropped. orv: bits any write set; andv: bits every write kept set.
  if (!(k in andv)) andv[k] = 0xFFFFFFFF
  orv[k] = or(orv[k], and(v, m)); andv[k] = and(andv[k], or(and(v, m), compl(m) % 4294967296))
  if (!(k in fpc)) fpc[k] = $9; lpc[k] = $9; fseq[k] = (k in fseq) ? fseq[k] : $3
  wk[k] = 1; next
}
$1 != board { next }
{
  a = ($12 == "-") ? -1 : hx($12); p = (a < 0) ? "" : periph(a)
  pos = $6 + 0; w = $7 + 0; fm = mask(pos, w)
  if (p == "") { print $0, "-", "-", 0, "-", "no peripheral at the address", "-"; next }
  off = a - base[p]; k = p SUBSEP off
  n = (k in cnt) ? cnt[k] : 0
  fv = (n > 0 && and(wmask[k], fm) == fm) ? sprintf("0x%X", rshift(and(last[k], fm), pos)) : "-"
  if (nomap[p] && !hasmap[p]) { cover = "no register collection (" cls[p] "): classify by source"; drop = (n ? "by-source" : "-") }
  else if (!(k in regs) && !(k in warned)) { cover = "not in the register collection at " sprintf("0x%X", off) ", and no unhandled-write warning: handled in the model's own switch (read the source)"; drop = (n ? "by-source" : "-") }
  else if (!(k in regs)) { cover = "no register at offset " sprintf("0x%X", off) " (unimplemented; the model warned)"; drop = (n ? "yes" : "-") }
  else {
    cover = ""; drop = (n ? "no" : "-")
    for (e = 1; e <= ne[k]; e++) {
      em2 = mask(ep[k, e], ew[k, e]); if (!and(em2, fm)) continue
      if (ek[k, e] == "tag") {
        tr = em[k, e]; sub(/.*reset=/, "", tr)
        cover = cover (cover ? "; " : "") "tag " en[k, e] " [" ep[k, e] "+" ew[k, e] "] reset " tr
        ov = and(em2, fm)   # only this row's bits of the tag
        rb = and(lshift(hx(tr), ep[k, e]), ov)   # the tag reset bits within this row
        if (n && fv != "-" && (and(orv[k], and(ov, compl(rb) % 4294967296)) || and(and(compl(andv[k]) % 4294967296, ov), rb))) drop = "yes"
        else if (n && drop != "yes") drop = "no (tag; written value equals its reset)"
      } else {
        cover = cover (cover ? "; " : "") "field " en[k, e] " [" ep[k, e] "+" ew[k, e] "] " em[k, e]
        if (n && !writable(em[k, e]) && drop == "no") drop = "no (a read-only field in the model: the write is ignored, as for a status bit on silicon; check RM0433's access)"
      }
    }
    und = and(fm, compl(defined[k]) % 4294967296)
    if (und) { cover = cover (cover ? "; " : "") sprintf("undefined bits 0x%X", und); if (n && and(orv[k], und)) drop = "yes"; else if (n && drop == "no") drop = "no (undefined bits written 0)" }
  }
  print $0, p, cls[p], n, fv, cover, drop
  covered[k] = or(covered[k], fm)
}
END {
  for (k in wk) {
    split(k, kk, SUBSEP); p = kk[1]; off = kk[2] + 0; unc = compl(covered[k]) % 4294967296
    if (cls[p] ~ /\.Memory\./) continue   # RAM (the twin's boot record), not a register
    if (nomap[p] && !hasmap[p]) { if (and(last[k], unc)) print board, p, cls[p], sprintf("0x%X", base[p] + off), sprintf("0x%X", off), "-", sprintf("0x%X", last[k]), cnt[k], fpc[k], "no register collection: by source" > out2; continue }
    if (!(k in regs) && !(k in warned)) continue   # handled by a switch: by source
    if (!(k in regs)) { if (and(last[k], unc)) print board, p, cls[p], sprintf("0x%X", base[p] + off), sprintf("0x%X", off), "absent register", sprintf("0x%X", last[k]), cnt[k], fpc[k], "write to an unimplemented register" > out2; continue }
    for (e = 1; e <= ne[k]; e++) {
      if (ek[k, e] != "tag") continue
      em2 = mask(ep[k, e], ew[k, e]); if (!and(em2, unc)) continue
      tr = em[k, e]; sub(/.*reset=/, "", tr); tv = and(rshift(last[k], ep[k, e]), mask(0, ew[k, e]))
      ob = and(orv[k], em2); ab = and(compl(andv[k]) % 4294967296, em2); rb = lshift(hx(tr), ep[k, e])
      if (and(ob, compl(rb) % 4294967296) || and(ab, rb)) print board, p, cls[p], sprintf("0x%X", base[p] + off), sprintf("0x%X", off), "tag " en[k, e] " [" ep[k, e] "+" ew[k, e] "]", sprintf("0x%X", tv), cnt[k], fpc[k], "tag holds reset " tr > out2
    }
    und = and(unc, compl(defined[k]) % 4294967296)
    if (and(orv[k], und)) print board, p, cls[p], sprintf("0x%X", base[p] + off), sprintf("0x%X", off), sprintf("undefined bits 0x%X", und), sprintf("0x%X", and(orv[k], und)), cnt[k], fpc[k], "bits with no field or tag" > out2
  }
}
