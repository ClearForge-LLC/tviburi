#!/usr/bin/env python3
"""TVI-WO-1300 / 1300a (tests/1300; host Python, run as `python3 -I tests/1300/allowlist.py check|write` from the repo root).

The RCC allow-list's default classification. Every RM0433 Rev 9 field (data/rm/fields.tsv) gets a class and an allowed set
from its RM access type and the stock model's kind (data/stock/fields.tsv), with the guard's own fields overriding.
From that it generates the two committed outputs: the `Fields` rows of model/GuardedSTM32H7_RCC.cs (between the exact
marker lines FIELDS_OPEN and FIELDS_CLOSE) and tests/1300/fields.resc.

  check   regenerate both in memory and compare byte for byte with the committed files: exit 0 if identical, else 1 + a diff
  write   rewrite both in place (the model file only between the markers)
"""
import collections, difflib, os, re, sys

HERE = os.path.dirname(os.path.abspath(__file__))
H = HERE + '/data/'
ROOT = os.path.dirname(os.path.dirname(HERE))
MODEL = ROOT + '/model/GuardedSTM32H7_RCC.cs'
RESC = HERE + '/fields.resc'
FIELDS_OPEN = '        private static readonly Row[] Fields =\n        {\n'
FIELDS_CLOSE = '        };\n'

SIX = {'SPI1EN', 'SPI2EN', 'I2C1EN', 'I2C2EN', 'USART1EN', 'RNGEN', 'USART2EN', 'USART3EN'}  # F7 (14:35Z): USART2/3 joined
# Kernel-clocked enables: the RM bit text names a kernel clock (39), plus Table 60 (p355-356) muxes whose enable text
# names only a "delay clock" or the pixel clock.
EXTRA_KERNEL = {'QSPIEN', 'SDMMC1EN', 'SDMMC2EN', 'LTDCEN'}
# Selector values the guard models, per mux (the source each names is modeled: the guard's MuxDef tables).
MUX_ALLOWED = {
    ('RCC_D1CCIPR', 'CKPERSEL[1:0]'): [0, 1, 2, 3],
    ('RCC_D2CCIP1R', 'SPI123SEL[2:0]'): [0, 1, 2, 4],               # 011 I2S_CKIN: a pin, not modeled; 101-111 reserved
                                                                    # (p414; refused, ruled 2026-11-23 17:16Z, TVI-WO-1300a)
    ('RCC_D2CCIP2R', 'I2C123SEL[1:0]'): [0, 1, 2, 3],
    ('RCC_D2CCIP2R', 'USART16SEL[2:0]'): [0, 1, 2, 3, 4, 6, 7],     # 101 lse_ck: not modeled
    ('RCC_D2CCIP2R', 'RNGSEL[1:0]'): [0, 1],                        # 10 lse_ck, 11 lsi_ck: not modeled
    ('RCC_D2CCIP2R', 'USART234578SEL[2:0]'): [0, 1, 2, 3, 4, 6, 7], # F7; 101 lse_ck: not modeled
}
# The architect's acknowledgements (TVI-WO-1300.rm0433.md §6.1, ruled 14:35Z): field -> (allowed, A-number, behaves as)
ACKED = {}
for f in ('MCO1PRE[3:0]', 'MCO2PRE[3:0]', 'RTCPRE[5:0]'):
    ACKED[('RCC_CFGR', f)] = (None, 2, 'no MCO pin and no RTC clock are modeled; with MCOx at reset no pin is driven')
ACKED[('RCC_CR', 'HSEBYP')] = (None, 3, 'the twin has no oscillator: bypass and crystal behave alike')
ACKED_RSTR = (None, 4, 'reserved: no flop on either side; written 1 by all-ones resets')
ACKED[('RCC_RSR', 'Reserved', 18)] = ([0, 1], 5, 'reserved, reset 1 by the register\'s 0x00FE0000: written 0 with RMVF, ignored by both')
for reg, f in (('RCC_AHB1LPENR', 'DMA2LPEN'), ('RCC_AHB2LPENR', 'RNGLPEN'), ('RCC_APB1LLPENR', 'SPI2LPEN'), ('RCC_APB2LPENR', 'SPI1LPEN')):
    ACKED[(reg, f)] = ([0, 1], 6, 'the twin does not model CSleep: the clock runs while the CPU sleeps, whatever this bit says (backlog)')
