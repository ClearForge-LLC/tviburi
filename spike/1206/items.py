"""TVI-WO-1206: the work list. Every `inherited-unaudited` ledger field behind the 129 items above the cut, with its model row
and the values the armed run writes.

Usage: python3 -I spike/1206/items.py STATES REACH MODEL_DIR GEN_WRITES OUT

  STATES     1205's join rows (spike/1205/data/main-5d4a61a/states.tsv)
  REACH      1205's armed registers outside rows (…/arm-reach-outside.tsv)
  MODEL_DIR  model/, whose guards' field tables hold the rows: R(reg, off, section, field, hi, lo, allowed, class, text, page)
  GEN_WRITES every write of the 14 generated boots and the fixture, logged (spike/1206/data/main-8e15435/gen-writes.tsv): 1100's
             queue kept one value per board, and spike/1100/data/writes.tsv lacks some writes (both found by readers)
  OUT        one line per ledger field: field, model file:line, bits, allowed, page, the items (rows and registers) it is
             behind, and the values written in the armed run (a row's arm_field_values; a register's values, cut to the
             field's bits; a register the join lists only as many:N distinct values is carried as `<instance> many:N`), and every value the generated boards' firmware writes there

Above the cut, as 1205: a row whose reach names `arm`, and a register, each off path `other`."""
import glob, os, re, sys

states, reach, model_dir, writes, out = sys.argv[1:6]
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
UNAUD = 'inherited-unaudited'


def rd(f):
    lines = open(f).read().splitlines()
    head = lines[0].lstrip('# ').split('\t')
    return [dict(zip(head, l.split('\t'))) for l in lines[1:] if not l.startswith('#')]


def fields(r):
    return [f.split(' ') for f in r['ledger_fields'].split('; ') if ' ' in f]


ROW = re.compile(r'\bR\("([A-Za-z0-9_]+)",\s*(0x[0-9A-Fa-f]+),\s*"([^"]*)",\s*"([^"]+)",\s*(\d+),\s*(\d+),\s*(new uint\[\] \{[^}]*\}|[^,]+),\s*'
                 r'("inherited, unaudited \(acknowledged by name\)"|Inherited),\s*"((?:[^"\\]|\\.)*)",\s*(\d+)\)')
model = {}
for path in sorted(glob.glob(os.path.join(model_dir, '*.cs'))):
    for n, line in enumerate(open(path), 1):
        m = ROW.search(line)
        if m:
            reg, off, sec, fld, hi, lo, allowed, _, text, page = m.groups()
            key = '%s.%s' % (reg, fld)
            model.setdefault(key, []).append(dict(src='%s:%d' % (os.path.relpath(path, os.path.dirname(model_dir.rstrip('/'))), n),
                                                  hi=int(hi), lo=int(lo), allowed=allowed.strip(), text=text, page=page, sec=sec))

on = lambda r: not r['path'].startswith('other')
need = {}
for r in rd(states):
    if 'arm' in r['reach'] and on(r) and r['state'] == 'acknowledged':
        for name, cls, *_ in fields(r):
            if cls == UNAUD:
                e = need.setdefault(name, dict(items=[], vals=set(), paths=set()))
                e['items'].append('row ' + r['id'])
                e['paths'].add(r['path'])
                e['vals'].update(v for v in r['arm_field_values'].split(',') if v)
                e.setdefault('at', set()).add((r['instance'], int(r['offset'], 16)))
for r in rd(reach):
    if on(r) and r['state'] == 'acknowledged':
        for name, cls, *_ in fields(r):
            if cls == UNAUD:
                e = need.setdefault(name, dict(items=[], vals=set(), paths=set()))
                e['items'].append('reg %s.%s@%s' % (r['instance'], r['register'], r['offset']))
                e['paths'].add(r['path'].split(' (')[0])
                e.setdefault('at', set()).add((r['instance'], int(r['offset'], 16)))
                e.setdefault('regvals', set()).update(v for v in r['values'].split(',') if v.startswith('0x'))
                e.setdefault('many', set()).update('%s %s' % (r['instance'], v) for v in r['values'].split(',') if v.startswith('many'))

bad = 0
with open(out, 'w') as o:
    o.write('# field\tmodel\tbits\tallowed\tpage\tpaths\tarm_values\tgen_values\titems\ttext\n')
    for name in sorted(need):
        e = need[name]
        rows = model.get(name, [])
        if len(rows) != 1:
            print('NO UNIQUE MODEL ROW for %s: %d' % (name, len(rows)), file=sys.stderr); bad += 1; continue
        m = rows[0]
        vals = set(e['vals'])
        width = m['hi'] - m['lo'] + 1
        for v in e.get('regvals', ()):
            vals.add(hex((int(v, 16) >> m['lo']) & ((1 << width) - 1)))
        gen = field_gen(fw, e.get('at', ()), m['lo'], width)
        o.write('\t'.join([name, m['src'], '%d:%d' % (m['hi'], m['lo']), m['allowed'], m['page'], ','.join(sorted(e['paths'])),
                           ','.join(sorted(vals, key=lambda x: int(x, 16)) + sorted(e.get('many', ()))) or '-',
                           ','.join(gen) or '-', ' | '.join(e['items']),
                           # CI's inventory refuses a code file that names a document: a quoted row's work-order document loses its suffix here
                           re.sub(r'\b(TVI-WO-\w+(?:\.[\w-]+)?)\.md\b', r'\1', m['text'])]) + '\n')
print('%d fields, %d items (%d rows, %d registers) written to %s; %d without a unique model row' % (
    len(need) - bad, len({i for e in need.values() for i in e['items']}),
    len({i for e in need.values() for i in e['items'] if i.startswith('row')}),
    len({i for e in need.values() for i in e['items'] if i.startswith('reg')}), out, bad))
sys.exit(1 if bad else 0)
