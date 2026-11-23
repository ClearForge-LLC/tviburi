#!/usr/bin/env python3
"""TVI-WO-1300 §1.1 (tests/1300, measurement analysis; host Python): the rm0433.md field table from allow/given.tsv and allow/rows.tsv.
One row per (register, field) any source gives a value. Sources are grouped: ArduPilot's firmware, the consumer's cold
writes (pc none, both firmwares), and the generated firmware (each value with the boards that write it)."""
import collections, os, re

H = os.environ.get('TVI1300_WORK', os.path.expanduser('~/.cache/tvi-1300')) + '/allow/'
rows = {}
for l in open(H + 'rows.tsv'):
    r = l.rstrip('\n').split('\t')
    if r[0] == 'register':
        continue
    rows[(r[0], r[1], r[2], r[3])] = r
SHORT = {'d1': 'd1-drv8876', 'djn': 'driver-judges-nothing', 'hc16': 'hse-crystal-16', 'he8': 'hse-external-8'}
out = ['| Register | Offset | Field | Bits | Page | Class | Allowed | ArduPilot | Cold writes | Generated firmware |',
       '|---|---|---|---|---|---|---|---|---|---|']
for l in open(H + 'given.tsv'):
    r = l.rstrip('\n').split('\t')
    if r[0] == 'register':
        continue
    reg, off, field, bits, cls, allowed, vals = r
    row = rows.get((reg.replace('(C1)', ''), off, field, bits))
    page = row[6] if row else '?'
    ap, cold, gen = set(), set(), collections.defaultdict(set)
    for part in vals.split('; '):
        src, vs = part.split(': ', 1)
        values = [re.sub(r'x\d+$', '', v) for v in vs.split(',')]
        for v in values:
            if src.endswith('/cold'):
                cold.add(v)
            elif src == 'ap':
                ap.add(v)
            else:
                b = src.split(':', 1)[1] if src.startswith('p1100:') else SHORT.get(src, src)
                gen[v].add(b + ('*' if src.startswith('p1100:') else ''))
    fmt = lambda s: ', '.join('`' + v + '`' for v in sorted(s, key=lambda x: int(x, 16))) or '—'
    g = '; '.join('`' + v + '` (' + ('all' if len(bs) >= 13 else ', '.join(sorted(bs))) + ')' for v, bs in sorted(gen.items(), key=lambda kv: int(kv[0], 16))) or '—'
    flag = ' **outside**' if cls not in ('-',) and allowed != 'any' and any(v not in allowed.split(',') for v in ap | cold | set(gen)) else ''
    out.append(f'| `{reg}` | `{off}` | `{field}` | {bits} | {page} | {cls}{flag} | {allowed} | {fmt(ap)} | {fmt(cold)} | {g} |')
print('\n'.join(out))
