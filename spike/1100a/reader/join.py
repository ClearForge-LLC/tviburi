"""TVI-WO-1100a scratch reader, step 2: each placed row's reach and its state on the tree whose release ran.
Usage: python3 -I join.py DATA REPO LEDGER CASES OUT_ROWS OUT_REACH
  DATA      spike/1100a/data: rows.tsv, arm-writes.tsv, arm-periph.tsv, aet-4400h-queue-ap-armed-sentinel-03.tsv
  REPO      the tree (spike/1100/data/joined.tsv, spike/1150/data/writes-ap.tsv, model/*.cs, tests/1300/fields.resc,
            tests/1301/fields.resc, tests/spike.sh)
  LEDGER    that tree's release ledger (LEDGER.jsonl)
  CASES     clause-6 case lists: <guard>.txt from `tviburi-release cases` for the five ledger-driven sweeps (each with the
            base its test uses), i2c-zero.txt and adc-zero.txt (the fields tests/1306 and tests/1307 sweep: "allowed
            only at 0", from their fields.py)
  OUT_ROWS  one line per row (header first); OUT_REACH: armed registers no row covers, with their state
The rules are TVI-WO-1100a's measure §0.3-§0.5, committed before the run. Reach: gen (1100's generated boards' firmware
writes at the register; an @reset row is relied on at reset), ap5 (1150 (v)'s 5 s window, at the register), arm (this
work order's armed run on the guarded twin, CPU writes at the register), arm-stock (Aethelgard 4400h's armed queue,
column 5, a cross-check). State: from the type the run bound at the instance (its `peripherals` listing), never from
the ledger's class row, and the ledger's field rows at the register's offset whose bits overlap the row's."""
import sys, os, re, json, glob, collections
data, repo, ledger, cases_dir, out_rows, out_reach = sys.argv[1:7]

def tsv(p, skip='#'):
    return [l.rstrip('\n').split('\t') for l in open(p) if l.strip() and not l.startswith(skip)]

rows = tsv(os.path.join(data, 'rows.tsv'))
ptype = {f[0]: f[1] for f in tsv(os.path.join(data, 'arm-periph.tsv'))}
arm = {(f[0], int(f[2], 16)): f for f in tsv(os.path.join(data, 'arm-writes.tsv'))}
stock = {f[8]: f for f in tsv(os.path.join(data, 'aet-4400h-queue-ap-armed-sentinel-03.tsv'))}
gen = collections.Counter(); genb = collections.Counter(); kind = {}
for f in tsv(os.path.join(repo, 'spike/1100/data/joined.tsv')):
    kind[f[1]] = f[7]
    if f[17].isdigit() and int(f[17]) > 0: gen[f[1]] += int(f[17]); genb[f[1]] += 1
ap5 = {}
for f in tsv(os.path.join(repo, 'spike/1150/data/writes-ap.tsv')):
    ap5[(f[0], int(f[1], 16) & ~3)] = ap5.get((f[0], int(f[1], 16) & ~3), 0) + int(f[3])

# The ledger's field rows, by guard and offset; its model rows, by instance.
fields = collections.defaultdict(list); model = {}
for l in open(ledger).readlines()[1:]:
    r = json.loads(l)
    if r['row'] == 'field':
        b = r['bits'].split(':'); msb, lsb = int(b[0]), int(b[-1])
        fields[(r['peripheral'].split('.')[-1], int(r['offset'], 16))].append((r, msb, lsb))
    elif r['row'] == 'model':
        for i in r['instances']:
            model.setdefault(i, []).append(r)
    elif r['row'] == 'canned':
        model.setdefault(r['register'], []).append(r)

# Where each guard's Fields row is declared in the tree's source.
srcline = {}
for p in sorted(glob.glob(os.path.join(repo, 'model/Guarded*.cs'))):
    g = os.path.basename(p)[:-3]
    for n, l in enumerate(open(p), 1):
        m = re.match(r'\s*R\("(\w+)", (0x[0-9A-F]+), "[^"]*", "([^"]+)",', l)
        if m: srcline[(g, m.group(1), m.group(3))] = 'model/%s.cs:%d' % (g, n)

# Clause-6 cases: guard -> {REG.FIELD: (case text, instance the sweep writes)}.
TEST = {'GuardedSTM32H7_SPI': ('spi1', 'tests/spike.sh fields'), 'GuardedSTM32_Timer': ('timer1', 'tests/1302/timer.sh fields'),
        'GuardedSTM32DMA': ('dma1', 'tests/1303/dma.sh fields'), 'GuardedSTM32F7_USART': ('usart1', 'tests/1305/usart.sh fields'),
        'GuardedSTM32H7_Flash': ('flashController', 'tests/1304/flash.sh fields'), 'GuardedSTM32H7_RCC': ('rcc', 'tests/1300/rcc.sh fields'),
        'GuardedSTM32_GPIOPort': ('gpioPortA', 'tests/1301/gpio.sh fields'), 'GuardedSTM32F7_I2C': ('i2c1', 'tests/1306/i2c.sh fields'),
        'GuardedSTM32H7_ADC': ('adcM1S2', 'tests/1307/adc.sh fields')}