# TVI-WO-1300b (3), ruled 2026-11-25T03:03Z Q1: the armed Sentinel-03 run's four LPEN clears, acknowledged by name, those four only,
# with A6's reason (the release resolves an acknowledgement by its A-number: tools/tviburi-release `acknowledgement`)
ACKED_1300B = {(reg, f): ([0, 1], 'the twin does not model CSleep: the clock runs while the CPU sleeps, whatever this bit says (backlog) '
                                  '(TVI-WO-1300 A6; extended to this bit by TVI-WO-1300b, ruled 2026-11-25T03:03Z Q1)')
               for reg, f in (('RCC_AHB1LPENR', 'ADC12LPEN'), ('RCC_AHB4LPENR', 'ADC3LPEN'), ('RCC_APB1LLPENR', 'USART2LPEN'), ('RCC_APB2LPENR', 'TIM1LPEN'))}
GUARD_ANY = {('RCC_PLLCKSELR', f) for f in ('PLLSRC[1:0]', 'DIVM1[5:0]', 'DIVM2[5:0]', 'DIVM3[5:0]')} | \
    {('RCC_PLLCFGR', f'DIV{o}{n}EN') for o in 'PQR' for n in '123'}


# TVI-WO-1300c (4), ruled 2026-11-26T04:03Z: the rows that feed the guard's KernelHz and BusHz (the overlay's one frequency rule),
# restated with what the field does to the frequency and its page. Applied after the classification above; a row's class is kept,
# but for RCC_PLLCFGR's RGE, VCOSEL and FRACEN, now the guard's copy served on read (they were tags reading 0, acknowledged as A1
# "no PLL frequency is modeled beyond PLL1's integer one", no longer true).
RULED_C = 'TVI-WO-1300c, ruled 2026-11-26T04:03Z'
ONE_RULE = "the guard's KernelHz and BusHz (the overlay's one frequency rule; " + RULED_C + ')'
FREQ = {
    ('RCC_CR', 'HSIDIV[1:0]'): (None, 'the stock model (stored+callback): stored; a change reverted while PLLSRC = HSI with a PLL on; hsi_ck = 64 MHz / HSIDIV '
        '(p385), for sys_ck, hsi_ker_ck and the PLL reference in ' + ONE_RULE),
    ('RCC_CFGR', 'SWS[2:0]'): (None, 'ignored (read-only; stock computed-read); it selects sys_ck (the HSI, CSI, HSE or pll1_p_ck, p393) for ' + ONE_RULE),
    ('RCC_D1CFGR', 'D1CPRE[3:0]'): (None, 'the stock model (stored+callback): stored; rcc_hclk = sys_ck / D1CPRE / HPRE (pp. 394-395) in ' + ONE_RULE),
    ('RCC_D1CFGR', 'HPRE[3:0]'): (None, 'the stock model (stored+callback): stored; rcc_hclk = sys_ck / D1CPRE / HPRE (pp. 394-395) in ' + ONE_RULE),
    ('RCC_D2CFGR', 'D2PPRE1[2:0]'): (None, 'the stock model (stored+callback): stored; rcc_pclk1 = rcc_hclk / D2PPRE1 (p396) in ' + ONE_RULE),
    ('RCC_D2CFGR', 'D2PPRE2[2:0]'): (None, 'the stock model (stored+callback): stored; rcc_pclk2 = rcc_hclk / D2PPRE2 (p396) in ' + ONE_RULE),
}
for n in '123':
    FREQ[('RCC_PLLCKSELR', f'DIVM{n}[5:0]')] = (None, "the guard's copy (the kernel clock query); ref" + n + '_ck = the PLLSRC source / DIVM' + n
        + ', 0 "prescaler disabled" (pp. 398-399), in ' + ONE_RULE)
    FREQ[('RCC_PLLCFGR', f'PLL{n}RGE[1:0]')] = ('guard', "the guard's copy, served on read (the stock model drops it): ref" + n + '_ck must lie in the range it '
        'names, 1-2, 2-4, 4-8 or 8-16 MHz, edges inclusive, else ' + ONE_RULE + ' has no frequency ("must be set according to the reference input '
        'frequency", p347; pp. 401-402; Q3)')
    FREQ[('RCC_PLLCFGR', f'PLL{n}VCOSEL')] = ('guard', "the guard's copy, served on read: the VCO's output range, 192-960 MHz or 150-420 MHz (p404, "
        'p407, p410), and its input: VCOH takes 2-16 MHz, VCOL 1-2 MHz (p346, p347), else ' + ONE_RULE + ' has no frequency; VCOL above 2 MHz with '
        "its VCO in VCOL's range acknowledged by name, as ChibiOS chooses it (hal_lld_type1.h L1834-1837; bench row R11; Q1)")
    FREQ[('RCC_PLLCFGR', f'PLL{n}FRACEN')] = ('guard', "the guard's copy, served on read; FRACN" + n + ' is 0 (its allow-list), so the PLL runs in '
        'integer mode whatever this bit says (p349, p405) in ' + ONE_RULE)
    page = {'1': 'pp. 403-404', '2': 'pp. 406-407', '3': 'pp. 409-410'}[n]
    for o in 'NPQR':
        # p410 prints DIVN3[7:0] over bits 8:0, and data/rm/fields.tsv keeps its spelling
        FREQ[('RCC_PLL%sDIVR' % n, f'DIV{o}{n}[{(7 if n == "3" else 8) if o == "N" else 6}:0]')] = (None, '%s; the guard\'s copy of the write it passed, silicon\'s value, feeds '
            % ('the stock model (%s)' % ('computed-read' if n == '1' and o in 'NP' else 'stored+callback' if n == '1' else 'stored, DIVN and DIVQ narrowed to 8 and 6 bits where silicon has 9 and 7, a row'))
            + ONE_RULE + ' (' + page + '): none after a write the stock model did not keep, or one made while PLL' + n + ' ran (Q2)'
            + ('; DIVP1 = 0 or an odd divisor gives none (p404; Figure 47 p346; Q7)' if (n, o) == ('1', 'P') else ''))
