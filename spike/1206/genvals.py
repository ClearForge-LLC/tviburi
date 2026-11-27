"""TVI-WO-1206: the generated boots' and the fixture's values per field, shared by items.py and items2.py (one home)."""
import re


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
