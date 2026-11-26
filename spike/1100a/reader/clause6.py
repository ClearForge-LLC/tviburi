"""TVI-WO-1100a scratch reader: gate clause 6 per guard, the ledger's restricted rows against the sweep's refuse cases.
Usage: python3 -I clause6.py REPO LEDGER CASES PERIPH OUT
  For each guard: its field rows whose `allowed` excludes some value (each must refuse, RUL-17); how many a refuse case
  of its clause-6 sweep names (the release's `cases`, tests/1300/fields.resc, tests/1301/fields.resc, or the I2C and ADC
  sweeps' "allowed only at 0" lists); which rows none names; the instance the sweep writes against the instances the
  armed run bound to that guard (its `peripherals` listing); and how the sweep writes (monitor writes, pc none)."""
import sys, os, json, collections
repo, ledger, cases_dir, pf, out = sys.argv[1:6]
bound = collections.defaultdict(list)
for l in open(pf):
    if l.startswith('#'): continue
    i, t, _ = l.rstrip('\n').split('\t')
    if t.startswith('Guarded'): bound[t].append(i)
restricted = collections.defaultdict(list)
for l in open(ledger).readlines()[1:]:
    r = json.loads(l)
    if r['row'] != 'field' or r['allowed'] == 'any': continue
    b = r['bits'].split(':'); w = int(b[0]) - int(b[-1]) + 1
    if len(r['allowed']) >= (1 << w): continue
    restricted[r['peripheral'].split('.')[-1]].append('%s.%s' % (r['register'], r['field']))
named = collections.defaultdict(set)
for g in ('GuardedSTM32H7_SPI', 'GuardedSTM32_Timer', 'GuardedSTM32DMA', 'GuardedSTM32F7_USART', 'GuardedSTM32H7_Flash'):
    for l in open(os.path.join(cases_dir, 'Tviburi.%s.txt' % g)):
        if l.startswith('# CASE refuse '): named[g].add(l.split()[3])
for l in open(os.path.join(repo, 'tests/1300/fields.resc')):
    if l.startswith('# FIELD '): named['GuardedSTM32H7_RCC'].add(l.split()[2])
for l in open(os.path.join(repo, 'tests/1301/fields.resc')):
    if l.startswith('# FIELD '): named['GuardedSTM32_GPIOPort'].add(l.split()[2].replace('GPIOA_', 'GPIOx_'))
for g, f in (('GuardedSTM32F7_I2C', 'i2c-zero.txt'), ('GuardedSTM32H7_ADC', 'adc-zero.txt')):
    named[g] |= {l.strip() for l in open(os.path.join(cases_dir, f))}
SWEPT = {'GuardedSTM32H7_SPI': 'spi1', 'GuardedSTM32_Timer': 'timer1', 'GuardedSTM32DMA': 'dma1 (stream 0 rows; streams 1-7 written 0 only)',
         'GuardedSTM32F7_USART': 'usart1', 'GuardedSTM32H7_Flash': 'flashController', 'GuardedSTM32H7_RCC': 'rcc',
         'GuardedSTM32_GPIOPort': 'gpioPortA', 'GuardedSTM32F7_I2C': 'i2c1', 'GuardedSTM32H7_ADC': 'adcM1S2 (ADC1 and common offsets)'}
with open(out, 'w') as o:
    o.write('# guard\trestricted ledger rows\tnamed by a refuse case\tnot named\tswept instance\tinstances bound in the armed run\thow written\n')
    for g in sorted(SWEPT):
        rs = restricted[g]; miss = [x for x in rs if x not in named[g]]
        o.write('\t'.join([g, str(len(rs)), str(len(rs) - len(miss)), ','.join(miss) or '-', SWEPT[g], ','.join(sorted(bound[g])), 'monitor sysbus writes (pc none)']) + '\n')
        print(g, len(rs), len(rs) - len(miss), miss[:12])
