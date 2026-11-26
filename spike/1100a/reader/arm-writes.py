"""TVI-WO-1100a scratch reader: one logged replay's console -> its peripherals and its CPU writes per register.
Usage: python3 -I arm-writes.py CONSOLE ARM_S PERIPH_OUT WRITES_OUT [UNMAPPED_OUT]
  CONSOLE     the console (\r trimmed); its `peripherals` listing gives each instance's type and address range
  ARM_S       the virtual second of the arm (the COMMAND_ACK for 400 with result 0, read by mav.py)
  PERIPH_OUT  instance, type, ranges ("0xA-0xB" comma-joined)
  WRITES_OUT  instance, type, word offset, register name (Renode's), writes, of them at or after ARM_S, distinct word
              values, first value, last value, OR of values, AND of values (full-word writes only, else -), widths
              seen, first PC, first virtual second, first virtual second at or after ARM_S (or -), and the distinct word
              values themselves (comma-joined, at most 64; `many:<n>` past that)
  locked      (last column) writes Renode refused at the bus because the RCC guard locks an unclocked peripheral: they
              are logged as `sysbus: Tried to write a locked peripheral: machine-0.<inst>. Address A, value V`, with no PC
              (adversarial D3-1). They are counted apart: they never reach the model, as silicon ignores them (p373).
  UNMAPPED_OUT  CPU writes to an address no peripheral answers (`WriteDoubleWord to non existing peripheral at A`).
A write counts only with a CPU context (`[cpu: 0x...]`): a monitor write (the consumer's or this harness's) has none.
A sub-word write is placed in its word (offset & ~3), shifted by its byte lane, for OR and the distinct values.
The guards' own records (one JSON object after the source) are never accesses: only `Write<width> to` lines count."""
import re, sys, collections
con, arm, pout, wout = sys.argv[1], float(sys.argv[2]), sys.argv[3], sys.argv[4]
acc = re.compile(r'^\[(\d\d):(\d\d):(\d\d\.\d+)\] \[INFO\] ([A-Za-z0-9_.]+): \[cpu: (0x[0-9A-Fa-f]+)\] Write(UInt8|UInt16|UInt32|Byte|Word|DoubleWord) to (0x[0-9A-Fa-f]+) \(([^)]*)\), value (0x[0-9A-Fa-f]+)\s*$')
top = re.compile(r'^  [├└]── ([A-Za-z0-9_]+) \(([A-Za-z0-9_.]+)\)\s*$')
rng = re.compile(r'<(0x[0-9A-Fa-f]+), (0x[0-9A-Fa-f]+)>')
lock = re.compile(r'^\[(\d\d):(\d\d):(\d\d\.\d+)\] \[WARNING\] sysbus: Tried to write a locked peripheral: [\w-]+\.(\w+)\. Address (0x[0-9A-Fa-f]+), value (0x[0-9A-Fa-f]+)')
unm = re.compile(r'^\[(\d\d):(\d\d):(\d\d\.\d+)\] \[WARNING\] sysbus: \[cpu: (0x[0-9A-Fa-f]+)\] (?:\(tag: \'([^\']*)\'\) )?Write(\w+) to non existing peripheral at (0x[0-9A-Fa-f]+), value (0x[0-9A-Fa-f]+)')
locked = []; unmapped = []
W = {'UInt8': 1, 'Byte': 1, 'UInt16': 2, 'Word': 2, 'UInt32': 4, 'DoubleWord': 4}
regs = collections.OrderedDict(); per = collections.OrderedDict(); cur = None; listing = False; lines = 0
for l in open(con, errors='replace'):
    l = l.rstrip('\r\n')
    if l.startswith('Available peripherals:'):
        listing = True; continue
    if listing:
        m = top.match(l)
        if m:
            cur = m.group(1); per[cur] = [m.group(2), []]; continue
        m = rng.search(l)
        if m and cur and l.startswith('  │   │   <') or (m and cur and l.startswith('  │       <')) or (m and cur and l.startswith('      │   <')) or (m and cur and l.startswith('          <')):
            per[cur][1].append('%s-%s' % m.groups())
        continue
    m = lock.match(l)
    if m:
        locked.append((m.group(4), int(m.group(5), 16), int(m.group(6), 16))); continue
    m = unm.match(l)
    if m:
        unmapped.append((m.group(1) + ':' + m.group(2) + ':' + m.group(3), m.group(4), m.group(5) or '-', m.group(6), m.group(7), m.group(8))); continue
    m = acc.match(l)
    if not m:
        continue
    lines += 1
    t = int(m.group(1)) * 3600 + int(m.group(2)) * 60 + float(m.group(3))
    inst, pc, width, off, name, val = m.group(4), m.group(5), W[m.group(6)], int(m.group(7), 16), m.group(8), int(m.group(9), 16)
    word = off & ~3; v = val << ((off & 3) * 8)
    k = (inst, word)
    r = regs.get(k)
    if r is None:
        r = regs[k] = dict(name=name, n=0, after=0, vals=set(), first=v, last=v, orv=0, andv=0xFFFFFFFF, widths=set(), pc=pc, t=t, ta=None, sub=False, locked=0)
    r['n'] += 1; r['vals'].add(v); r['last'] = v; r['orv'] |= v; r['widths'].add(width)
    if width == 4: r['andv'] &= v
    else: r['sub'] = True
    if t >= arm:
        r['after'] += 1
        if r['ta'] is None: r['ta'] = t