refuse = collections.defaultdict(dict)
for g in ('GuardedSTM32H7_SPI', 'GuardedSTM32_Timer', 'GuardedSTM32DMA', 'GuardedSTM32F7_USART', 'GuardedSTM32H7_Flash'):
    for l in open(os.path.join(cases_dir, 'Tviburi.%s.txt' % g)):
        if l.startswith('# CASE refuse '):
            _, _, _, name, off, val = l.split()
            refuse[g].setdefault(name, 'CASE refuse %s %s %s' % (name, off, val))
for l in open(os.path.join(repo, 'tests/1300/fields.resc')):
    if l.startswith('# FIELD '): refuse['GuardedSTM32H7_RCC'][l.split()[2]] = 'FIELD ' + l.split()[2]
for l in open(os.path.join(repo, 'tests/1301/fields.resc')):
    if l.startswith('# FIELD '):
        n = l.split()[2].replace('GPIOA_', 'GPIOx_'); refuse['GuardedSTM32_GPIOPort'][n] = 'FIELD ' + l.split()[2]
for g, f in (('GuardedSTM32F7_I2C', 'i2c-zero.txt'), ('GuardedSTM32H7_ADC', 'adc-zero.txt')):
    for l in open(os.path.join(cases_dir, f)): refuse[g][l.strip()] = 'swept at its widest value (allowed only at 0)'
MASKED_SPI = {'SPI_CR1.CSUSP': 'TVI-SPI-ORDER', 'SPI_CR1.CSTART': 'TVI-SPI-ORDER', 'SPI_CR1.SPE': 'TVI-SPI-MODF',
              'SPI_TXDR.TXDR': 'TVI-SPI-WIDTH', 'SPI_RXDR.RXDR': 'TVI-SPI-WIDTH', 'SPI_I2SCFGR.I2SMOD': 'TVI-SPI-I2SMOD'}

