#!/usr/bin/env python3
"""TVI-WO-1305: the USART guard's Fields table (RUL-16, RUL-17), generated, never typed: from RM0433 Rev 9's USART register
fields (tests/1305/data/rm0433-rev9-usart-fields.tsv, written by tests/1305/extract.py) and the classes ruled
2026-11-24T18:55Z (Q8, Q2 (c)). Prints the C# `R(...)` rows of model/GuardedSTM32F7_USART.cs's `Fields`; tests/1305/usart.sh
`regen` checks them byte for byte.
Usage: python3 -I tests/1305/fields.py tests/1305/data/rm0433-rev9-usart-fields.tsv"""
import sys

G, I, RES, RO, T, U = ('modeled by the guard', 'inherited, unaudited (acknowledged by name)', 'reserved', 'read-only', 'tag',
                       'undefined in the stock model')
RULED = 'ruled 2026-11-24T18:55Z'
UE = 'protected under UE (p%d): changed with UE = 1 it is TVI-USART-UE, unmodeled (' + RULED + ' Q2)'
REINIT = ('protected under UE (p%d): a change with UE = 1 confined to BRR, CR1.M0/PCE/FIFOEN and CR2.RXINV is ChibiOS re-running '
          'usart_init on an enabled port (sdStart without sdStop), acknowledged by name with no record: the stock model takes it at '
          'once; RM0433 does not say what silicon does, and thousands of H7 flight controllers run this ArduPilot RC-protocol probing '
          'and change baud, field evidence, not a page (' + RULED + ' Q2 (c); bench row U1); any other change with UE = 1 is '
          'TVI-USART-UE, unmodeled')
NO_LINE = 'the twin\'s line has no polarity, word length, parity or framing: bytes are delivered whole'


def sw(what):
    """The class of a field the stock model gets wrong and the ruling acknowledges by name (RUL-16's stock-wrong, as
    TVI-WO-1303's acknowledged DMA fields)."""
    return 'stock, wrong (' + what + '; acknowledged by name, ' + RULED + ' Q8)'


