"""TVI-WO-1206 §1.2: a reading worker's rows are claims. This checks each quote against its source, mechanically:
- every `rm` quote is on its printed page of RM0433 Rev 9 (spike/1206/page.sh's convention), whitespace-insensitive;
- every `stock` quote is inside its cited lines of the stock file (renode-infrastructure 066a7f13's tree);
- every `guard` quote is inside its cited lines of the guard's model file;
- every field is in the work list, and its `values` cover the work list's armed and generated values.
What it cannot check (whether a quote covers the value written, whether a line is this register's) is the builder's own pass.

Usage: python3 -I spike/1206/check-reads.py ITEMS RM_TXT STOCK_ROOT MODEL_DIR READS.jsonl...
Prints one line per failed check, and a total; exit 1 if any failed."""
import json, re, sys

items, rm_txt, stock_root, model_dir, *reads = sys.argv[1:]
work = {}
for l in open(items):
    if l.startswith('#'): continue
    c = l.rstrip('\n').split('\t')
    work[c[0]] = dict(model=c[1], arm=c[6], gen=c[7])

pages, cur, buf = {}, 0, []
for line in open(rm_txt, errors='replace'):
    buf.append(line)
    m = re.search(r'(\d+)/3329', line)
    if m:
        pages[cur + 1] = ''.join(buf); buf = []; cur = int(m.group(1))
squash = lambda t: re.sub(r'\s+', '', t).replace('’', "'").replace('‘', "'").replace('“', '"').replace('”', '"')


def lines_of(path, spec):
    text = open(path, errors='replace').read().split('\n')
    rng = []
    for part in re.findall(r'L?(\d+)(?:\s*[-–]\s*L?(\d+))?', spec):
        a = int(part[0]); b = int(part[1] or part[0])
        rng.append('\n'.join(text[a - 1:b]))
    return '\n'.join(rng)


def guard_file(field):
    return model_dir.rstrip('/') + '/' + work[field]['model'].split(':')[0].split('/', 1)[1]


fails = n = 0
def fail(f, why):
    global fails; fails += 1; print('FAIL %s: %s' % (f, why))

for path in reads:
    for raw in open(path):
        if not raw.strip(): continue
        r = json.loads(raw); f = r.get('field', '?'); n += 1
        if f not in work: fail(f, 'not in the work list'); continue
        for q in r.get('rm', []):
            if squash(q['quote']) not in squash(pages.get(int(q['page']), '')):
                fail(f, 'rm quote not on p%s: %.80s' % (q['page'], q['quote']))
        for q in r.get('stock', []):
            src = stock_root.rstrip('/') + '/' + q['file']
            try:
                if squash(q['quote']) not in squash(lines_of(src, q['lines'])):
                    fail(f, 'stock quote not at %s %s: %.80s' % (q['file'], q['lines'], q['quote']))
            except OSError as e:
                fail(f, 'stock file %s: %s' % (q['file'], e))
        for q in r.get('guard', []):
            if squash(q['quote']) not in squash(lines_of(guard_file(f), q['lines'])):
                fail(f, 'guard quote not at %s: %.80s' % (q['lines'], q['quote']))
        if not r.get('rm'): fail(f, 'no rm quote')
        if not r.get('stock') and not r.get('guard'): fail(f, 'no stock or guard quote')
        said = squash(json.dumps(r.get('values', ''))).lower()
        for v in set(work[f]['arm'].split(',')) | set(work[f]['gen'].split(',')):
            if v in ('-', '', 'null') or v.split(' ')[-1].startswith('many'): continue
            iv = int(v, 0)
            forms = {v.lower(), hex(iv), str(iv), bin(iv)[2:], format(iv, '02b')}
            if r.get('all_values') is not True and not any(x in said for x in forms):
                fail(f, 'value %s written but not covered' % v)
        if r.get('from_memory'): fail(f, 'from memory, not read: %s' % r['from_memory'])
print('%d rows read, %d checks failed' % (n, fails))
sys.exit(1 if fails else 0)
