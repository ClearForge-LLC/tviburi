#!/usr/bin/env python3
"""TVI-WO-1306: RM0433 Rev 9 §47.9's I2C register fields, from the held text, as tests/1306/data/rm0433-rev9-i2c-fields.tsv
(tests/1305/extract.py's method). Each register's bit lines ("Bit N NAME:" / "Bits a:b NAME[..]:" / "Bits a:b Reserved"),
between its §47.9.n heading and the next. Pages by the footer rule of tests/1300/rmpage.sh. Fails unless every register covers
bits 31:0 exactly once. Usage: python3 -I tests/1306/extract.py RM0433_TXT > tests/1306/data/rm0433-rev9-i2c-fields.tsv"""
import re, sys

REGS = [('I2C_CR1', 0x00, '§47.9.1'), ('I2C_CR2', 0x04, '§47.9.2'), ('I2C_OAR1', 0x08, '§47.9.3'), ('I2C_OAR2', 0x0C, '§47.9.4'),
        ('I2C_TIMINGR', 0x10, '§47.9.5'), ('I2C_TIMEOUTR', 0x14, '§47.9.6'), ('I2C_ISR', 0x18, '§47.9.7'), ('I2C_ICR', 0x1C, '§47.9.8'),
        ('I2C_PECR', 0x20, '§47.9.9'), ('I2C_RXDR', 0x24, '§47.9.10'), ('I2C_TXDR', 0x28, '§47.9.11')]
text = open(sys.argv[1], encoding='utf-8', errors='replace').read().split('\n')
heads = {}
for i, l in enumerate(text):
    m = re.match(r'^\s*(47\.9\.\d+)\s+I2C .*\((I2C_\w+)\)\s*$', l)
    if m:
        heads.setdefault('§' + m.group(1), i)
heads.setdefault('§47.9.12', next(i for i, l in enumerate(text) if re.match(r'^\s*47\.9\.12\s+I2C register map\s*$', l)))
page, pages = 0, []
for l in text:
    m = re.search(r'(\d+)/3329', l)
    pages.append(page + 1)          # content after footer N is page N+1; a footer line is its own page N
    if m:
        page = int(m.group(1))
        pages[-1] = page
print('# TVI-WO-1306: RM0433 Rev 9 §47.9\'s I2C register fields, extracted by tests/1306/extract.py from the held text; pages as')
print('# printed; line numbers there. The classes are tests/1306/fields.py\'s.')
print('register\toffset\tsection\tfield\tmsb\tlsb\tpage\tline')
for n, (reg, off, sec) in enumerate(REGS):
    a = heads[sec]
    b = heads['§47.9.%d' % (n + 2)]
    bits = set()
    for i in range(a, b):
        m = re.match(r'^\s*Bits? (\d+)(?::(\d+))? ([A-Za-z_0-9]+)(?:\[\d+:\d+\])?[:,.]', text[i])
        if not m:
            continue
        msb, lsb = int(m.group(1)), int(m.group(2) if m.group(2) is not None else m.group(1))
        if msb < lsb or bits & set(range(lsb, msb + 1)):
            sys.exit('%s: bits %d:%d overlap or are reversed (L%d)' % (reg, msb, lsb, i + 1))
        bits |= set(range(lsb, msb + 1))
        print('%s\t0x%03X\t%s\t%s\t%d\t%d\t%d\tL%d' % (reg, off, sec, m.group(3), msb, lsb, pages[i], i + 1))
    if bits != set(range(32)):
        sys.exit('%s: bits %s not covered' % (reg, sorted(set(range(32)) - bits)))
