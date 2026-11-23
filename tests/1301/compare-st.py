#!/usr/bin/env python3
"""TVI-WO-1301 (1): DS12117 Rev 10 Table 9, as transcribed (model/stm32h753-af.tsv), against ST's open pin data as
Aethelgard holds it (the GPIO IP modes file, STM32H747_gpio_v1_0, which STM32H753ZITx.xml names).

Per (pin, AF): DS's set is the cell split at "/" ("-" is the empty set); ST's is every PinSignal of that pin whose
GPIO_AF value is GPIO_AF<n>_*. Each (pin, AF) whose two sets differ is a disagreement, printed with both sources, and
classed, without resolving it:
  af-number  a signal named by both sources on this pin, at different AF numbers
  presence   one source has no function at this AF and the other has one
  alias      every difference here is explained by the explicit alias map (ALIASES.tsv, each pair with its rule)
  name       both have functions here, and no signal of either is at another AF of this pin in the other source
Usage: compare-st.py TABLE.tsv GPIO-MODES.xml ALIASES.tsv  (exit 0; the report is the output)
"""
import csv, re, sys, xml.etree.ElementTree as ET

ds = {}
for row in csv.DictReader(open(sys.argv[1], encoding='utf-8'), delimiter='\t'):
    ds[(row['pin'], int(row['af']))] = (set() if row['cell'] == '-' else set(row['cell'].split('/')), row['cell'], row['page'])

alias = {}
for line in open(sys.argv[3], encoding='utf-8'):
    if line.startswith('#') or line.startswith('ds_signal\t'): continue
    d, st_, rule = line.rstrip('\n').split('\t')
    alias[d] = (set(st_.split('/')), rule)
root = ET.parse(sys.argv[2]).getroot()
tag = lambda e: e.tag.split('}')[-1]
st, stnames = {}, {}
for p in root.iter():
    if tag(p) != 'GPIO_Pin': continue
    raw = p.get('Name')
    pin = re.split(r'[ -]', raw)[0]
    stnames.setdefault(pin, []).append(raw)
    for sig in p:
        if tag(sig) != 'PinSignal': continue
        for sp in sig:
            if tag(sp) == 'SpecificParameter' and sp.get('Name') == 'GPIO_AF':
                for v in sp:
                    m = re.fullmatch(r'GPIO_AF(\d+)_\w+', (v.text or '').strip())
                    assert m, v.text
                    st.setdefault((pin, int(m.group(1))), set()).add(sig.get('Name'))

pins = sorted({k[0] for k in ds}, key=lambda s: (s[1], int(s[2:])))
stonly = sorted({k[0] for k in st} - set(pins))
print(f'# DS pins: {len(pins)}; ST GPIO_Pin entries: {sum(len(v) for v in stnames.values())}; ST pins with an AF and no DS row: {stonly or "none"}')
counts = {'agree': 0, 'af-number': 0, 'presence': 0, 'alias': 0, 'name': 0}
print('class\tpin\taf\tds_cell\tds_page\tst_signals\tnote')
for pin in pins:
    for af in range(16):
        d, cell, page = ds[(pin, af)]
        s = st.get((pin, af), set())
        if d == s:
            counts['agree'] += 1
            continue
        elsewhere = []
        for x in sorted(s - d):
            o = [a for a in range(16) if a != af and x in ds[(pin, a)][0]]
            if o: elsewhere.append(f'ST {x} is DS AF{o}')
        for x in sorted(d - s):
            o = [a for a in range(16) if a != af and x in st.get((pin, a), set())]
            if o: elsewhere.append(f'DS {x} is ST AF{o}')
        mapped = set().union(*[alias[x][0] if x in alias else {x} for x in d]) if d else set()
        cls = 'af-number' if elsewhere else ('presence' if not d or not s else 'alias' if mapped == s else 'name')
        if cls == 'alias':
            elsewhere = sorted({alias[x][1] for x in d if x in alias and x not in s})
        counts[cls] += 1
        print(f"{cls}\t{pin}\t{af}\t{cell}\t{page}\t{'/'.join(sorted(s)) or '-'}\t{'; '.join(elsewhere)}")
print('# ' + ', '.join(f'{k} {v}' for k, v in counts.items()))
