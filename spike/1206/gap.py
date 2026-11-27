"""TVI-WO-1206 adversarial pass: the fields 1205's join never counted. 1100a/1205 placed queue rows (each covering its own
bits) and armed registers with no row at all, so a field the armed run writes at a register that has some other row is in
neither: it is reached, and judged nowhere. This lists every such field whose ledger state on main is inherited-unaudited,
with its register's flight path (from that register's own armed rows).

Usage: python3 -I spike/1206/gap.py JOIN_DATA LEDGER > spike/1206/data/main-8e15435/gap.tsv
  JOIN_DATA  1205's join outputs on main (states.tsv, arm-reach-outside.tsv, arm-writes.tsv); LEDGER main's LEDGER.jsonl"""
import collections, json, sys

M, L = sys.argv[1].rstrip('/') + '/', sys.argv[2]
def rd(f):
    ls = open(f).read().splitlines(); h = ls[0].lstrip('# ').split('\t')
    return [dict(zip(h, l.split('\t'))) for l in ls[1:] if not l.startswith('#')]
states, reach, writes = rd(M + 'states.tsv'), rd(M + 'arm-reach-outside.tsv'), rd(M + 'arm-writes.tsv')
fields = collections.defaultdict(list)
for l in open(L).read().splitlines()[1:]:
    r = json.loads(l)
    if r.get('row') == 'field': fields[(r['peripheral'].replace('Tviburi.', ''), int(r['offset'], 16))].append(r)
covered, regpath = set(), collections.defaultdict(set)
for r in states:
    if 'arm' in r['reach']: regpath[(r['instance'], int(r['offset'], 16))].add(r['path'].split(' (')[0])
for r in states + reach:
    covered.update((r['instance'], f.split(' ')[0]) for f in r['ledger_fields'].split('; ') if ' ' in f)
print('# instance\toffset\tregister.field\tbits\twritten\tregister_paths (its armed rows)')
for w in writes:
    off, inst = int(w['offset'], 16), w['instance']
    for f in fields.get((w['type'], off), []):
        name = '%s.%s' % (f['register'], f['field'])
        if f['state'] != 'inherited-unaudited' or (inst, name) in covered: continue
        hi, lo = int(f['bits'].split(':')[0]), int(f['bits'].split(':')[-1]); m = ((1 << (hi + 1)) - 1) & ~((1 << lo) - 1)
        print('%s\t0x%03X\t%s\t%s\t%s\t%s' % (inst, off, name, f['bits'], 'ever-1' if int(w['or'], 16) & m else 'only-0',
                                          ','.join(sorted(regpath.get((inst, off), {'(no arm row)'})))))
