"""TVI-WO-1206: per-field coverage, the gate's measure (architect, 2026-11-27T00:43Z). Every field of every register the armed run
writes is judged, not only the bits a queue row covers (1100a's join could not see a field written at a register that has
another row: FEEDBACK, spike/1206/gap.py). A write is whole-register, so every field at a written offset is written.

Usage: python3 -I spike/1206/reader/fields.py JOIN_DATA LEDGER OUT
  JOIN_DATA  the join's outputs for the tree (arm-writes.tsv, arm-periph.tsv, states.tsv, arm-reach-outside.tsv)
  LEDGER     that tree's release ledger (LEDGER.jsonl)
  OUT        one line per (instance, offset, field): its state, its outcome, the register's path
Outcomes: guarded (state `guard`); read (any other state but `inherited-unaudited`, RUL-17 as amended); open
(`inherited-unaudited`); unlisted (a guarded type's register offset with no field rows: written, judged by no row: open);
stock (the instance is bound to a stock type, not a guard); unmapped (ruled 2026-11-27T02:12Z: an armed write to an
address the twin maps to nothing, listed by address and by what RM0433 places there, so a missing peripheral is never
invisible to the measure; each is judged against the ruled cut's text in UNMAPPED below). Above the cut: a register whose path is not `other`. Prints the
counts above the cut; exit 0 always (the counts are the result)."""
import collections, json, sys

data, ledger, out = sys.argv[1].rstrip('/') + '/', sys.argv[2], sys.argv[3]
def rd(f):
    ls = open(f).read().splitlines(); h = ls[0].lstrip('# ').split('\t')
    return [dict(zip(h, l.split('\t'))) for l in ls[1:] if not l.startswith('#')]
writes, periph = rd(data + 'arm-writes.tsv'), rd(data + 'arm-periph.tsv')
states, reach = rd(data + 'states.tsv'), rd(data + 'arm-reach-outside.tsv')
rows = collections.defaultdict(list); guards = set()
for l in open(ledger).read().splitlines()[1:]:
    r = json.loads(l)
    if r.get('row') == 'field':
        ty = r['peripheral'].replace('Tviburi.', ''); guards.add(ty); rows[(ty, int(r['offset'], 16))].append(r)
# The offset whose ledger rows judge a register: as spike/1206/reader/join.py's ledger_offset (join.py runs on import, so the
# six lines are repeated here, not imported): DMA streams 1-7 by stream 0's rows, DMAMUX channels by C0's, generators by RG0's.
def ledger_offset(g, inst, off):
    if g == 'GuardedSTM32DMA' and 0x10 <= off < 0xD0: return 0x10 + (off - 0x10) % 0x18
    if g == 'GuardedSTM32H7_ADC' and off < 0x300: return off & 0xFF
    if g == 'GuardedSTM32H7_Flash' and off in (0x100, 0x11C): return off - 0x100
    if g == 'GuardedSTM32H7_DMAMUX' and off < 0x40: return 0x000
    if g == 'GuardedSTM32H7_DMAMUX' and 0x100 <= off < 0x120: return 0x100
    return off
path = collections.defaultdict(set)
for r in states:
    if 'arm' in r['reach']: path[(r['instance'], int(r['offset'], 16))].add(r['path'].split(' (')[0])
for r in reach:
    path[(r['instance'], int(r['offset'], 16))].add(r['path'].split(' (')[0])
# What RM0433 Rev 9 places at each address the armed run writes and the twin does not map, and its place against the ruled
# cut (the cut's L31: off the four paths are "the interrupt controller; the RTC; the watchdog; ADC3's internal channels
# (BDMA, and DMAMUX2, which has no model at all); the debug units; the RNG").
UNMAPPED = {
    0x5802581C: ('DMAMUX2 C7CR (p132; register p709)', 'off the cut: ADC3\'s request to BDMA channel 7; ADC3 converts its internal '
                 'channels only (SQR1 0x4D2442: SQ1-3 = 17, 18, 19; PCSEL 0xE0000; p925: VINP[17] VBAT/4, [18] VSENSE, [19] VREFINT)'),
    0x5C00103C: ('DBGMCU APB1LFZ1 (p3195 base 0x5C001000; register p3197)', 'off the cut: the debug units (0x8 freezes TIM5 in debug)'),
    0x51008108: ('the AXI interconnect\'s GPV (p133, 0x51000000-0x510FFFFF; p111)', 'on no flight path: the AXI matrix\'s own '
                 'configuration, not named by the cut; the twin has no bus matrix'),
}
lines, tally, unm = [], collections.Counter(), collections.Counter()
for u in rd(data + 'arm-unmapped.tsv'):
    a = int(u['address'], 16)
    where, judged = UNMAPPED.get(a, ('not in UNMAPPED: judge it', 'unjudged'))
    lines.append('\t'.join(['(unmapped)', '-', '0x%08X' % a, where, '-', 'value %s at %s' % (u['value'], u['virtual time']), 'unmapped', judged]))
    unm['judged' if a in UNMAPPED else 'UNJUDGED'] += 1
for w in writes:
    inst, ty, off = w['instance'], w['type'], int(w['offset'], 16)
    p = ','.join(sorted(path.get((inst, off), {'(no path)'})))
    on = any(x not in ('other', '(no path)') for x in path.get((inst, off), ()))
    if ty not in guards:
        fs = [('-', 'stock type %s' % ty, 'stock')]
    elif not rows.get((ty, ledger_offset(ty, inst, off))):
        fs = [('-', 'no field rows at this offset', 'unlisted')]
    else:
        fs = [('%s.%s' % (f['register'], f['field']), f['state'],
               'guarded' if f['state'] == 'guard' else 'open' if f['state'] == 'inherited-unaudited' else 'read') for f in rows[(ty, ledger_offset(ty, inst, off))]]
    for name, st, outcome in fs:
        lines.append('\t'.join([inst, ty, '0x%03X' % off, w['register'], name, st, outcome, p]))
        if on: tally[outcome] += 1
with open(out, 'w') as o:
    o.write('# instance\ttype\toffset\tregister\tfield\tstate\toutcome\tregister_paths\n' + '\n'.join(lines) + '\n')
print('per-field coverage above the cut: %d fields at %d registers: %s' % (
    sum(tally.values()), len({(l.split('\t')[0], l.split('\t')[2]) for l in lines if l.split('\t')[6] != 'unmapped' and any(x not in ('other', '(no path)') for x in l.split('\t')[7].split(','))}),
    dict(tally)))
print('unmapped writes (outside the field count, each judged against the cut in its line): %s' % (dict(unm) or 'none'))
