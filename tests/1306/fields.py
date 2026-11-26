#!/usr/bin/env python3
"""TVI-WO-1306: the I2C guard's Fields table (RUL-16, RUL-17), generated, never typed: from RM0433 Rev 9's I2C register fields
(tests/1306/data/rm0433-rev9-i2c-fields.tsv, written by tests/1306/extract.py) and the classes ruled 2026-11-25T05:06Z (Q2, Q5,
Q8-Q11) and, for the NACK, 2026-11-26T00:22Z (TVI-WO-1306a). Prints the C# `R(...)` rows of model/GuardedSTM32F7_I2C.cs's `Fields`; tests/1306/i2c.sh `regen` checks them byte for
byte. Usage: python3 -I tests/1306/fields.py tests/1306/data/rm0433-rev9-i2c-fields.tsv"""
import sys

G, I, RES, RO, T, U = ('modeled by the guard', 'inherited, unaudited (acknowledged by name)', 'reserved', 'read-only', 'tag',
                       'undefined in the stock model')
RULED = 'ruled 2026-11-25T05:06Z'
PE = ('protected under PE (p%d): changed with PE = 1 it is TVI-I2C-PE, unmodeled: RM0433 says it can be programmed only with PE = 0 '
      'and not what silicon does with such a write (' + RULED + ' Q2)')
TARGET = 'target (slave) mode is not modeled'
SMBUS = 'SMBus is not modeled'
RULED2 = 'ruled 2026-11-26T00:22Z'
NACK = ('a write START to an address with no device is NACKed by the guard: NACKF nine SCL periods after the START (p1955; the period '
        'by p1971 from TIMINGR, DNF and the RCC guard\'s KernelHz, each tSYNC at its stated floor: a lower bound), STOPF one SCL period '
        'after NACKF (inferred: RM0433 gives no figure), whatever AUTOEND ("a STOP condition is automatically sent", p1975), the guard '
        'writing the stock line "Unknown slave at address N." under its own name (TVI-WO-1306a.measure.md; ' + RULED2 + ' Q1-Q3, Q5, Q8); '
        'with no kernel-clock frequency it is TVI-I2C-NACK, unmodeled (Q2); a read START to an address with no device is TVI-I2C-ABSENT, '
        'unmodeled: RM0433 states the automatic STOP only for a write (Q6)')
OPEN = ('changed while a transfer is open (START passed, no TC, TCR or STOPF since, no STOP or PE clear) it is TVI-I2C-TRANSFER, '
        'unmodeled ("Changing these bits when the START bit is set is not allowed", p2004-2006; ' + RULED + ' Q8)')


