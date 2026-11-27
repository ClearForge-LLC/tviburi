"""TVI-WO-1206: the proof that nothing behavioural moved (architect, 2026-11-27T00:10Z): main's release ledger against the branch's.
Only `state`, `behaves_as`, `detail` and `reason` may differ, and only on the work list's fields; every other row, and every
other key of a changed row (bits, allowed, offset, citation, ...), is identical.

Usage: python3 -I spike/1206/ledger-diff.py MAIN_LEDGER BRANCH_LEDGER spike/1206/data/items.tsv
Each LEDGER.jsonl's first line names its manifest and is skipped. Prints the counts; exit 1 on any other difference."""
import collections, json, sys

main, branch, items = sys.argv[1:4]
work = {l.split('\t')[0] for l in open(items) if not l.startswith('#')}
TEXT = {'state', 'behaves_as', 'detail', 'reason'}


def rows(path):
    out = {}
    for l in open(path).read().splitlines()[1:]:
        r = json.loads(l)
        k = (r.get('row'), r.get('peripheral'), r.get('register'), r.get('field'), r.get('offset'), r.get('instance'), r.get('name'))
        assert k not in out, k
        out[k] = r
    return out


a, b = rows(main), rows(branch)
bad, changed, keys = [], [], collections.Counter()
if set(a) != set(b):
    bad.append('row sets differ: %d only on main, %d only on the branch' % (len(set(a) - set(b)), len(set(b) - set(a))))
for k in sorted(set(a) & set(b), key=str):
    if a[k] == b[k]: continue
    diff = {x for x in set(a[k]) | set(b[k]) if a[k].get(x) != b[k].get(x)}
    name = '%s.%s' % (k[2], k[3])
    keys.update(diff)
    if k[0] != 'field' or name not in work or not diff <= TEXT:
        bad.append('%s %s: %s' % (k[1], name, sorted(diff)))
    else:
        changed.append((name, a[k].get('state'), b[k].get('state')))
moves = collections.Counter((x, y) for _, x, y in changed)
print('%d rows each side; %d changed, all work-list fields, keys changed: %s' % (len(a), len(changed), dict(keys)))
print('state moves: %s' % dict(moves))
print('work-list fields not changed: %s' % (sorted(work - {n for n, _, _ in changed}) or 'none'))
for x in bad: print('BAD ' + x)
sys.exit(1 if bad or work - {n for n, _, _ in changed} else 0)