for inst, a, v in locked:
    rs = per.get(inst, [None, []])[1]
    b = int(rs[0].split('-')[0], 16) if rs else 0
    k = (inst, (a - b) & ~3)
    r = regs.get(k)
    if r is None:
        r = regs[k] = dict(name='-', n=0, after=0, vals=set(), first=0, last=0, orv=0, andv=0xFFFFFFFF, widths=set(), pc='-', t=0.0, ta=None, sub=False, locked=0)
    r['locked'] += 1
with open(pout, 'w') as f:
    f.write('# instance\ttype\tranges\n')
    for k, (ty, rs) in per.items():
        f.write('%s\t%s\t%s\n' % (k, ty, ','.join(rs) or '-'))
with open(wout, 'w') as f:
    f.write('# instance\ttype\toffset\tregister\twrites\tafter_arm\tdistinct\tfirst\tlast\tor\tand\twidths\tfirst_pc\tfirst_s\tfirst_after_arm_s\tvalues\tlocked\n')
    for (inst, word), r in sorted(regs.items()):
        ty = per.get(inst, ['?'])[0]
        andv = '-' if r['sub'] or r['andv'] == 0xFFFFFFFF and r['orv'] != 0xFFFFFFFF else '0x%08X' % r['andv']
        f.write('\t'.join([inst, ty, '0x%03X' % word, r['name'], str(r['n']), str(r['after']), str(len(r['vals'])),
                           '0x%08X' % r['first'], '0x%08X' % r['last'], '0x%08X' % r['orv'], andv,
                           ','.join(str(w) for w in sorted(r['widths'])), r['pc'], '%.9f' % r['t'],
                           '-' if r['ta'] is None else '%.9f' % r['ta'],
                           (','.join('0x%X' % v for v in sorted(r['vals'])) if len(r['vals']) <= 64 else 'many:%d' % len(r['vals'])) or '-',
                           str(r['locked'])]) + '\n')
if len(sys.argv) > 5:
    with open(sys.argv[5], 'w') as f:
        f.write('# virtual time\tpc\ttag\twidth\taddress\tvalue\n')
        for u in unmapped: f.write('\t'.join(u) + '\n')
print('arm-writes: %d CPU write lines, %d locked writes, %d unmapped writes; %d registers on %d instances; %d peripherals listed' % (lines, len(locked), len(unmapped), len(regs), len({k[0] for k in regs}), len(per)))