FREQ[('RCC_PLLCKSELR', 'PLLSRC[1:0]')] = (None, "the guard's copy (the kernel clock query); the PLLs' reference, the HSI, CSI or HSE, 11 none (p399), in " + ONE_RULE)


def tsv(path):
    return [l.rstrip('\n').split('\t') for l in open(path, encoding='utf-8') if l.strip() and not l.startswith(('register', '#'))]


def build():
    stock = collections.defaultdict(list)
    for r in tsv(H + 'stock/fields.tsv'):
        try:
            stock[int(r[1], 16)].append((int(r[3]), int(r[4]), r[5], r[2], r[7]))
        except (ValueError, IndexError):
            pass


    def skind(off, msb, lsb):
        ks = {(k, b) for (m, l, k, n, b) in stock.get(off, []) if not (m < lsb or l > msb) and k != 'other'}
        if not ks:
            return ('undefined' if off not in stock else 'gap'), ''
        kinds = sorted({k for k, _ in ks})
        return ','.join(kinds), '; '.join(sorted({b for _, b in ks}))[:120]


    rows = []
    for r in tsv(H + 'rm/fields.tsv'):
        reg, off, field, msb, lsb, acc, reset, page = r[:8]
        off = int(off, 16); msb = int(msb); lsb = int(lsb)
        if reg == 'RCC_ICSCR':
            continue   # one register per offset: 0x004 is RCC_HSICFGR (revision V); RCC_ICSCR is revision Y's
        if reg == 'RCC_CSICFGR' and field == 'Reserved' and (msb, lsb) == (23, 8):
            lsb = 10   # p390 prints Reserved 23:8 over CSICAL[9:0]: a misprint; CSICAL holds 9:8
        try:
            rv = int(reset, 16) if reset.startswith('0x') else int(reset, 2)
        except ValueError:
            rv = None
        sk, sb = skind(off, msb, lsb)
        name = field
        base = re.sub(r'\[.*', '', field)
        if field == 'Reserved':
            cls, allowed, behaves = 'reserved', [rv or 0], 'reserved: dropped, reads its reset value'
        elif (reg, field) in MUX_ALLOWED:
            cls, allowed, behaves = 'guard', MUX_ALLOWED[(reg, field)], "the guard's copy and its mux (p371)"
        elif (reg, field) in GUARD_ANY:
            cls, allowed, behaves = 'guard', None, "the guard's copy (the kernel clock query)"
        elif reg == 'RCC_D3CCIPR' and base == 'ADCSEL':
            # TVI-WO-1307 (granted 2026-11-25T21:17Z Q3): the query answers adc12 and adc3 from this mux.
            cls, allowed, behaves = 'guard-copy', None, "the guard's copy, served on read; the ADCs' kernel clock mux, answered by the query for adc12 and adc3 (TVI-WO-1307 Q3)"
        elif re.match(r'RCC_D[123]CCIP', reg):
            cls, allowed, behaves = 'guard-copy', None, "the guard's copy, served on read; this mux's peripherals are not modeled, and their enable refuses"
        elif reg.endswith('ENR') and not reg.endswith('LPENR') and acc.startswith('rw'):
            if base in SIX:
                cls, allowed, behaves = 'guard', None, "the guard's copy and the stock bus enable; the kernel clock query"
            elif base in ('ADC12EN', 'ADC3EN'):
                # TVI-WO-1300b (3), ruled 2026-11-25T03:03Z Q1 (superseding 1300a's 17:52Z): the bus enable only
                cls, allowed, behaves = 'inherited', None, ("the guard's copy; the stock bus enable (" + sk + "): the bus lock only. With ADCx_CCR.CKMODE = 00 the "
                    "ADC's kernel clock is the one ADCSEL selects (p920, p419), answered by the query; the ADC guard asks it at ADSTART and registers as "
                    'watched (TVI-WO-1307 Q3) (TVI-WO-1300b, ruled 2026-11-25T03:03Z Q1)')
            elif base in EXTRA_KERNEL or 'kernel clock' in (r[8] if len(r) > 8 else ''):
                cls, allowed, behaves = 'kernel-unmodeled', [0], 'disabled: a kernel-clocked peripheral the query does not cover (TVI-WO-1300 (4))'
            elif sk in ('computed-read',):
                cls, allowed, behaves = 'inherited', None, "the guard's copy; the stock bus enable (" + sk + ')'
            else:
                cls, allowed, behaves = 'tag', [rv], 'its reset value (stock ' + sk + ')'
        elif acc == 'r':
            cls, allowed, behaves = 'read-only', None, 'ignored (read-only; stock ' + sk + ')'
        elif sk in ('stored', 'stored+callback', 'computed-read', 'write-only-action', 'read-only-constant'):
            cls, allowed, behaves = 'inherited', None, 'the stock model (' + sk + '): ' + sb
        else:
            cls, allowed, behaves = 'tag', [rv], 'its reset value (stock ' + sk + ')'
        if (reg, field) == ('RCC_CFGR', 'SW[2:0]'):
            cls, allowed, behaves = 'inherited', [0, 1, 2, 3], 'the stock model (stored+callback); 100-111 are reserved (p393), and the stock model keeps the old value'
        elif (reg, field) == ('RCC_BDCR', 'RTCEN'):
            # TVI-WO-1300b (5), ruled 2026-11-25T03:03Z Q4
            cls, allowed, behaves = 'inherited', [0, 1], ('the guard\'s copy, served on read: 0 from construction (a backup domain reset, p427, '
                'p334), kept across a machine reset (p427); it gates rtc_ck only (p366), which has no source while RTCSEL = 00 ("No clock", p427), '
                'and the RTC\'s registers follow RCC_APB4ENR.RTCAPBEN (p471), so `rtc` is never locked by it; RCC_BDCR\'s write protection '
                '(PWR_CR1.DBP, p427) is not modeled: the twin\'s PWR is a canned stub that reads DBP = 1 (acknowledged by name) (TVI-WO-1300b, '
                'ruled 2026-11-25T03:03Z Q4)')
        elif reg == 'RCC_BDCR' and acc not in ('r', '-') and field != 'Reserved':
            cls, allowed, behaves = 'inherited', [rv or 0], 'its reset value only: RCC_BDCR is write-protected after a system reset until PWR_CR1.DBP = 1 (p427), which the twin does not model, and the RTC\'s kernel clock (RTCSEL) is not modeled'
        if (reg, field) in FREQ:
            c, behaves = FREQ[(reg, field)]
            if c:
                cls, allowed = c, None   # any value: the guard's copy, served on read (was a tag acknowledged as A1)
        if (reg, field) in ACKED_1300B:
            allowed, why = ACKED_1300B[(reg, field)]
            cls, behaves = 'acknowledged', why
        ack = ACKED.get((reg, field)) or ACKED.get((reg, field, msb)) or (ACKED_RSTR if reg.endswith('RSTR') and field == 'Reserved' else None)
        if ack:
            allowed, a, why = ack
            cls, behaves = 'acknowledged', why + f' (TVI-WO-1300 A{a})'
        rows.append(dict(reg=reg, off=off, field=field, msb=msb, lsb=lsb, acc=acc, reset=rv, page=page, cls=cls,
                         allowed=allowed, behaves=behaves, stock=sk))
    return rows