def cls(reg, f, msb, lsb, page):
    """(allowed symbol, class, behaves) for one field."""
    r = reg[4:]
    if f == 'Reserved':
        if r in ('ISR', 'PECR', 'RXDR'):
            return ('Any', RO, 'reserved in a read-only register: a write is ignored and changes nothing (no record, RUL-18 as amended)')
        return ('Zero', RES, 'reserved: must be kept at reset value')
    if r == 'CR1':
        return {
            'PECEN': ('Zero', T, '0 only: a stock tag; ' + SMBUS),
            'ALERTEN': ('Zero', T, '0 only: a stock tag; ' + SMBUS),
            'SMBDEN': ('Zero', T, '0 only: a stock tag; ' + SMBUS),
            'SMBHEN': ('Zero', T, '0 only: a stock tag; ' + SMBUS),
            'GCEN': ('Zero', T, '0 only: a stock tag; general call (target mode) is not modeled'),
            'WUPEN': ('Zero', T, '0 only: a stock tag; wake-up from Stop is not modeled'),
            'NOSTRETCH': ('Zero', I, '0 only: the stock model stores it; ' + TARGET + '; ' + PE % page),
            'SBC': ('Zero', T, '0 only: a stock tag; ' + TARGET),
            'RXDMAEN': ('Zero', I, '0 only: the stock model stores it and raises DmaReceive; the DMA side of I2C is not modeled (TVI-WO-1306 NOT)'),
            'TXDMAEN': ('Zero', T, '0 only: a stock tag; the DMA side of I2C is not modeled (TVI-WO-1306 NOT)'),
            'ANFOFF': ('Any', G, 'kept by the guard and served on read (a stock tag; ' + RULED + ' Q5); the twin\'s bus carries no noise to filter; '
                       + PE % page),
            'DNF': ('Any', G, 'kept by the guard and served on read (a stock tag; ' + RULED + ' Q5); the twin\'s bus carries no noise to filter; '
                    + PE % page),
            'ERRIE': ('Any', I, 'the stock model keeps it; no bus error (BERR, ARLO, OVR, TIMEOUT, PECERR, ALERT) is ever raised, so it never fires '
                      '(ArduPilot sets it; ' + RULED + ' Q11)'),
            'TCIE': ('Any', I, 'the stock model: TC and TCR raise the event interrupt'),
            'STOPIE': ('Any', I, 'the stock model: STOPF raises the event interrupt'),
            'NACKIE': ('Any', G, 'the guard: NACKF raises the event interrupt (Table 400 p1999; the stock model has NACKIE && false); ' + NACK),
            'ADDRIE': ('Zero', I, '0 only: the stock model stores it; ' + TARGET),
            'RXIE': ('Any', I, 'the stock model: RXNE on a read raises the event interrupt'),
            'TXIE': ('Any', I, 'the stock model: TXIS raises the event interrupt'),
            'PE': ('Any', G, 'the stock model; set (0 -> 1), the RCC guard\'s kernel-clock query (TVI-I2C-KERNEL; ' + RULED + ' Q3), and set as '
                   'the very next access after the one that cleared it is TVI-I2C-RESET, unmodeled (p1960: write 0, check, write 1; Q6); '
                   'cleared (1 -> 0), the software reset (p1959): the guard resets the owned stock instance (every ISR flag, TXE = 1, its '
                   'byte queues) and restores the configuration (CR1, OAR1, OAR2, CR2 but START and STOP; TIMINGR is its own) (Q6); '
                   'a write that clears it is judged as written with PE = 0 (Q2)'),
        }[f]
    if r == 'CR2':
        return {
            'PECBYTE': ('Zero', T, '0 only: a stock tag; ' + SMBUS),
            'AUTOEND': ('Any', I, 'the stock model: a STOP after NBYTES (STOPF)'),
            'RELOAD': ('Zero', I, '0 only: the stock model stores it and extends a transfer, used by no firmware (TVI-WO-1306.measure.md §2.1), '
                       'so held at 0, not audited (' + RULED + ' Q11)'),
            'NBYTES': ('Any', I, 'the stock model; a START with NBYTES = 0 and RELOAD = 0 is TVI-I2C-TRANSFER, unmodeled, never passed on: the '
                       'stock model hands the device an empty write and Renode aborts (measure.md §2.3 I8b; ' + RULED + ' Q8); ' + OPEN),
            'NACK': ('Zero', T, '0 only: a stock tag; ' + TARGET),
            'STOP': ('Any', G, 'the stock model: STOPF; it closes an open transfer; written while a NACKed address\'s automatic STOP is pending, '
                     'absorbed by the guard: silicon clears it "when a STOP condition is detected" (p2005; ' + RULED2 + ' Q4)'),
            'START': ('Any', G, 'the stock model starts the transfer; with PE = 0, or while a transfer is open, it is TVI-I2C-TRANSFER, '
                      'unmodeled (p2003 "Peripheral disabled"; ' + RULED + ' Q8); while a NACKed address\'s automatic STOP is pending it is TVI-I2C-TRANSFER, '
                      'unmodeled (' + RULED2 + ' Q4); ' + NACK),
            'HEAD10R': ('Zero', T, '0 only: a stock tag; 10-bit addressing is not modeled'),
            'ADD10': ('Zero', I, '0 only: the stock model stores it; 10-bit addressing is not modeled'),
            'RD_WRN': ('Any', I, 'the stock model; ' + OPEN),
            'SADD': ('Any', I, 'the stock model, 7-bit (SADD[7:1]; bits 9, 8 and 0 are don\'t care, p2006); ' + OPEN),
        }[f]
    if r in ('OAR1', 'OAR2'):
        return ('Zero', I, '0 only: the stock model stores it; ' + TARGET)
    if r == 'TIMINGR':
        return ('Any', G, 'kept by the guard and served on read: the stock model has every TIMINGR field as a tag that reads 0 (measure.md '
                '§2.3 I2; ' + RULED + ' Q5); the twin times nothing by it; ' + PE % page)
    if r == 'TIMEOUTR':
        return ('Zero', U, '0 only: the stock model has no register 0x14; SMBus timeouts are not modeled; reads 0')
    if r == 'ISR':
        if f == 'TXE':
            return ('Zero', I, '0 only: the stock model\'s TXE always reads 1 (it holds no TXDR), so a flush (1 written) is not modeled; set at PE = 0 (p2011)')
        if f == 'TXIS':
            return ('Zero', I, '0 only: writing 1 generates TXIS with NOSTRETCH = 1 only, ' + TARGET)
        if f == 'NACKF':
            return ('Any', G, 'read-only (a write is ignored), set and served by the guard: ' + NACK + '; cleared by NACKCF (p2012) and at '
                    'PE = 0 (p2011); a NACK in the data phase is not modeled: no device model in Renode NACKs a byte (' + RULED2 + ' Q7)')
        if f == 'STOPF':
            return ('Any', RO, 'read-only: a write is ignored; the stock model\'s, and the guard\'s one SCL period after a NACK\'s NACKF (inferred: '
                    'RM0433 gives no figure; ' + RULED2 + ' Q3)')
        return ('Any', RO, 'read-only: a write is ignored and changes nothing (no record, RUL-18 as amended)')
    if r == 'ICR':
        return {
            'ALERTCF': ('Zero', T, '0 only: a stock tag; ' + SMBUS),
            'TIMOUTCF': ('Zero', T, '0 only: a stock tag; ' + SMBUS),
            'PECCF': ('Zero', T, '0 only: a stock tag; ' + SMBUS),
            'OVRCF': ('Zero', T, '0 only: a stock tag; no bus error is modeled'),
            'ARLOCF': ('Zero', T, '0 only: a stock tag; no bus error is modeled'),
            'BERRCF': ('Zero', T, '0 only: a stock tag; no bus error is modeled'),
            'STOPCF': ('Any', I, 'the stock model clears STOPF; the guard clears the NACK\'s own STOPF (p2012)'),
            'NACKCF': ('Any', G, 'the guard clears NACKF (p2012); ' + NACK),
            'ADDRCF': ('Zero', I, '0 only: ' + TARGET + ' (in controller mode the stock model clears TXIS, where silicon clears ADDR and a pending START, p2012)'),
        }[f]
    if r == 'PECR':
        return ('Any', RO, 'read-only: reads 0 (PECEN 0 only); a write is ignored')
    if r == 'RXDR':
        return ('Any', RO, 'read-only: the stock model\'s received byte; a write is ignored')
    if r == 'TXDR':
        return ('Any', I, 'the stock model sends the byte; written with PE = 0 it is TVI-I2C-TRANSFER, unmodeled (' + RULED + ' Q8)')
    raise KeyError(reg + '.' + f)


