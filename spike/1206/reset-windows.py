"""TVI-WO-1206: every CPU access to a peripheral other than the RCC made while any RCC reset register (RCC_*RSTR) holds a 1, so
that a reset-bit reading's "unaffected" is measured: the twin resets a registered peripheral once, at the write of 1, and RM0433
says only "1: resets" (whether silicon holds the block in reset while the bit is 1 is not quoted, so no reading relies on it).

Usage: zcat CONSOLE.gz | python3 -I spike/1206/reset-windows.py RUN   (one line per (instance, access, reset registers held))"""
import collections, re, sys
run = sys.argv[1]
w = re.compile(r"\] ([A-Za-z0-9_]+): \[cpu: [^]]*\] (Read|Write)[A-Za-z0-9]* (?:from|to) (0x[0-9A-Fa-f]+) \(([^)]*)\)(?:, (?:value|returned) (0x[0-9A-Fa-f]+))?")
held, hits, writes = {}, collections.Counter(), 0
for l in sys.stdin.buffer:
    m = w.search(l.decode('latin-1'))
    if not m: continue
    inst, rw, off, name, val = m.groups()
    if inst == 'rcc' and re.search(r'Reset(Low|High)?$', name) and rw == 'Write':  # AHBxReset, APBxReset, APB1ResetLow/High
        writes += 1
        if int(val, 16): held[name] = int(val, 16)
        else: held.pop(name, None)
    elif held and inst != 'rcc':
        hits[(inst, rw, ','.join(sorted(held)))] += 1
print('%s\treset-register writes %d' % (run, writes))
for (inst, rw, regs), n in sorted(hits.items()): print('%s\t%s\t%s\twhile %s\t%d' % (run, inst, rw, regs, n))