# C# rows for the guard's `Fields` table (one per RM field), and the gate-clause-6 cases for tests/1300/fields.resc.
def cs(s):
    return '"' + s.replace('\\', '\\\\').replace('"', '\\"') + '"'

def ledger_class(x):  # the class strings tools/tviburi-release maps to RUL-16's states
    c = x['cls']
    if c in ('guard', 'guard-copy'):
        return 'modeled by the guard'
    if c in ('inherited', 'kernel-unmodeled'):
        return 'inherited, unaudited (acknowledged by name)'
    if c == 'acknowledged':
        return 'acknowledged by name'
    if c == 'tag' and x['stock'] == 'undefined':
        return 'undefined in the stock model'
    return c   # tag, reserved, read-only


def csname(x):  # SPI's row names: a reserved range is ResM_L (ResM for one bit); a field without its [m:n] suffix
    if x['field'] == 'Reserved':
        return f"Res{x['msb']}" + ('' if x['msb'] == x['lsb'] else f"_{x['lsb']}")
    return re.sub(r'\[.*', '', x['field'])


def emit_rows(rows, section):
    out = []
    for x in rows:
        a = 'Any' if x['allowed'] is None else ('Zero' if x['allowed'] == [0] else 'new uint[] { ' + ', '.join(str(v) for v in x['allowed']) + ' }')
        out.append(f"            R({cs(x['reg'])}, 0x{x['off']:03X}, {cs(section[x['reg']])}, {cs(csname(x))}, {x['msb']}, {x['lsb']}, {a}, {cs(ledger_class(x))}, {cs(x['behaves'])}, {x['page']}),\n")
    return ''.join(out)


