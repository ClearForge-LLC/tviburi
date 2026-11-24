#!/usr/bin/env python3
"""TVI-WO-1305: RM0433 Rev 9 §48.8's USART register fields, from the held text, as tests/1305/data/rm0433-rev9-usart-fields.tsv.
Each register's bit lines ("Bit N NAME:" / "Bits a:b NAME[..]:" / "Bits a:b Reserved"), in the FIFO-enabled views (§48.8.1
CR1 and §48.8.9 ISR; both firmwares run FIFOEN = 1, TVI-WO-1305.measure.md §2.1): the "[alternate]" FIFO-disabled views
(§48.8.2, §48.8.10) are skipped. Pages by the footer rule of tests/1300/rmpage.sh. Fails unless every register covers bits
31:0 exactly once. Usage: python3 -I tests/1305/extract.py RM0433_TXT > tests/1305/data/rm0433-rev9-usart-fields.tsv"""
import re, sys

REGS = [('USART_CR1', 0x00, '§48.8.1'), ('USART_CR2', 0x04, '§48.8.3'), ('USART_CR3', 0x08, '§48.8.4'), ('USART_BRR', 0x0C, '§48.8.5'),
        ('USART_GTPR', 0x10, '§48.8.6'), ('USART_RTOR', 0x14, '§48.8.7'), ('USART_RQR', 0x18, '§48.8.8'), ('USART_ISR', 0x1C, '§48.8.9'),
        ('USART_ICR', 0x20, '§48.8.11'), ('USART_RDR', 0x24, '§48.8.12'), ('USART_TDR', 0x28, '§48.8.13'), ('USART_PRESC', 0x2C, '§48.8.14')]
text = open(sys.argv[1], encoding='utf-8', errors='replace').read().split('\n')
heads = {}
for i, l in enumerate(text):
    m = re.match(r'^\s*(48\.8\.\d+)\s+USART .*\((USART_\w+)\)\s*$', l)
    if m and 'LPUART' not in l:
        heads.setdefault('§' + m.group(1), i)
# Every §48 heading after §48.8.1 bounds the section before it (the LPUART chapter's included).
starts = sorted(i for i, l in enumerate(text) if re.match(r'^\s*(48\.\d+(\.\d+)?|49\.\d+)\s+[A-Z]', l) and i > heads['§48.8.1'])
page, pages = 0, []
for l in text:
    m = re.search(r'(\d+)/3329', l)
    pages.append(page + 1)          # content after footer N is page N+1; a footer line is its own page N
    if m:
        page = int(m.group(1))
        pages[-1] = page
print('# TVI-WO-1305: RM0433 Rev 9 §48.8\'s USART register fields (the FIFO-enabled views of CR1 and ISR), extracted by')
print('# tests/1305/extract.py from the held text; pages as printed; line numbers there. The classes are tests/1305/fields.py\'s.')
print('register\toffset\tsection\tfield\tmsb\tlsb\tpage\tline')
for reg, off, sec in REGS:
    a = heads[sec]
    b = min([s for s in starts if s > a] + [len(text)])
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