def cls(reg, f, page):
    """(allowed symbol, class, behaves) for one field."""
    if f == 'Reserved':
        if reg == 'USART_ICR':
            return ('Any', I, 'ChibiOS writes 1 here (ICR = 0xFFFFFFFF at every usart_init, TVI-WO-1305.measure.md §2.1); RM0433 says '
                    '"must be kept at reset value"; a clear register: the write clears nothing (' + RULED + ' Q8)')
        if reg in ('USART_ISR', 'USART_RDR'):
            return ('Any', RO, 'reserved in a read-only register: a write is ignored and changes nothing (no record, RUL-18 as amended)')
        return ('Zero', RES, 'reserved: must be kept at reset value')
    r = reg[6:]
    if r == 'CR1':
        return {
            'RXFFIE': ('Zero', U, '0 only: the stock model has no bit 31 (reserved there); the RX FIFO never fills in the twin (U8)'),
            'TXFEIE': ('Zero', U, '0 only: the stock model has no bit 30 (reserved there); the TX FIFO interrupts are not modeled'),
            'FIFOEN': ('Any', sw('FIFOEN is reserved in the stock model, which runs no FIFO: its receive queue is unbounded and ORE never sets, U8'), 'kept by the guard and served on read (the stock model has no bit 29, U2), so a read-modify-write keeps it as '
                       'silicon does; FIFO mode acknowledged by name (' + RULED + ' Q8): the receive queue is unbounded and ORE never sets '
                       '(U8), the transmitter never fills; ' + REINIT % page),
            'M1': ('Zero', T, '0 only: a stock tag; 7-bit words are not modeled; ' + UE % page),
            'EOBIE': ('Zero', T, '0 only: a stock tag; smartcard mode is not modeled'),
            'RTOIE': ('Zero', I, '0 only: the stock receiver timeout runs on RTOIE alone, ignoring RTOEN, at 8 times RTO bit times '
                      '(STM32F7_USART.cs L118-131; U9), so it is not modeled'),
            'DEAT': ('Zero', T, '0 only: a stock tag; driver enable is not modeled; ' + UE % page),
            'DEDT': ('Zero', T, '0 only: a stock tag; driver enable is not modeled; ' + UE % page),
            'OVER8': ('Zero', I, '0 only: the stock model stores it and halves its baud divisor; oversampling by 8 is not audited (no '
                      'firmware writes it, TVI-WO-1305.measure.md §2.1); ' + UE % page),
            'CMIE': ('Zero', T, '0 only: a stock tag; character match is not modeled'),
            'MME': ('Zero', T, '0 only: a stock tag; mute mode is not modeled'),
            'M0': ('Any', sw('M0 is a stock tag: the word length is not modeled'), 'kept by the guard and served on read (a stock tag, U2); 1 only with PCE = 1, 8 data bits and parity (ArduPilot\'s '
                   'SBUS probe, TVI-WO-1305.measure.md §2.1), acknowledged by name (' + RULED + ' Q8): ' + NO_LINE + '; M0 = 1 with PCE = 0 '
                   '(9 data bits) is TVI-USART-FIELD; it enters IDLE\'s frame time; ' + REINIT % page),
            'WAKE': ('Zero', T, '0 only: a stock tag; mute mode is not modeled; ' + UE % page),
            'PCE': ('Any', sw('the stock model stores PCE but neither generates nor checks parity'), 'the stock model stores it; parity is neither generated nor checked (' + RULED + ' Q8): ' + NO_LINE + '; '
                    + REINIT % page),
            'PS': ('Any', I, 'the stock model stores it (its ParityBit); parity is not checked; ' + UE % page),
            'PEIE': ('Any', I, 'the stock model stores it; no parity error is ever raised, so it never fires'),
            'TXFNFIE': ('Any', I, 'the stock model\'s TXEIE: TXFNF always reads 1 (U14), so it fires while set'),
            'TCIE': ('Any', I, 'the stock model: TC set at each TDR write and when TE is cleared (U14)'),
            'RXFNEIE': ('Any', I, 'the stock model\'s RXNEIE: fires while a received byte waits'),
            'IDLEIE': ('Any', G, 'kept by the guard and served on read (a stock tag); with it set, the guard\'s IDLE raises the port\'s '
                       'interrupt (p2070, p2090; ' + RULED + ' Q5)'),
            'TE': ('Any', G, 'the stock model transmits; a start (UE with TE or RE) asks the RCC guard\'s kernel-clock query, '
                   'TVI-USART-KERNEL (' + RULED + ' Q3)'),
            'RE': ('Any', G, 'the stock model receives; a start (UE with TE or RE) asks the RCC guard\'s kernel-clock query, '
                   'TVI-USART-KERNEL (' + RULED + ' Q3)'),
            'UESM': ('Zero', T, '0 only: a stock tag; wake-up from Stop is not modeled'),
            'UE': ('Any', G, 'the stock model; at a start the kernel-clock query (TVI-USART-KERNEL); while set, protected fields refuse '
                   '(TVI-USART-UE); a write that clears it is judged as with UE = 0 (' + RULED + ' Q2); cleared, it resets every ISR '
                   'flag and the DMA requests (p2070): the guard flushes the receive queue and clears IDLE and TC'),
        }[f]
    if r == 'CR2':
        if f == 'RXINV':
            return ('Any', sw('RXINV is a stock tag: the line has no polarity'), 'kept by the guard and served on read (a stock tag, U2); ' + NO_LINE + ' (ArduPilot\'s SBUS probe, '
                    + RULED + ' Q8); ' + REINIT % page)
        if f == 'LBDIE':
            return ('Any', sw('LBDIE is a stock tag: no LIN break is raised'), 'kept by the guard and served on read (a stock tag); ChibiOS sets it on every port: no LIN break is ever '
                    'raised (LINEN 0 only), so it never fires (' + RULED + ' Q8)')
        if f == 'STOP':
            return ('Any', I, 'the stock model stores it (its StopBits); it enters IDLE\'s frame time; ' + UE % page)
        if f in ('DIS_NSS', 'SLVEN'):
            return ('Zero', U, '0 only: the stock model has no bits 3:0 (reserved there); SPI slave mode is not modeled')
        what = {'ADD': 'address match', 'RTOEN': 'the receiver timeout', 'ABRMOD': 'auto baud rate', 'ABREN': 'auto baud rate',
                'MSBFIRST': 'MSB-first order', 'DATAINV': 'data inversion', 'TXINV': 'TX inversion', 'SWAP': 'the pin swap',
                'LINEN': 'LIN mode', 'CLKEN': 'synchronous mode', 'CPOL': 'synchronous mode', 'CPHA': 'synchronous mode',
                'LBCL': 'synchronous mode', 'LBDL': 'LIN mode', 'ADDM7': 'address match'}[f]
        return ('Zero', T, '0 only: a stock tag; ' + what + ' is not modeled' + ('' if f in ('RTOEN', 'ABREN') else '; ' + UE % page))
    if r == 'CR3':
        if f in ('TXFTCFG', 'RXFTIE', 'RXFTCFG'):
            return ('Zero', U, '0 only: the stock model has no bits 31:25 (reserved there); FIFO thresholds are not modeled')
        if f == 'TXFTIE':
            return ('Zero', T, '0 only: the stock model\'s tag at bit 23 (named UCESM there); FIFO thresholds are not modeled')
        if f == 'DMAT':
            return ('Any', I, 'the stock model stores it; a memory-to-peripheral stream moves its bytes at the DMA\'s EN, unpaced '
                    '(TVI-WO-1303 Q5 (c))')
        if f == 'DMAR':
            return ('Any', G, 'the guard drives ReceiveDmaRequest: asserted while DMAR = 1, UE = 1 and a received byte waits, again '
                    'after each RDR read that leaves one, and once per frame while one waits; reset at UE = 0 (p2048, p2070; '
                    + RULED + ' Q6)')
        if f == 'EIE':
            return ('Any', sw('EIE is a stock tag: no framing, noise or overrun error is raised'), 'kept by the guard and served on read (a stock tag); ChibiOS sets it on every port: no framing, noise or '
                    'overrun error is ever raised, so it never fires (' + RULED + ' Q8)')
        what = {'TCBGTIE': 'smartcard mode', 'WUFIE': 'wake-up from Stop', 'WUS': 'wake-up from Stop', 'SCARCNT': 'smartcard mode',
                'DEP': 'driver enable', 'DEM': 'driver enable', 'DDRE': 'DMA disable on error', 'OVRDIS': 'overrun disable',
                'ONEBIT': 'one-bit sampling', 'CTSIE': 'hardware flow control', 'CTSE': 'hardware flow control',
                'RTSE': 'hardware flow control', 'SCEN': 'smartcard mode', 'NACK': 'smartcard mode', 'HDSEL': 'half-duplex mode',
                'IRLP': 'IrDA', 'IREN': 'IrDA'}[f]
        return ('Zero', T, '0 only: a stock tag; ' + what + ' is not modeled' + ('' if f in ('TCBGTIE', 'WUFIE', 'CTSIE') else '; ' + UE % page))
    if r == 'BRR':
        return ('Any', I, 'the stock model stores it: its BaudRate, the frequency property over BRR (baud accuracy is Aethelgard\'s); '
                'it enters IDLE\'s frame time; ' + REINIT % page)
    if r == 'GTPR':
        return ('Zero', U, '0 only: the stock model has no register 0x10 (U1); smartcard and IrDA are not modeled; the guard keeps 0 '
                'and serves it; ' + UE % page)
    if r == 'RTOR':
        if f == 'BLEN':
            return ('Zero', T, '0 only: a stock tag; smartcard block length is not modeled')
        return ('Zero', I, '0 only: the stock receiver timeout is not modeled (RTOIE)')
    if r == 'RQR':
        if f == 'RXFRQ':
            return ('Any', I, 'the stock model flushes the receive queue (U15)')
        return ('Zero', T if f in ('TXFRQ', 'ABRRQ') else I, '0 only: ' + {'TXFRQ': 'a stock tag; the TX FIFO flush is not modeled',
                'ABRRQ': 'a stock tag; auto baud rate is not modeled', 'MMRQ': 'the stock model takes it with no effect; mute mode is not modeled',
                'SBKRQ': 'the stock model takes it with no effect; no break is sent'}[f])
    if r == 'ISR':
        reads = {'IDLE': 'the guard\'s: set one idle frame after the last received byte, never at a start (p2090, p2021, p2040; ' + RULED + ' Q5), cleared by IDLECF and by UE = 0',
                 'RXFNE': 'the stock model\'s: a received byte waits',
                 'TXFNF': 'the stock model\'s: always 1 (transmission is immediate)',
                 'TC': 'the stock model\'s: set at each TDR write and when TE is cleared',
                 'TEACK': 'the stock model\'s: TE', 'REACK': 'the stock model\'s: RE',
                 'RTOF': 'the stock model\'s: 0 while RTOIE = 0 (the only value allowed)'}.get(f, 'reads 0: never set in the twin')
        return ('Any', RO, 'read-only (p%d): a write is ignored and changes nothing (no record, RUL-18 as amended); ' % page + reads)
    if r == 'ICR':
        if f == 'TCCF':
            return ('Any', I, 'the stock model clears TC')
        if f == 'IDLECF':
            return ('Any', G, 'clears the guard\'s IDLE (p2097)')
        if f == 'RTOCF':
            return ('Any', I, 'the stock model clears RTOF')
        return ('Any', I, 'clears a flag the twin never sets, so the write changes nothing; ChibiOS writes it at every init ('
                + RULED + ' Q8)')
    if r == 'RDR':
        return ('Any', RO, 'read-only (p%d): a write is ignored (no record); a read takes the stock model\'s next received byte; a byte or '
                'half-word read passes (' % page + RULED + ' Q7)')
    if r == 'TDR':
        return ('Any', I, 'the stock model sends its low byte (9-bit words refuse at CR1.M0); a byte or half-word write passes: '
                'ArduPilot\'s DMA writes it by bytes, though p2067 says "by words", field evidence (' + RULED + ' Q7)')
    if r == 'PRESC':
        return ('Zero', U, '0 only: the stock model has no register 0x2C (U1); the kernel clock is not prescaled; the guard keeps 0 '
                'and serves it; ' + UE % page)
    raise SystemExit('no class for ' + reg + '.' + f)


def cs(s):
    return '"' + s.replace('\\', '\\\\').replace('"', '\\"') + '"'


rows = [l.rstrip('\n').split('\t') for l in open(sys.argv[1]) if not l.startswith('#')][1:]
names = {}
for reg, off, sec, f, msb, lsb, page, _ in rows:
    names[(reg, f)] = names.get((reg, f), 0) + 1
for reg, off, sec, f, msb, lsb, page, _ in rows:
    allowed, c, behaves = cls(reg, f, int(page))
    name = f if names[(reg, f)] == 1 else 'Res%s_%s' % (msb, lsb)
    print('            R(%s, %s, %s, %s, %s, %s, %s, %s, %s, %s),' % (cs(reg), off, cs(sec), cs(name), msb, lsb, allowed, cs(c), cs(behaves), page))
