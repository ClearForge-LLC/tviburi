#!/usr/bin/env python3
"""TVI-WO-1301: DS12117 Rev 10 Figure 6, "LQFP144 pinout" (p57), read mechanically: which name each of the 144
package pins carries. Input: Ghostscript's txtwrite (-dTextFormat=0) of page 57. Glyphs are joined into words
(horizontal words on one baseline, vertical words on one column). Each pin number 1..144 is paired with the name on
its own baseline (left and right sides), or the vertical name in its own column (top and bottom). Every number must appear once.
Output: TSV pin, name, page. Usage: ds12117-fig6.py f6.xml > lqfp144-fig6.tsv"""
import re, sys
chars = [(int(a), int(b), int(c), int(d), ch) for a, b, c, d, ch in
         re.findall(r'<char bbox="(\d+) (\d+) (\d+) (\d+)" c="([^"]*)"/>', open(sys.argv[1], encoding='utf-8', errors='replace').read())]

def words(items, horizontal):
    """Glyphs, in the file's (reading) order, joined into words along one line. A glyph with the previous glyph's
    exact box is in the same span (Ghostscript boxes a span, not each glyph); otherwise a word continues only where
    the next glyph starts within 2 pt of the last one's end, in the reading direction (rightward; upward).
    The bottom row's numbers are as close as 1 pt, so a four-digit run that reads as two consecutive pin numbers
    (`4041`) is split into them, its box halved."""
    out = []
    lines = {}
    for c in items:
        lines.setdefault(c[1] if horizontal else c[0], []).append(c)
    for line, row in lines.items():
        cur, prev = [], None
        for c in row:
            if c[4].isspace():
                if cur: out.append(cur); cur = []
                prev = None
                continue
            if cur and prev is not None and c[:4] != prev[:4]:
                if horizontal:
                    gap = c[0] - prev[2]
                else:
                    gap = min(prev[1], prev[3]) - max(c[1], c[3])
                if gap >= 2 or gap < -1:
                    out.append(cur); cur = []
            cur.append(c); prev = c
        if cur: out.append(cur)
    return [(''.join(c[4] for c in w), min(min(c[0], c[2]) for c in w), min(min(c[1], c[3]) for c in w),
             max(max(c[0], c[2]) for c in w), max(max(c[1], c[3]) for c in w)) for w in out]

W0 = words([c for c in chars if c[1] == c[3] and c[0] != c[2]], True) + words([c for c in chars if c[0] == c[2] and c[1] != c[3]], False)
W = []
for w in W0:
    t = w[0]
    if len(t) == 4 and t.isdigit() and int(t[:2]) + 1 == int(t[2:]) and w[2] == w[4]:
        mid = (w[1] + w[3]) / 2
        W += [(t[:2], w[1], w[2], mid, w[4]), (t[2:], mid, w[2], w[3], w[4])]
    else:
        W.append(w)
def dist(a, b):  # rectangle to rectangle, pt
    dx = max(0, max(a[1], b[1]) - min(a[3], b[3])); dy = max(0, max(a[2], b[2]) - min(a[4], b[4]))
    return (dx * dx + dy * dy) ** 0.5
W = [w for w in W if w[4] < 600]   # the figure's frame; the page footer ("DS12117 Rev 10") is below it
nums = [w for w in W if w[0].isdigit() and 1 <= int(w[0]) <= 144]
names = [w for w in W if not w[0].isdigit() and re.fullmatch(r'[A-Z][A-Z0-9_+\-]*', w[0]) and w[0] not in ('MSv41917V4',)]
pins = {}
horiz = lambda w: w[2] == w[4]
cx = lambda w: (w[1] + w[3]) / 2
for n in nums:
    if horiz(n):   # left and right sides: a name on the same baseline; bottom row: the vertical name in its column
        cands = [(abs(cx(m) - cx(n)), m) for m in names if horiz(m) and abs(m[2] - n[2]) <= 1]
        if not cands:
            cands = [(abs(m[2] - n[2]), m) for m in names if not horiz(m) and abs(cx(m) - cx(n)) <= 4]
    else:          # top row: the vertical name in its column
        cands = [(abs(m[2] - n[2]), m) for m in names if not horiz(m) and abs(cx(m) - cx(n)) <= 2]
    if cands:
        pins.setdefault(int(n[0]), []).append(min(cands, key=lambda t: t[0])[1][0])
bad = [k for k in range(1, 145) if len(pins.get(k, [])) != 1]
assert not bad, ('pin numbers not paired exactly once', bad[:20], {k: pins.get(k) for k in bad[:10]})
used = [pins[k][0] for k in range(1, 145)]
dup = sorted({u for u in used if used.count(u) > 1 and not re.fullmatch(r'V\w+|NRST|BOOT0|PDR_ON', u)})
assert not dup, ('a GPIO name on two pins', dup)
print('pin\tname\tpage')
for k in range(1, 145): print(f'{k}\t{pins[k][0]}\t57')
