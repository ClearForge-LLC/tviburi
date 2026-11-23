# TVI-WO-1100 scratch reader, view (2), step 1: every write in one replay's console (Renode's access log,
# `sysbus LogAllPeripheralsAccess true`), split by who issued it. A line carrying `[<cpu>: 0x<pc>]` is the
# firmware's (SystemBus.DecorateWithCPUNameAndPC adds it only when a CPU is executing); a line without it is
# the judge's (a monitor `sysbus` command). Also every model warning line.
# Usage: gawk -f view2-writes.awk BOARD replay-N-logged.log
# Output: W <board> <seq> <who: fw|judge> <peripheral> <width> <offset> <value> <pc> <register-name-as-model-maps-it>
#         U <board> <seq> <peripheral> <pc-or--> <text>   (an "Unhandled ..." / "non existing" / "overriding tag" warning)
BEGIN { OFS = "\t"; board = ARGV[1]; ARGV[1] = "" }
{
  if (match($0, /([A-Za-z0-9_.]+): (\[([^:\]]+): (0x[0-9A-Fa-f]+)\] )?Write(UInt32|UInt16|Byte|UInt64|DoubleWord|Word|QuadWord) to (0x[0-9A-Fa-f]+) \(([^)]*)\), value (0x[0-9A-Fa-f]+)/, m)) {
    seq++
    who = (m[2] != "") ? "fw" : "judge"
    print "W", board, seq, who, m[1], m[5], m[6], m[8], (m[4] != "" ? m[4] : "-"), m[7]
    next
  }
  if ($0 ~ /Unhandled write|Unhandled access|non existing peripheral|overriding tag|Invalid value written/) {
    seq++
    p = "-"; if (match($0, /([A-Za-z0-9_.]+): /, q)) p = q[1]
    pc = "-"; if (match($0, /\[[^:\]]+: (0x[0-9A-Fa-f]+)\]/, r)) pc = r[1]
    t = $0; gsub(/\t/, " ", t)
    print "U", board, seq, p, pc, t
  }
}