# ICR's reserved bits split where ChibiOS writes 1 (bits 1, 2 and 6, its ISR flag masks; acknowledged by name, ruled Q10).
ICR_RES = {(7, 6): [(7, 7, 'Zero'), (6, 6, 'Any')], (2, 0): [(2, 1, 'Any'), (0, 0, 'Zero')]}
CHIBI = ('ChibiOS writes 1 here (ICR = 0x2, 0x4 or 0x40, the TXIS, RXNE and TC positions of ISR, at every ICR write it makes, '
         'TVI-WO-1306.measure.md §2.1); RM0433 marks the bits reserved, "must be kept at reset value" (p2012); a clear register: the '
         'write clears nothing (' + RULED + ' Q10)')


def esc(s):
    return s.replace('\\', '\\\\').replace('"', '\\"')


rows = [l.rstrip('\n').split('\t') for l in open(sys.argv[1]) if not l.startswith('#') and not l.startswith('register\t')]
nres = {}
for r in rows:
    nres[r[0]] = nres.get(r[0], 0) + (r[3] == 'Reserved')
for reg, off, sec, f, msb, lsb, page, line in rows:
    msb, lsb, page = int(msb), int(lsb), int(page)
    rname = f if f != 'Reserved' or nres[reg] == 1 else ('Res%d' % msb if msb == lsb else 'Res%d_%d' % (msb, lsb))
    parts = [(msb, lsb, rname, None)]
    if reg == 'I2C_ICR' and f == 'Reserved':
        parts = [(a, b, 'Res%d' % a if a == b else 'Res%d_%d' % (a, b), sym) for a, b, sym in ICR_RES.get((msb, lsb), [(msb, lsb, None)])] \
            if (msb, lsb) in ICR_RES else [(msb, lsb, 'Reserved', None)]
    for a, b, name, sym in parts:
        if sym == 'Any':
            allowed, c, behaves = 'Any', I, CHIBI
        else:
            allowed, c, behaves = cls(reg, 'Reserved' if name.startswith('Res') else name, a, b, page)
        print('            R("%s", 0x%03X, "%s", "%s", %d, %d, %s, "%s", "%s", %d),' % (reg, int(off, 16), sec, name, a, b, allowed, esc(c), esc(behaves), page))
