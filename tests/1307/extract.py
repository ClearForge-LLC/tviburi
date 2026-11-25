#!/usr/bin/env python3
"""TVI-WO-1307: RM0433 Rev 9 §25.6's and §25.7's ADC register fields, from the held text, as tests/1307/data/rm0433-rev9-adc-fields.tsv
(tests/1306/extract.py's method). Each register's bit lines ("Bit N NAME:" / "Bits a:b NAME[..]:" / "Bits a:b Reserved"),
between its §25.6.n / §25.7.n heading and the next. Offsets are as seen from one ADC's own 0x400 window: §25.6 registers at their
printed offset, §25.7 common registers at 0x300 + their printed offset (the text: "relative to the master ADC base address + 0x300");
each printed "Address offset" is checked against the one below. ADC_OFRy and ADC_JDRy (y = 1 to 4) are one section each and
become four registers each. ADC_CR is printed twice, "Devices revision Y" then "Devices revision V" (BOOST is bit 8 on Y, bits 9:8
on V), and is emitted as ADC_CR_RevY and ADC_CR_RevV at the same offset; no other register is printed per revision (the per-revision
text inside ADC_CFGR.RES, ADCx_CCR.CKMODE is in the field's description, one bit line). Pages by the footer rule of
tests/1300/rmpage.sh. Fails unless every register covers bits 31:0 exactly once.
Usage: python3 -I tests/1307/extract.py RM0433_TXT > tests/1307/data/rm0433-rev9-adc-fields.tsv"""
import re, sys

# (section, [(register, printed offset)], base added to the printed offset)
SECS = [('25.6.1', [('ADC_ISR', 0x00)]), ('25.6.2', [('ADC_IER', 0x04)]), ('25.6.3', [('ADC_CR_RevY', 0x08), ('ADC_CR_RevV', 0x08)]),
        ('25.6.4', [('ADC_CFGR', 0x0C)]), ('25.6.5', [('ADC_CFGR2', 0x10)]), ('25.6.6', [('ADC_SMPR1', 0x14)]),
        ('25.6.7', [('ADC_SMPR2', 0x18)]), ('25.6.8', [('ADC_PCSEL', 0x1C)]), ('25.6.9', [('ADC_LTR1', 0x20)]),
        ('25.6.10', [('ADC_HTR1', 0x24)]), ('25.6.11', [('ADC_SQR1', 0x30)]), ('25.6.12', [('ADC_SQR2', 0x34)]),
        ('25.6.13', [('ADC_SQR3', 0x38)]), ('25.6.14', [('ADC_SQR4', 0x3C)]), ('25.6.15', [('ADC_DR', 0x40)]),
        ('25.6.16', [('ADC_JSQR', 0x4C)]), ('25.6.17', [('ADC_OFR%d' % y, 0x60 + 4 * (y - 1)) for y in range(1, 5)]),
        ('25.6.18', [('ADC_JDR%d' % y, 0x80 + 4 * (y - 1)) for y in range(1, 5)]), ('25.6.19', [('ADC_AWD2CR', 0xA0)]),
        ('25.6.20', [('ADC_AWD3CR', 0xA4)]), ('25.6.21', [('ADC_LTR2', 0xB0)]), ('25.6.22', [('ADC_HTR2', 0xB4)]),
        ('25.6.23', [('ADC_LTR3', 0xB8)]), ('25.6.24', [('ADC_HTR3', 0xBC)]), ('25.6.25', [('ADC_DIFSEL', 0xC0)]),
        ('25.6.26', [('ADC_CALFACT', 0xC4)]), ('25.6.27', [('ADC_CALFACT2', 0xC8)]),
        ('25.7.1', [('ADCx_CSR', 0x300)]), ('25.7.2', [('ADCx_CCR', 0x308)]), ('25.7.3', [('ADCx_CDR', 0x30C)]),
        ('25.7.4', [('ADCx_CDR2', 0x310)])]
text = open(sys.argv[1], encoding='utf-8', errors='replace').read().split('\n')
heads = {}
for i, l in enumerate(text):
    m = re.match(r'^\s*(25\.[67]\.\d+)\s+ADC ', l)
    if m and i > 39000 and not re.search(r'\. \. \.', l):
        heads.setdefault(m.group(1), i)
end = next(i for i, l in enumerate(text) if i > 39000 and re.match(r'^\s*25\.8\s+ADC register map\s*$', l))
page, pages = 0, []
for l in text:
    m = re.search(r'(\d+)/3329', l)
    pages.append(page + 1)          # content after footer N is page N+1; a footer line is its own page N
    if m:
        page = int(m.group(1))
        pages[-1] = page
print('# TVI-WO-1307: RM0433 Rev 9 §25.6\'s and §25.7\'s ADC register fields, extracted by tests/1307/extract.py from the held text; pages as')
print('# printed; line numbers there. Offsets are within one ADC\'s 0x400 window (common registers at 0x300 + their printed offset).')
print('# ADC_CR is printed per device revision and is two registers here, ADC_CR_RevY and ADC_CR_RevV, at the same offset 0x008.')
print('register\toffset\tsection\tfield\tmsb\tlsb\tpage\tline')
keys = [s for s, _ in SECS]
FIELD = re.compile(r'^\s*Bits? (\d+)(?::(\d+))? ([A-Za-z_0-9]+)(?:\[\d+:\d+\])?\s*[:,.]')
for n, (sec, regs) in enumerate(SECS):
    a = heads[sec]
    b = heads[keys[n + 1]] if n + 1 < len(keys) else end
    off = next((re.search(r'Address offset:\s*0x([0-9A-Fa-f]+)', text[i]) for i in range(a, b) if 'Address offset' in text[i]), None)
    if not off:
        sys.exit('%s: no address offset' % sec)
    printed = int(off.group(1), 16)
    if printed + (0x300 if sec.startswith('25.7') else 0) != regs[0][1]:
        sys.exit('%s: printed offset 0x%X does not match 0x%X' % (sec, printed, regs[0][1]))
    ranges = [(a, b)]
    if sec == '25.6.3':
        v = next(i for i in range(a, b) if re.match(r'^\s*Devices revision V\s*$', text[i]))
        ranges = [(a, v), (v, b)]
    for ri, (reg, o) in enumerate(regs):
        lo, hi = ranges[ri] if sec == '25.6.3' else ranges[0]
        bits = set()
        for i in range(lo, hi):
            m = FIELD.match(text[i])
            if not m:
                continue
            msb, lsb = int(m.group(1)), int(m.group(2) if m.group(2) is not None else m.group(1))
            if msb < lsb or bits & set(range(lsb, msb + 1)):
                sys.exit('%s: bits %d:%d overlap or are reversed (L%d)' % (reg, msb, lsb, i + 1))
            bits |= set(range(lsb, msb + 1))
            print('%s\t0x%03X\t§%s\t%s\t%d\t%d\t%d\tL%d' % (reg, o, sec, m.group(3), msb, lsb, pages[i], i + 1))
        if bits != set(range(32)):
            sys.exit('%s: bits %s not covered' % (reg, sorted(set(range(32)) - bits)))
