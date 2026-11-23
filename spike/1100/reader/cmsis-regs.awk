# TVI-WO-1100 scratch reader: register offsets and instance bases from ST's CMSIS device header
# (the copy Aethelgard pins: crates/aethelgard-svd-hal/data/st-cmsis-device-h7/stm32h753xx.h).
# Output, tab-separated: INSTANCE  REGISTER  ADDRESS(hex)  TYPE  OFFSET(hex)
# An array member X[n] at "Address offset: 0xA-0xB" is emitted as Xk at A + 4k (GPIO AFR[2] -> AFR0, AFR1).
function hex(s) { return strtonum(s) }
function base(name,   e, n, parts, i, tot, t) {
  if (name in resolved) return resolved[name]
  e = def[name]; gsub(/[()]|UL/, "", e); n = split(e, parts, "+"); tot = 0
  for (i = 1; i <= n; i++) { t = parts[i]; gsub(/ /, "", t); tot += (t ~ /^0x/ ? hex(t) : base(t)) }
  resolved[name] = tot; return tot
}
/^typedef struct/ { inside = 1; delete cur; ncur = 0; next }
inside && /^} *[A-Za-z0-9_]+_TypeDef;/ {
  ty = $0; sub(/^} */, "", ty); sub(/;.*/, "", ty)
  for (i = 1; i <= ncur; i++) regs[ty] = regs[ty] cur[i] "\n"
  inside = 0; next
}
inside && /Address offset: *0x/ {
  line = $0; decl = line; sub(/\/\*.*/, "", decl); gsub(/;/, "", decl); sub(/[ \t]+$/, "", decl)
  m = split(decl, w, /[ \t]+/); name = w[m]
  off = line; sub(/.*Address offset: */, "", off); sub(/[^0-9A-Fa-fx].*/, "", off)
  if (name ~ /\[/) {
    cnt = name; sub(/.*\[/, "", cnt); sub(/\].*/, "", cnt); sub(/\[.*/, "", name)
    if (name ~ /^RESERVED/) next
    for (k = 0; k < cnt + 0; k++) cur[++ncur] = name k "\t" (hex(off) + 4 * k)
  } else if (name !~ /^RESERVED/) cur[++ncur] = name "\t" hex(off)
  next
}
/^#define [A-Za-z0-9_]+_BASE[ \t]+[(A-Z]/ { n = $2; v = $0; sub(/^#define [A-Za-z0-9_]+[ \t]+/, "", v); sub(/\/\*.*/, "", v); def[n] = v; next }
/^#define [A-Za-z0-9_]+[ \t]+\(\([A-Za-z0-9_]+_TypeDef \*\) *[A-Za-z0-9_]+_BASE\)/ {
  inst = $2; t = $0; sub(/.*\(\(/, "", t); sub(/ \*.*/, "", t); b = $0; sub(/.*\*\) */, "", b); sub(/\).*/, "", b)
  insts[inst] = t "\t" b
}
END {
  for (inst in insts) {
    split(insts[inst], p, "\t"); ty = p[1]; bn = p[2]; if (!(bn in def)) continue
    bb = base(bn); n = split(regs[ty], rl, "\n")
    for (i = 1; i < n; i++) { split(rl[i], f, "\t"); printf "%s\t%s\t0x%08X\t%s\t0x%X\n", inst, f[1], bb + f[2], ty, f[2] }
  }
}
