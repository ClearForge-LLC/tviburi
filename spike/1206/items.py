"""TVI-WO-1206: the work list. Every `inherited-unaudited` ledger field behind the 129 items above the cut, with its model row
and the values the armed run writes.

Usage: python3 -I spike/1206/items.py STATES REACH MODEL_DIR QUEUE OUT

  STATES     1205's join rows (spike/1205/data/main-5d4a61a/states.tsv)
  REACH      1205's armed registers outside rows (…/arm-reach-outside.tsv)
  MODEL_DIR  model/, whose guards' field tables hold the rows: R(reg, off, section, field, hi, lo, allowed, class, text, page)
  QUEUE      1100's queue (spike/1100/data/queue.tsv): column 10 is each generated board's value for a row
  OUT        one line per ledger field: field, model file:line, bits, allowed, page, the items (rows and registers) it is
             behind, and the values written in the armed run (a row's arm_field_values; a register's values, cut to the
             field's bits), and the generated boards' values for its rows (1100's queue)

Above the cut, as 1205: a row whose reach names `arm`, and a register, each off path `other`."""
import glob, os, re, sys

states, reach, model_dir, queue, out = sys.argv[1:6]
gen = {}
for l in open(queue):
    c = l.rstrip('\n').split('\t')
    if len(c) > 9:
        gen[c[5]] = {v.split('=')[1] for v in c[9].split() if '=' in v}
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
                e.setdefault('gen', set()).update(gen.get(r['id'], ()))
for r in rd(reach):
    if on(r) and r['state'] == 'acknowledged':
        for name, cls, *_ in fields(r):
            if cls == UNAUD:
                e = need.setdefault(name, dict(items=[], vals=set(), paths=set()))
                e['items'].append('reg %s.%s@%s' % (r['instance'], r['register'], r['offset']))
                e['paths'].add(r['path'].split(' (')[0])
                e.setdefault('regvals', set()).update(v for v in r['values'].split(',') if v.startswith('0x'))

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
        o.write('\t'.join([name, m['src'], '%d:%d' % (m['hi'], m['lo']), m['allowed'], m['page'], ','.join(sorted(e['paths'])),
                           ','.join(sorted(vals, key=lambda x: int(x, 16))) or '-',
                           ','.join(sorted(e.get('gen', ()), key=lambda x: (not x.startswith('0x') and not x.isdigit(), int(x, 0) if x[:1].isdigit() else 0))) or '-', ' | '.join(e['items']), m['text']]) + '\n')
print('%d fields, %d items (%d rows, %d registers) written to %s; %d without a unique model row' % (
    len(need) - bad, len({i for e in need.values() for i in e['items']}),
    len({i for e in need.values() for i in e['items'] if i.startswith('row')}),
    len({i for e in need.values() for i in e['items'] if i.startswith('reg')}), out, bad))
sys.exit(1 if bad else 0)
