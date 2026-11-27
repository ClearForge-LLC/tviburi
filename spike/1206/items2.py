"""TVI-WO-1206, widened (architect, 2026-11-27T00:43Z): the second work list. Every field the per-field measure
(spike/1206/reader/fields.py) finds open above the cut, with its model row and every value written, by the armed run (its
register values cut to the field's bits) and by the generated boards and the fixture (spike/1206/data/main-8e15435/gen-writes.tsv).

Usage: python3 -I spike/1206/items2.py COVERAGE ARM_WRITES MODEL_DIR GEN_WRITES OUT
  COVERAGE    fields.py's output for the tree (spike/1206/data/branch/coverage-first.tsv)
  ARM_WRITES  the join's arm-writes.tsv for the same tree
Columns as items.tsv: field, model, bits, allowed, page, paths, arm_values, gen_values, items (instance@offset), text."""
import glob, os, re, sys

cov, armw, model_dir, writes, out = sys.argv[1:6]
ROW = re.compile(r'\bR\("([A-Za-z0-9_]+)",\s*(0x[0-9A-Fa-f]+),\s*"([^"]*)",\s*"([^"]+)",\s*(\d+),\s*(\d+),\s*(new uint\[\] \{[^}]*\}|[^,]+),\s*'
                 r'("inherited, unaudited \(acknowledged by name\)"|Inherited),\s*"((?:[^"\\]|\\.)*)",\s*(\d+)\)')
model = {}
for path in sorted(glob.glob(os.path.join(model_dir, '*.cs'))):
    for n, line in enumerate(open(path), 1):
        m = ROW.search(line)
        if m:
            model.setdefault('%s.%s' % (m.group(1), m.group(4)), []).append(dict(
                src='model/%s:%d' % (os.path.basename(path), n), hi=int(m.group(5)), lo=int(m.group(6)), allowed=m.group(7).strip(),
                text=m.group(9), page=m.group(10)))
def rd(f):
    ls = open(f).read().splitlines(); h = ls[0].lstrip('# ').split('\t')
    return [dict(zip(h, l.split('\t'))) for l in ls[1:] if not l.startswith('#')]
arm = {(r['instance'], int(r['offset'], 16)): r['values'] for r in rd(armw)}
def gen_values(path):
    """(instance, offset) -> [values] and [many-tokens] from spike/1206/data/main-8e15435/gen-writes.tsv (every board's writes)."""
    out = {}
    for l in open(path):
        if l.startswith('#'): continue
        b, inst, off, name, n, vals = l.rstrip('\n').split('\t')
        e = out.setdefault((inst, int(off, 16)), ([], []))
        if vals.startswith('many'):
            m = re.match(r'many:(\d+) or=(0x[0-9A-F]+) and=(0x[0-9A-F]+)', vals); e[1].append((b, int(m.group(1)), int(m.group(2), 16)))
        else:
            e[0].extend(int(x, 16) for x in vals.split(','))
    return out


def field_gen(fw, at, lo, width):
    """A field's values from gen_values: each value cut to its bits; a `many` register gives 0 if the field's bits are never 1,
    else `<board> many:N (bits ever 1: 0x..)`."""
    mask = (1 << width) - 1; vals, many = set(), set()
    for k in at:
        vs, ms = fw.get(k, ([], []))
        vals.update(hex((v >> lo) & mask) for v in vs)
        for b, n, orv in ms:
            if (orv >> lo) & mask == 0: vals.add('0x0')
            else: many.add('%s %s many:%d (bits ever 1: 0x%X)' % (b, k[0], n, (orv >> lo) & mask))
    return sorted(vals, key=lambda x: int(x, 16)) + sorted(many)

fw = gen_values(writes)
need = {}
for l in open(cov):
    if l.startswith('#'): continue
    inst, ty, off, reg, field, state, outcome, paths = l.rstrip('\n').split('\t')
    if outcome != 'open' or not any(p not in ('other', '(no path)') for p in paths.split(',')): continue
    e = need.setdefault(field, dict(at=set(), paths=set()))
    e['at'].add((inst, int(off, 16))); e['paths'].update(p for p in paths.split(',') if p != 'other')
bad = 0
with open(out, 'w') as o:
    o.write('# field\tmodel\tbits\tallowed\tpage\tpaths\tarm_values\tgen_values\titems\ttext\n')
    for f in sorted(need):
        e, rows = need[f], model.get(f, [])
        if len(rows) != 1:
            print('NO UNIQUE MODEL ROW for %s: %d' % (f, len(rows)), file=sys.stderr); bad += 1; continue
        m = rows[0]; w = m['hi'] - m['lo'] + 1; cut = lambda v: hex((v >> m['lo']) & ((1 << w) - 1))
        av, many = set(), set()
        for k in e['at']:
            for v in arm.get(k, '').split(','):
                if v.startswith('0x'): av.add(cut(int(v, 16)))
                elif v.startswith('many'): many.add('%s %s' % (k[0], v))
        gv = field_gen(fw, e['at'], m['lo'], w)
        key = lambda x: int(x, 16)
        o.write('\t'.join([f, m['src'], '%d:%d' % (m['hi'], m['lo']), m['allowed'], m['page'], ','.join(sorted(e['paths'])),
                           ','.join(sorted(av, key=key) + sorted(many)) or '-', ','.join(gv) or '-',
                           ' | '.join('%s@0x%03X' % k for k in sorted(e['at'])),
                           re.sub(r'\b(TVI-WO-\w+(?:\.[\w-]+)?)\.md\b', r'\1', m['text'])]) + '\n')
print('%d fields at %d instance-registers written to %s; %d without a unique model row' % (
    len(need) - bad, len({k for e in need.values() for k in e['at']}), out, bad))
sys.exit(1 if bad else 0)