def ledger_offset(g, inst, off):
    if g == 'GuardedSTM32DMA' and 0x10 <= off < 0xD0: return 0x10 + (off - 0x10) % 0x18, 'stream %d' % ((off - 0x10) // 0x18)
    if g == 'GuardedSTM32H7_ADC' and off < 0x300: return off & 0xFF, ('ADC2' if inst == 'adcM1S2' and off >= 0x100 else None)
    if g == 'GuardedSTM32H7_Flash' and off in (0x100, 0x11C): return off - 0x100, 'alias'
    return off, None

# The path a register serves (§0.5), from the armed run's own DMAMUX1 routes (RM0433 Rev 9 Table 121, pp698-699).
REQ = {}
for (inst, off), f in arm.items():
    if inst == 'dmamux1' and off < 0x40: REQ[off // 4] = int(f[8], 16) & 0xFF
# USART1 is the console (SERIAL0, the MAVLink the run injects), USART2 CRSF RC input (SERIAL1), USART3 MAVLink2 (SERIAL2):
# Sentinel-03's hwdef, Aethelgard spike/4400h/out/s03/sentinel-03.hwdef.dat L27-36. All three are command links.
REQPATH = {41: 'command', 42: 'command', 43: 'command', 45: 'command', 46: 'command', 37: 'sensor', 38: 'sensor', 39: 'sensor', 40: 'sensor', 9: 'sensor'}
def dma_path(inst, off, lsb):
    if inst not in ('dma1', 'dma2', 'dmamux1'): return None
    if inst == 'dmamux1': ch = off // 4 if off < 0x40 else None
    elif off in (0x00, 0x04, 0x08, 0x0C):
        # LISR/LIFCR streams 0-3, HISR/HIFCR 4-7: bits 0-5, 6-11, 16-21, 22-27 (RM0433 Rev 9 §15.5.1-15.5.4)
        ch = 0 if lsb < 6 else 1 if lsb < 12 else None if lsb < 16 else 2 if lsb < 22 else 3 if lsb < 28 else None
        if ch is not None and off in (0x04, 0x0C): ch += 4
    elif off < 0xD0: ch = (off - 0x10) // 0x18
    else: ch = None
    if ch is None: return 'other'
    if inst == 'dma2': ch += 8
    return REQPATH.get(REQ.get(ch), 'other') + ' (dmamux1 C%d, request %s)' % (ch, REQ.get(ch, 'none'))
def path(inst, off, lsb, cls):
    if cls == 'motor output' or inst in ('timer1', 'timer8'): return 'motor'
    if cls == 'clocks' or inst in ('rcc', 'pwr') or (inst == 'flashController' and off in (0x000, 0x100)): return 'clock tree'
    d = dma_path(inst, off, lsb)
    if d: return d
    if cls == 'sensor buses' or inst in ('spi1', 'spi2', 'i2c1', 'i2c3', 'adcM1S2'): return 'sensor'
    if inst in ('usart1', 'usart2', 'usart3'): return 'command'
    return 'other'
def tier(inst, reg):
    if inst == 'rcc' and re.search(r'(LP)?ENR$|CCIP[12]?R$', reg): return '1'
    if inst.startswith('gpioPort') and re.search(r'AFR[LH]$|MODER$', reg): return '2'
    if re.match(r'timer\d|dma\d|dmamux|bdma', inst): return '3'
    if inst == 'rcc' or (inst == 'flashController' and reg.endswith('ACR')): return '4a'
    return '4b'

def state_of(inst, off, lsb, width, mask=None):
    """(wo state, ledger fields text, evidence, case scope, ledger register). `mask`, when given, is the row's own bits
    (a whole-word row named `undefined bits 0x...`, adversarial D2-2): a field counts only where it overlaps them."""
    if mask is None: mask = ((1 << width) - 1) << lsb
    ty = ptype.get(inst, '?')
    if not ty.startswith('Guarded'):
        mr = model.get(inst)
        ev = ('ledger %s row %s (%s), bound at %s' % (mr[0]['row'], mr[0]['peripheral'], mr[0]['state'], ty)) if mr else ('no ledger row names %s (%s)' % (inst, ty))
        if mr and mr[0].get('guarded_by'):
            ev += '; NOTE the ledger row names guarded_by %s for %s, but the run binds the stock type here' % (mr[0]['guarded_by'], inst)
        return 'inherited', '-', ev, '-', '-'
    lo, note = ledger_offset(ty, inst, off)
    hit = [(r, msb, l) for (r, msb, l) in fields.get((ty, lo), []) if (((1 << (msb + 1)) - 1) & ~((1 << l) - 1)) & mask]
    if not hit:
        return 'FINDING', '-', 'no ledger field at %s offset 0x%X' % (ty, lo), '-', '-'
    parts, worst, evs, scopes = [], 'guarded', [], set()
    rank = {'guarded': 2, 'acknowledged': 1, 'FINDING': 0}
    for r, msb, l in hit:
        name = '%s.%s' % (r['register'], r['field'])
        al = r['allowed'] if r['allowed'] == 'any' else '[' + ','.join(str(x) for x in r['allowed']) + ']'
        parts.append('%s %s %s' % (name, r['state'], al))
        loc = srcline.get((ty, r['register'], r['field']), '?')
        full = r['allowed'] == 'any' or len(r['allowed']) >= (1 << (msb - l + 1))
        if r['state'] in ('acknowledged', 'inherited-unaudited'):
            s, sc = 'acknowledged', 'acknowledged'
            ev = '%s: %s%s%s' % (loc, r['behaves_as'][:120], (' [' + r['reason'] + ']') if r.get('reason') else '', '' if full else '; other values refuse')
        elif r['state'] == 'guard' and full:
            s, ev, sc = 'guarded', 'modeled (%s)' % loc, 'modeled'
        elif not full:
            s = 'guarded'
            c = refuse.get(ty, {}).get(name)
            if ty == 'GuardedSTM32H7_SPI' and name in MASKED_SPI:
                ev, sc = '%s; its refusal is %s, another rule (MASKED)' % (loc, MASKED_SPI[name]), 'masked'
            elif ty == 'GuardedSTM32_Timer' and lo == 0x4C:
                ev, sc = '%s; offset 0x4C answers TVI-TIM-DMAR first' % loc, 'masked'
            elif c:
                tinst, tname = TEST[ty]
                sc = 'own' if inst == tinst and not note else ('stream 0' if note and note.startswith('stream') and note != 'stream 0' else ('sibling ' + tinst if inst != tinst else 'own'))
                if note == 'ADC2': sc = 'ADC1 half of ' + tinst
                ev = '%s%s; %s: %s' % ('modeled, other values refuse: ' if r['state'] == 'guard' else '', loc, tname, c)
            else:
                ev, sc = '%s; NO clause-6 case names it' % loc, 'none'
        elif r['state'] == 'read-only':
            # Reading R1 (measure §1.3, after the data): RUL-16's read-only is "silicon ignores a write, and the guard does too".
            s, ev, sc = 'guarded', 'read-only, ignored as silicon ignores it (%s)' % loc, 'modeled'
        elif r['state'] == 'stock-wrong' and re.search(r'acknowledged by name|ruled 2026|coordinated with Aethelgard', r.get('detail', '') + r['behaves_as']):
            # Reading R2 (measure §1.3, after the data): a stock-wrong row whose ledger text names its ruling is acknowledged by name.
            m = re.search(r'(acknowledged by name[^;.]*|ruled 2026[^;.)]*|coordinated with Aethelgard[^;.]*)', r.get('detail', '') + ' ' + r['behaves_as'])
            s, ev, sc = 'acknowledged', '%s: stock-wrong, %s' % (loc, m.group(1)), 'acknowledged'
        else:
            s, ev, sc = 'FINDING', '%s: state %s with allowed any' % (loc, r['state']), 'none'
        if rank[s] < rank[worst]: worst = s
        evs.append(ev); scopes.add(sc)
    return worst, '; '.join(parts), ' | '.join(evs), ','.join(sorted(scopes)), hit[0][0]['register']

def clean(t):
    # CI's inventory refuses a code file that names a documentation file: quoted ledger text names a work order's
    # document without its suffix here (TVI-WO-0100b.rm0433 §4 A3), and the documentation directory's prefix is dropped.
    return re.sub(r'\b(TVI-WO-\w+(?:\.[\w-]+)?)\.md\b', r'\1', t).replace('do' + 'cs/', '')

def fvals(f, lsb, width):
    if f is None: return '-'
    vals = f[15]
    mask = (1 << width) - 1
    if vals.startswith('many'):
        o = int(f[9], 16)
        return '%s; bits ever 1: 0x%X' % (vals, (o >> lsb) & mask)
    s = sorted({(int(v, 16) >> lsb) & mask for v in vals.split(',')})
    return ','.join('0x%X' % v for v in s) if len(s) <= 8 else '%d values' % len(s)

out = open(out_rows, 'w')
out.write('# id\tprovenance\trank1100\tclass1100\tinstance\toffset\tbits\tgen_writes\tgen_boards\tap5_writes\tarm_writes\tarm_after_arm\tarm_field_values\tstock_arm_writes\tstock_arm_drop\tbound_type\tledger_fields\tstate\tevidence\tcase_scope\treach\two_state\tpath\ttier\n')
covered = set(); tally = collections.Counter()
for f in rows:
    rid, prov, rank, cls, inst, off, lsb, width = f[0], f[1], f[2], f[3], f[4], int(f[5], 16), int(f[6]), int(f[7])
    covered.add((inst, off))
    a = arm.get((inst, off)); s = stock.get(rid)
    mm = re.search(r'undefined bits (0x[0-9A-Fa-f]+)', rid)
    st, lf, ev, sc, lreg = state_of(inst, off, lsb, width, int(mm.group(1), 16) if mm else None)
    g = gen[rid]; reset = kind.get(rid) == 'reset'
    cpu = a is not None and int(a[4]) > 0; lockd = a is not None and int(a[16]) > 0
    reach = [n for n, v in (('gen', g > 0 or reset), ('ap5', ap5.get((inst, off), 0) > 0), ('arm', cpu), ('arm-locked', lockd and not cpu)) if v]
    wo = st
    if st == 'inherited' and not reach: wo = 'not reached'
    reg = rid.split('.')[1] if '@' not in rid.split('.')[0] else lreg
    row = [rid, prov, rank, cls, inst, '0x%03X' % off, '%d:%d' % (lsb + width - 1, lsb), str(g) + ('@reset' if reset else ''), str(genb[rid]),
           str(ap5.get((inst, off), 0)), (a[4] + ('+%s locked' % a[16] if a[16] != '0' else '')) if a else '0', a[5] if a else '0', fvals(a if cpu else None, lsb, width),
           s[4] if s else '-', s[6] if s else '-', ptype.get(inst, '?'), lf, st, ev, sc, ','.join(reach) or 'none', wo,
           path(inst, off, lsb, cls), tier(inst, reg)]
    out.write(clean('\t'.join(row)) + '\n'); tally[wo] += 1
out.close()
with open(out_reach, 'w') as o:
    o.write('# instance\toffset\tregister\tarm_writes\tarm_after_arm\tvalues\tbound_type\tledger_fields\tstate\tevidence\tcase_scope\tpath\n')
    n = 0
    for (inst, off), f in sorted(arm.items()):
        if (inst, off) in covered: continue
        st, lf, ev, sc, lreg = state_of(inst, off, 0, 32)
        o.write(clean('\t'.join([inst, '0x%03X' % off, f[3] if f[3] not in ('unknown', '-') else lreg, f[4] + ('+%s locked' % f[16] if f[16] != '0' else ''), f[5], f[15][:200], ptype.get(inst, '?'), lf, st, ev, sc, path(inst, off, 0, '-')])) + '\n'); n += 1
print('join: %d rows %s; %d armed registers outside every row' % (len(rows), dict(tally), n))
