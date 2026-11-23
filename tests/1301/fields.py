#!/usr/bin/env python3
"""TVI-WO-1301 §1.1: the GPIO field table from the prototype's traces (`TVI1301-T` lines, tests/1301/replay-1301.sh
trace mode). Usage: fields.py TABLE.tsv NAME=CONSOLE.gz ... ; writes the table and the order analysis to stdout.
Each trace line: seq, port, R/W + width, offset, value, pc, and MODER/AFRL/AFRH after the access."""
import csv, gzip, re, sys
from collections import defaultdict

REG = {0x00: ('GPIOx_MODER', 541), 0x04: ('GPIOx_OTYPER', 541), 0x08: ('GPIOx_OSPEEDR', 542), 0x0C: ('GPIOx_PUPDR', 542),
       0x10: ('GPIOx_IDR', 543), 0x14: ('GPIOx_ODR', 543), 0x18: ('GPIOx_BSRR', 544), 0x1C: ('GPIOx_LCKR', 544),
       0x20: ('GPIOx_AFRL', 545), 0x24: ('GPIOx_AFRH', 546)}
def fields(off):
    if off in (0x00, 0x08, 0x0C): return [(f'{REG[off][0][6:]}{i}', 2 * i, 2) for i in range(16)]
    if off in (0x04, 0x10, 0x14): return [(f'{REG[off][0][6:]}{i}', i, 1) for i in range(16)] + [('Res31_16', 16, 16)]
    if off == 0x18: return [(f'BS{i}', i, 1) for i in range(16)] + [(f'BR{i}', 16 + i, 1) for i in range(16)]
    if off == 0x1C: return [(f'LCK{i}', i, 1) for i in range(16)] + [('LCKK', 16, 1), ('Res31_17', 17, 15)]
    if off == 0x20: return [(f'AFSEL{i}', 4 * i, 4) for i in range(8)]
    if off == 0x24: return [(f'AFSEL{8 + i}', 4 * i, 4) for i in range(8)]
    return [('?', 0, 32)]

table = {}
for r in csv.DictReader(open(sys.argv[1], encoding='utf-8'), delimiter='\t'):
    table[(r['pin'], int(r['af']))] = (r['cell'], r['page'])

LINE = re.compile(r'TVI1301-T \d+ P([A-K]) ([RW])(\d+) off=0x([0-9A-F]+) val=0x([0-9A-F]+) pc=(\S+) moder=0x([0-9A-F]+) afrl=0x([0-9A-F]+) afrh=0x([0-9A-F]+)')
vals = defaultdict(lambda: defaultdict(set))     # (reg, field) -> source -> values
widths = defaultdict(lambda: defaultdict(int))   # (reg, op, width) -> source -> count
cold = defaultdict(list)
rulefires = defaultdict(list)                     # independent of the prototype: AF mode on a blank cell, after any write
order = []                                        # pins put in AF mode: (source, pin, AF at the MODER write, AF final, first-AF-mode seq)
final_af = {}
for arg in sys.argv[2:]:
    src, path = arg.split('=', 1)
    afmode = {}; moder_prev = {}; last = {}
    for line in gzip.open(path, 'rt', errors='replace'):
        m = LINE.search(line)
        if not m: continue
        port, op, w, off, val, pc, moder, afrl, afrh = m.groups()
        w, off, val, moder, afrl, afrh = int(w), int(off, 16), int(val, 16), int(moder, 16), int(afrl, 16), int(afrh, 16)
        aligned = off & ~3
        widths[(REG.get(aligned, ('?', 0))[0], op, w, off & 3)][src] += 1
        if op == 'R': continue
        if pc == 'none': cold[src].append((port, off, val))
        word = val << (8 * (off & 3))
        mask = ((1 << w) - 1) << (8 * (off & 3))
        for name, lsb, n in fields(aligned):
            fm = ((1 << n) - 1) << lsb
            if fm & mask: vals[(REG.get(aligned, ('?', 0))[0], name)][src].add((word & fm) >> lsb)
        prev = moder_prev.get(port, None)
        for pin in range(16):
            mode = (moder >> 2 * pin) & 3
            af = ((afrl if pin < 8 else afrh) >> 4 * (pin & 7)) & 0xF
            key = f'P{port}{pin}'
            cell = table.get((key, af))
            if mode == 2 and cell is not None and cell[0] == '-' and last.get(key) != (mode, af):
                rulefires[src].append((key, af, cell[1], REG.get(aligned, ('?', 0))[0]))
            if mode == 2 and afmode.get(key) is None:
                afmode[key] = (aligned, af)
            if mode != 2: afmode.pop(key, None)
            last[key] = (mode, af)
        moder_prev[port] = moder
    for key, (mode, af) in last.items():
        if mode == 2:
            first = afmode.get(key)
            order.append((src, key, first, af))
print('## The AF rule, evaluated here at every write (AF mode on a blank cell): ' + (', '.join(f'{s}: {v}' for s, v in rulefires.items()) or 'never, on any source'))
print('## Access widths (register, op, width, byte offset: count per source)')
for k in sorted(widths): print('\t'.join(map(str, k)), dict(widths[k]))
print('## Cold writes (pc none)')
for s, l in cold.items(): print(s, l)
print('## Field values written (register, field: source -> values)')
for k in sorted(vals, key=lambda k: (k[0], k[1])):
    print(k[0], k[1], {s: sorted(hex(v) for v in vs) for s, vs in sorted(vals[k].items())}, sep='\t')
print('## Pins in AF mode at the end: source, pin, (register written when AF mode began, AF then), AF now, the table\'s cell')
for src, key, first, af in order:
    cell = table.get((key, af))
    print(src, key, first and (REG[first[0]][0], first[1]), af, cell, 'BLANK' if cell and cell[0] == '-' else '', sep='\t')
