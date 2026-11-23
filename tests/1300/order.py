#!/usr/bin/env python3
"""TVI-WO-1300 §1.1 (tests/1300, measurement analysis; host Python): for each kernel-clocked peripheral of the query (spi1, spi2, i2c1, i2c2, usart1, rng),
the order in which a firmware enables its bus clock, starts its kernel source, and starts it; and the query's answer
at every start, by the rule as ruled (the mux's current input, p371-372). Reads one console in order: the wrapper's
TVI1300-T lines and Renode's access-log lines for the six peripherals.
Usage: order.py CONSOLE [LABEL]
"""
import re, sys

T = re.compile(r'^\[([\d:.]+)\].*TVI1300-T (\d+) ([RW])(\d+) off=0x([0-9A-F]+) val=0x([0-9A-F]+) pc=(\S+)(?: after=0x([0-9A-F]+))?')
A = re.compile(r'^\[([\d:.]+)\] \[\w+\] (spi1|spi2|i2c1|i2c2|usart1|rng): (?:\[cpu: [^\]]*\] )?Write\w+ to 0x([0-9A-F]+) \(\w*\), value 0x([0-9A-F]+)')
PER = {  # enable (offset, bit, name), mux, start (offset, mask, name)
    'spi1': ((0x0F0, 12, 'APB2ENR.SPI1EN'), 'SPI123SEL', (0x0, 0x201, 'CR1.SPE|CSTART')),
    'spi2': ((0x0E8, 14, 'APB1LENR.SPI2EN'), 'SPI123SEL', (0x0, 0x201, 'CR1.SPE|CSTART')),
    'i2c1': ((0x0E8, 21, 'APB1LENR.I2C1EN'), 'I2C123SEL', (0x0, 0x1, 'CR1.PE')),
    'i2c2': ((0x0E8, 22, 'APB1LENR.I2C2EN'), 'I2C123SEL', (0x0, 0x1, 'CR1.PE')),
    'usart1': ((0x0F0, 4, 'APB2ENR.USART1EN'), 'USART16SEL', (0x0, 0x1, 'CR1.UE')),
    'rng': ((0x0DC, 6, 'AHB2ENR.RNGEN'), 'RNGSEL', (0x0, 0x4, 'CR.RNGEN')),
}
MUX = {  # register offset, lsb, width, sources by value
    'CKPERSEL': (0x04C, 28, 2, ['hsi_ker_ck', 'csi_ker_ck', 'hse_ck', 'disabled']),
    'SPI123SEL': (0x050, 12, 3, ['pll1_q_ck', 'pll2_p_ck', 'pll3_p_ck', 'I2S_CKIN', 'per_ck', 'disabled', 'disabled', 'disabled']),
    'I2C123SEL': (0x054, 12, 2, ['rcc_pclk1', 'pll3_r_ck', 'hsi_ker_ck', 'csi_ker_ck']),
    'USART16SEL': (0x054, 3, 3, ['rcc_pclk2', 'pll2_q_ck', 'pll3_q_ck', 'hsi_ker_ck', 'csi_ker_ck', 'lse_ck', 'disabled', 'disabled']),
    'RNGSEL': (0x054, 8, 2, ['hsi48_ck', 'pll1_q_ck', 'lse_ck', 'lsi_ck']),
}
reg = {0x000: 0x25, 0x028: 0x02020200, 0x02C: 0x01FF0000}
for o in (0x04C, 0x050, 0x054, 0x058) + tuple(range(0x0D4, 0x0F8, 4)):
    reg[o] = 0
cur = {m: 0 for m in MUX}


def sel(m):
    off, lsb, w, _ = MUX[m]
    return (reg[off] >> lsb) & ((1 << w) - 1)


def running(src):
    cr = reg[0x000]
    if src in ('rcc_pclk1', 'rcc_pclk2'):
        return True
    if src == 'hsi_ker_ck': return bool(cr & 1)
    if src == 'csi_ker_ck': return bool(cr >> 7 & 1)
    if src == 'hse_ck': return bool(cr >> 16 & 1)
    if src == 'hsi48_ck': return bool(cr >> 12 & 1)
    if src == 'per_ck':
        return cur['CKPERSEL'] == sel('CKPERSEL') and running(MUX['CKPERSEL'][3][cur['CKPERSEL']])
    if src.startswith('pll'):
        n = int(src[3]); o = 'pqr'.index(src[5])
        on = cr >> (24 + 2 * (n - 1)) & 1
        en = reg[0x02C] >> (16 + 3 * (n - 1) + o) & 1
        ck = reg[0x028]
        divm = (ck >> [0, 4, 12, 20][n]) & 0x3F
        ref = [cr & 1, cr >> 7 & 1, cr >> 16 & 1, 0][ck & 3]
        return bool(on and en and divm and ref)
    return False


def settle():
    for m in MUX:
        s = sel(m)
        if cur[m] != s and running(MUX[m][3][cur[m]]) and running(MUX[m][3][s]):
            cur[m] = s


def query(p):
    (eo, eb, en), m, _ = PER[p]
    if not reg[eo] >> eb & 1:
        return False, en + ' = 0'
    if cur[m] != sel(m):
        return False, f'{m} pending: input {MUX[m][3][cur[m]]} -> {MUX[m][3][sel(m)]}'
    src = MUX[m][3][cur[m]]
    return running(src), f'{m} = {MUX[m][3][cur[m]]} ({"running" if running(src) else "NOT running"})'


label = sys.argv[2] if len(sys.argv) > 2 else ''
first = {}
starts = {p: [] for p in PER}
srcrun = {p: None for p in PER}
idx = 0
for line in open(sys.argv[1], errors='replace'):
    line = line.strip('\r\n')
    m = T.search(line)
    if m and m.group(3) == 'W' and m.group(4) == '32':
        off = int(m.group(5), 16); val = int(m.group(6), 16); pc = m.group(7); after = m.group(8)
        idx += 1
        off = off - 0x60 if 0x130 <= off <= 0x17C else off
        if off == 0x000 and after:
            reg[off] = int(after, 16)          # CR as the stock model stores it (its reverts included)
        elif off in reg:
            reg[off] = val
        settle()
        for p, ((eo, eb, en), mx, _) in PER.items():
            if off == eo and val >> eb & 1 and ('enable', p) not in first:
                first[('enable', p)] = ('#' + str(idx) + ' ' + m.group(1), pc)
            if srcrun[p] is None and cur[mx] == sel(mx) and running(MUX[mx][3][cur[mx]]):
                srcrun[p] = ('#' + str(idx) + ' ' + m.group(1), pc, mx + ' = ' + MUX[mx][3][cur[mx]])
        continue
    a = A.search(line)
    if a:
        p = a.group(2); off = int(a.group(3), 16); val = int(a.group(4), 16)
        idx += 1
        so, mask, sname = PER[p][2]
        if off == so and val & mask:
            k, why = query(p)
            starts[p].append(('#' + str(idx) + ' ' + a.group(1), val, k, why))

for p in PER:
    e = first.get(('enable', p))
    s = starts[p]
    bad = [x for x in s if not x[2]]
    print(f"{label}\t{p}\tenable {e[0] + ' pc ' + e[1] if e else 'never'}\tkernel clock first running {srcrun[p][0] + ' pc ' + srcrun[p][1] + ' (' + srcrun[p][2] + ')' if srcrun[p] else 'never'}"
          f"\tstarts {len(s)}{', first ' + s[0][0] + ' (' + s[0][3] + ')' if s else ''}\tstarts with no kernel clock: {len(bad)}{' first ' + bad[0][0] + ' ' + bad[0][3] if bad else ''}")
