#!/usr/bin/env python3
"""TVI-WO-1301 (1): DS12117 Rev 10 Table 9 ("Alternate functions", pp. 89-100), transcribed mechanically.

Inputs, all derived from the held PDF (sha256 607b9ead...) by Ghostscript in the pinned tools image:
  t9.xml   gs -sDEVICE=txtwrite -dTextFormat=0, pages 89-100 (one <page> per page; char bboxes in pt)
  gNN.pgm  gs -sDEVICE=pgmraw -r144, page NN (2 px per pt), for the ruled grid
Each character is placed in the cell of the ruled grid that contains it; a cell's text is its characters in reading
order with whitespace removed. Output: TSV port, pin, af, cell, page. Usage: ds12117-table9.py DIR > out.tsv
"""
import re, sys

def pgm(path):
    b = open(path, 'rb').read()
    toks, i = [], 0
    while len(toks) < 4:
        while b[i:i+1].isspace(): i += 1
        if b[i:i+1] == b'#':
            while b[i:i+1] != b'\n': i += 1
            continue
        j = i
        while not b[j:j+1].isspace(): j += 1
        toks.append(b[i:j]); i = j
    return int(toks[1]), int(toks[2]), b[i + 1:]

def longest(seq):
    best = cur = 0
    for d in seq:
        cur = cur + 1 if d else 0
        best = max(best, cur)
    return best

def clusters(xs):
    out = []
    for x in xs:
        if out and x - out[-1][-1] <= 2: out[-1].append(x)
        else: out.append([x])
    return [(c[0], c[-1]) for c in out]

def grid(path):
    w, h, px = pgm(path)
    hs = clusters([y for y in range(h) if longest(px[y*w + x] < 128 for x in range(w)) > 150])
    vs = clusters([x for x in range(w) if longest(px[y*w + x] < 128 for y in range(h)) > 60])
    mids = [(a + b) / 2 for a, b in vs]
    # the 17 AF boundaries: the longest run of rules spaced 74-76 px apart
    best = []
    for s in range(len(mids)):
        run = [mids[s]]
        for m in mids[s + 1:]:
            if 73 <= m - run[-1] <= 77: run.append(m)
            elif m - run[-1] > 77: break
        if len(run) > len(best): best = run
    assert len(best) == 17, (path, best)
    pinleft = max(m for m in mids if m < best[0] - 5)
    # rows: after the header's thick rule (a cluster >= 3 px), each rule closes a row
    thick = [i for i, (a, b) in enumerate(hs) if b - a >= 2]
    assert thick, path
    rules = [(a + b) / 2 for a, b in hs[thick[0]:]]
    return pinleft, best, (hs[0][0] + hs[0][1]) / 2, rules

def chars(xml):
    pages = xml.split('<page>')[1:]
    out = []
    for p in pages:
        cs = []
        for x0, y0, x1, y1, c in re.findall(r'<char bbox="(\d+) (\d+) (\d+) (\d+)" c="([^"]*)"/>', p):
            x0, y0, x1, y1 = map(int, (x0, y0, x1, y1))
            if y0 != y1: continue                      # vertical text: the margins and the "Port X" labels
            c = {'&amp;': '&', '&lt;': '<', '&gt;': '>', '&quot;': '"', '&apos;': "'"}.get(c, c)
            cs.append((2 * (x0 + min(x1 - x0, 5) / 2), 2 * (y0 - 2), y0, x0, c))   # px; a glyph is <= 5 pt wide (gs gives some a bogus x1); its body ~2 pt above the baseline
        out.append(cs)
    return out

def main(d):
    pages = chars(open(f'{d}/t9.xml', encoding='utf-8', errors='replace').read())
    assert len(pages) == 12
    rows_out = []
    for k, cs in enumerate(pages):
        page = 89 + k
        pinleft, afb, top, rules = grid(f'{d}/g{page}.pgm')
        def col(x):
            if pinleft < x < afb[0]: return 'pin'
            for i in range(16):
                if afb[i] < x < afb[i + 1]: return i
            return None
        # the header: AF0..AF15 each in its own column
        hdr = {}
        for cx, cy, yb, x0, c in cs:
            if top < cy < rules[0]:
                hdr.setdefault(col(cx), []).append((yb, x0, c))
        for i in range(16):
            first = ''.join(c for _, _, c in sorted(hdr.get(i, [])) if not c.isspace())
            assert first.startswith(f'AF{i}'), (page, i, first)
        for r in range(len(rules) - 1):
            cell = {}
            for cx, cy, yb, x0, c in cs:
                if rules[r] < cy < rules[r + 1]:
                    k2 = col(cx)
                    if k2 is not None: cell.setdefault(k2, []).append((yb, x0, c))
            text = {k2: ''.join(c for _, _, c in sorted(v) if not c.isspace()) for k2, v in cell.items()}
            pin = text.get('pin', '')
            assert re.fullmatch(r'P[A-K]\d{1,2}', pin), (page, r, pin, text)
            for i in range(16):
                rows_out.append((pin[1], pin, i, text.get(i, ''), page))
    print('port\tpin\taf\tcell\tpage')
    for row in rows_out: print('\t'.join(map(str, row)))

main(sys.argv[1])
