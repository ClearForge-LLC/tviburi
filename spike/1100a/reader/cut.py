"""TVI-WO-1100a scratch reader: the cut table (measure §3.1) from the two state tables, by measure §0.5's rule.
Usage: python3 -I cut.py STATES OUTSIDE
Counts what the armed run reaches (rows whose reach names `arm`, and registers outside the queue) on the four paths
(motor, clock tree, sensor bus, command link), by RUL-13's amended tier (1150's rank-ap.awk tiers) and family, with
each state's count. Prints one line per (tier, family)."""
import sys, csv, re, collections
states, outside = sys.argv[1:3]
FAM = {'rcc': 'RCC', 'pwr': 'PWR', 'timer1': 'TIM1', 'spi1': 'SPI1/2', 'spi2': 'SPI1/2', 'i2c1': 'I2C1/2', 'i2c2': 'I2C1/2',
       'usart1': 'USART1-3', 'usart2': 'USART1-3', 'usart3': 'USART1-3', 'adcM1S2': 'ADC1', 'dmamux1': 'DMAMUX1',
       'dma1': 'DMA1/2 streams', 'dma2': 'DMA1/2 streams', 'flashController': 'FLASH.ACR'}
def fam(inst): return 'GPIO' if inst.startswith('gpioPort') else FAM.get(inst, inst)
def tier(inst, reg):
    if inst == 'rcc' and re.search(r'(LP)?ENR$|CCIP[12]?R$', reg): return '1'
    if inst.startswith('gpioPort') and re.search(r'AFR[LH]$|MODER$', reg): return '2'
    if re.match(r'timer\d|dma\d|dmamux|bdma', inst): return '3'
    if inst in ('rcc', 'flashController'): return '4a'
    return '4b'
T = collections.defaultdict(collections.Counter)
for r in csv.reader(open(states), delimiter='\t'):
    if r[0].startswith('#') or 'arm' not in r[20].split(','): continue
    p = r[22].split(' (')[0]
    if p == 'other': continue
    k = (r[23], fam(r[4])); T[k][r[21]] += 1; T[k]['rows'] += 1; T[k]['path:' + p] += 1
for r in csv.reader(open(outside), delimiter='\t'):
    if r[0].startswith('#'): continue
    p = r[11].split(' (')[0]
    if p == 'other': continue
    k = (tier(r[0], r[2].split('.')[0]), fam(r[0])); T[k][r[8].lower()] += 1; T[k]['regs'] += 1; T[k]['path:' + p] += 1
for k in sorted(T):
    c = T[k]
    print('tier %s\t%s\t%s\trows %d + registers %d\tguarded %d\tacknowledged %d\topen %d\tfinding %d' % (
        k[0], k[1], ','.join(sorted(x[5:] for x in c if x.startswith('path:'))), c['rows'], c['regs'], c['guarded'],
        c['acknowledged'], c['inherited'], c['finding']))