def emit_resc(rows):
    out = []
    w = out.append
    # Gate clause 6 (RUL-17): tests/1300/fields.resc, one case per row whose allowed set is restricted: the register's
    # reset word with that field given the smallest value outside its set, then `machine Reset`.
    reset_word = collections.defaultdict(int)
    for x in rows:
        if x['reset']:
            reset_word[x['off']] |= x['reset'] << x['lsb']
    w('# TVI-WO-1300 gate clause 6 (RUL-17): every field outside the RCC allow-list, written with a value the model does\n'
      '# not implement, one per case, from reset; each must refuse with TVI-RCC-FIELD, kind unmodeled, naming the field.\n'
      '# GENERATED by tests/1300/allowlist.py from the allow-list rows (model/GuardedSTM32H7_RCC.cs `Fields`); the\n'
      '# `# FIELD` lines are the expected refusals, in order.\n')
    for x in rows:
        if x['allowed'] is None:
            continue
        width = x['msb'] - x['lsb'] + 1
        bad = next((v for v in range(1 << min(width, 4)) if v not in x['allowed']), None)
        if bad is None:
            continue
        mask = ((1 << width) - 1) << x['lsb']
        word = (reset_word[x['off']] & ~mask) | (bad << x['lsb'])
        bits = f"bit {x['lsb']}" if x['msb'] == x['lsb'] else f"{x['msb']}:{x['lsb']}"   # as Row.Bits formats it
        w(f"# FIELD {x['reg']}.{csname(x)} ({bits})\n")
        w(f"sysbus WriteDoubleWord 0x{0x58024400 + x['off']:08X} 0x{word:08X}\n")
        w("machine Reset\n")
    return ''.join(out)


def read(path):
    with open(path, encoding='utf-8', newline='') as f:
        return f.read()


def splice(model, rows_text):
    if model.count(FIELDS_OPEN) != 1:
        sys.exit('allowlist.py: the Fields opening marker is not found exactly once in the model')
    i = model.index(FIELDS_OPEN) + len(FIELDS_OPEN)
    j = model.index(FIELDS_CLOSE, i)
    return model[:i] + rows_text + model[j:]


def main():
    mode = sys.argv[1] if len(sys.argv) == 2 else ''
    if mode not in ('check', 'write'):
        sys.exit('usage: python3 -I tests/1300/allowlist.py check|write')
    rows = build()
    section = {r[0]: '§' + r[2] for r in tsv(H + 'rm/registers.tsv')}
    want = {MODEL: splice(read(MODEL), emit_rows(rows, section)), RESC: emit_resc(rows)}
    bad = 0
    for path, text in want.items():
        have = read(path)
        if have == text:
            continue
        bad = 1
        if mode == 'write':
            with open(path, 'w', encoding='utf-8', newline='') as f:
                f.write(text)
            print('rewrote', os.path.relpath(path, ROOT))
        else:
            print('DIFFERS:', os.path.relpath(path, ROOT))
            d = list(difflib.unified_diff(have.split('\n'), text.split('\n'), 'committed', 'regenerated', n=0, lineterm=''))
            print('\n'.join(l[:200] for l in d[:12]))
    if mode == 'check':
        print(f'{len(rows)} rows;', 'allow-list differs from the committed files' if bad else 'allow-list regenerates byte for byte')
        return bad
    print(len(rows), 'rows')
    return 0


if __name__ == '__main__':
    sys.exit(main())
