"""TVI-WO-1206 §2's acceptance: every field-table row this work order changed cites a manual page and a stock source line, and
every `(TVI-WO-1206 A<n>` it cites names a row of TVI-WO-1206.rm0433.md §3.

Usage: python3 -I spike/1206/check-rows.py BASE_REV   (from the repository root; BASE_REV is main's commit, e.g. 8e15435)
Compares each guard's rows in model/ with BASE_REV's; a changed row is one whose class or text differs. Exit 1 on any failure."""
import glob, os, re, subprocess, sys

base = sys.argv[1]
ROW = re.compile(r'R\("([A-Za-z0-9_]+)", (0x[0-9A-Fa-f]+), "[^"]*", "([^"]+)", \d+, \d+, (?:new uint\[\] \{[^}]*\}|[^,]+), '
                 r'("(?:[^"\\]|\\.)*"|Inherited), ("(?:[^"\\]|\\.)*"), \d+\)')
doc = open('docs/work-orders/TVI-WO-1206.rm0433.md').read()
sec3 = doc[doc.index('## 3.'):doc.index('## 4.')]
arows = set(re.findall(r'^\| A(\d+) \|', sec3, re.M))
changed = fails = 0
for path in sorted(glob.glob('model/*.cs')):
    old = subprocess.run(['git', 'show', '%s:%s' % (base, path)], capture_output=True, text=True).stdout
    before = {(m[0], m[2]): (m[3], m[4]) for m in ROW.findall(old)}
    for m in ROW.findall(open(path).read()):
        key, now = (m[0], m[2]), (m[3], m[4])
        if key in before and before[key] == now: continue
        changed += 1
        t = now[0] + ' ' + now[1]
        where = '%s %s.%s' % (os.path.basename(path), key[0], key[1])
        if not re.search(r'\bp\d{3,4}\b', t): fails += 1; print('FAIL %s: no manual page' % where)
        if not re.search(r'\.cs L\d+', t): fails += 1; print('FAIL %s: no stock source line' % where)
        for n in re.findall(r'\(TVI-WO-1206 A(\d+)[;)]', t):
            if n not in arows: fails += 1; print('FAIL %s: A%s is not a row of TVI-WO-1206.rm0433.md §3' % (where, n))
print('%d rows changed against %s, %d checks failed' % (changed, base, fails))
sys.exit(1 if fails or not changed else 0)
