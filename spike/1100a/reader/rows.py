"""TVI-WO-1100a scratch reader, step 1: place each queue row by (Renode instance, word offset, bits), never by name.
Usage: python3 -I rows.py QUEUE1100 JOINED1100 QUEUEAP CMSIS PERIPH OUT [ARMQUEUE]
  QUEUE1100  spike/1100/data/queue.tsv (722 rows)       JOINED1100  spike/1100/data/joined.tsv (positions: col 12, 6, 7, 16)
  QUEUEAP    spike/1150/data/queue-ap.tsv (844 rows)    CMSIS       1100's cmsis-regs.awk on Aethelgard's pinned header
  PERIPH     arm-periph.tsv (the run's own `peripherals` listing: each instance's address ranges)
  OUT        id, provenance, rank1100, class1100, instance, offset, lsb, width, address, how placed
  ARMQUEUE   Aethelgard 4400h's committed queue-ap-armed-sentinel-03.tsv (1150 (v)'s format, the same readers, the armed
             run on stock Renode): each of its ids not in QUEUEAP is an armed-only row, placed as an ArduPilot-only row
A 1100 row takes its instance and address from joined.tsv. A 1150 ArduPilot-only row named <INSTANCE>.<REG>.<field>
takes the CMSIS address and the bits Renode's cover names ([lsb+width]); one named <instance>@<offset>.<field> keeps
the Renode instance and offset 1150's writes-ap.tsv gave it. A cover with no bits (PWR's "by source", an absent
register) is the whole word. Every id must be placed exactly once: the reader exits 1 otherwise."""
import sys, re, collections
q1, j1, qa, cm, pf, out = sys.argv[1:7]
CMSIS_TO_RENODE = {'ADC1': 'adcM1S2', 'ADC3': 'adc3', 'ADC3_COMMON': 'adc3', 'BDMA': 'bdma', 'TIM1': 'timer1',
                   'USART2': 'usart2', 'USART3': 'usart3', 'I2C2': 'i2c2', 'RCC': 'rcc', 'FLASH': 'flashController', 'DMA1': 'dma1', 'DMA2': 'dma2', 'USART1': 'usart1',
                   'TIM5': 'timer5', 'PWR': 'pwr', 'SPI1': 'spi1', 'SPI2': 'spi2', 'I2C1': 'i2c1'}
base = {}
for l in open(pf):
    if l.startswith('#'): continue
    inst, ty, rs = l.rstrip('\n').split('\t')
    if rs != '-': base[inst] = int(rs.split(',')[0].split('-')[0], 16)
addr = {}
for l in open(cm):
    f = l.rstrip('\n').split('\t'); addr[(f[0], f[1])] = int(f[2], 16)
pos = {}
for l in open(j1):
    f = l.rstrip('\n').split('\t')
    if f[1] not in pos: pos[f[1]] = (f[15], int(f[11], 16), int(f[5]), int(f[6]))
q = {}
for l in open(q1):
    f = l.rstrip('\n').split('\t'); q[f[5]] = (f[0], f[2])
rows = []; bad = []
src = [(l, 'ap5') for l in open(qa)]
seen5 = {l.split('\t')[8] for l, _ in src}
if len(sys.argv) > 7:
    src += [(l, 'arm') for l in open(sys.argv[7]) if l.split('\t')[8] not in seen5]
for l, srcname in src:
    f = l.rstrip('\n').split('\t'); rid, origin, cover = f[8], f[12], f[10]
    rank, cls = q.get(rid, ('-', '-'))
    if rid in pos:
        inst, a, lsb, w = pos[rid]
        if inst not in base: bad.append((rid, 'no base for ' + inst)); continue
        rows.append((rid, '1100' if rank != '-' else srcname + '-only', rank, cls, inst, (a - base[inst]) & ~3, lsb, w, a, 'joined.tsv')); continue
    m = re.match(r'^([a-z0-9]+)@(0x[0-9A-Fa-f]+)\.', rid)
    b = re.search(r'\[(\d+)\+(\d+)\]', cover)
    lsb, w = (int(b.group(1)), int(b.group(2))) if b else (0, 32)
    if m:
        inst, off = m.group(1), int(m.group(2), 16)
        rows.append((rid, srcname + '-only', rank, cls, inst, off & ~3, lsb, w, base[inst] + off, 'renode offset')); continue
    p = rid.split('.')
    inst = CMSIS_TO_RENODE.get(p[0])
    if inst is None or (p[0], p[1]) not in addr: bad.append((rid, 'unplaced')); continue
    a = addr[(p[0], p[1])]
    rows.append((rid, srcname + '-only', rank, cls, inst, (a - base[inst]) & ~3, lsb, w, a, 'cmsis'))
ids = [r[0] for r in rows]
dup = [k for k, v in collections.Counter(ids).items() if v > 1]
with open(out, 'w') as f:
    f.write('# id\tprovenance\trank1100\tclass1100\tinstance\toffset\tlsb\twidth\taddress\tplaced_by\n')
    for r in rows:
        f.write('\t'.join([r[0], r[1], r[2], r[3], r[4], '0x%03X' % r[5], str(r[6]), str(r[7]), '0x%08X' % r[8], r[9]]) + '\n')
print('rows: %d placed (%s), %d unplaced, %d duplicate ids' % (len(rows), dict(collections.Counter(r[1] for r in rows)), len(bad), len(dup)))
for b in bad[:20]: print('  unplaced', b)
sys.exit(0 if not bad and not dup and len(rows) == len(src) and sum(1 for r in rows if r[1] in ('1100', 'ap5-only')) == 844 else 1)
