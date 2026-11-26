#!/usr/bin/env python3
"""TVI-WO-1304: the flash guard's Fields table (RUL-16, RUL-17), generated, never typed: from RM0433 Rev 9's FLASH register
fields (tests/1304/data/rm0433-rev9-flash-fields.tsv) and the classes ruled 2026-11-24T15:50Z (Q1-Q5). Prints the C#
`R(...)` rows of model/GuardedSTM32H7_Flash.cs's `Fields`; tests/1304/flash.sh `regen` checks them byte for byte.
Usage: python3 -I tests/1304/fields.py tests/1304/data/rm0433-rev9-flash-fields.tsv"""
import re, sys

G, I, RES, RO, T, U = ('modeled by the guard', 'inherited, unaudited (acknowledged by name)', 'reserved', 'read-only', 'tag',
                       'undefined in the stock model')
RULED = 'ruled 2026-11-24T15:50Z'
BANK1 = '0 only: bank 1 programming and erase are not modeled (TVI-WO-1304 (2), outside the allow-list); a store into bank 1, which silicon rejects with PGSERR1 while PG1 = 0 (p164), is not seen by the guard (a row)'


def cls(reg, f, page):
    """(allowed symbol, class, behaves) for one field."""
    bank = reg[-1] if reg[-1] in '12' else ''
    base = re.sub(r'[12]$', '', f)
    if f == 'Reserved':
        if reg.startswith('FLASH_CCR'):
            return ('Any', I, 'ArduPilot writes 1 here (CCRx = 0xFFFFFFFF, TVI-WO-1304.measure.md §2.1); RM0433 says "must be kept at reset value"; ignored')
        if reg == 'FLASH_OPTSR_CUR':
            return ('Any', RO, 'reserved: served as the declared optionBytes image has it (the board\'s 0x1BC6AAF0 and the consumer\'s 0x0406AAFC set reserved bits), never invented; a write is ignored (read-only, p212-214)')
        if reg.startswith('FLASH_SR'):
            return ('Any', RO, 'reserved in a read-only register: a write is ignored and changes nothing (no record, RUL-18 as amended); reads 0')
        return ('Zero', RES, 'reserved: dropped, reads 0')
    if reg == 'FLASH_ACR':
        if f == 'LATENCY':
            return ('Latencies', G, 'kept and served, also at its bank 2 alias 0x100 (p201); TVI-FLASH-WAIT: wait states below Table 17\'s for the AXI clock at the voltage scale in force are a predicted-fault, at the write (ACR or RCC) that makes them so (p159-161); the scale is the PWR guard\'s where the platform has one (TVI-WO-1308, ruled 2026-11-26T11:06Z Q21), otherwise PWR_D3CR read through the bus: where it reads VOS 00, decided at VOS1 and named in the record, the most permissive scale that can be known, so a mismatch only at VOS2/VOS3 passes, acknowledged (' + RULED + ' Q1): that is the stock stub, which drops every write, and also a firmware that writes VOS 00 itself on a pwr that stores it ("Reserved (Scale 3 selected)", p310), which the guard cannot tell from the stub without a name; a VOS write is not seen, so a mismatch it makes is judged at the next ACR or RCC clock write, or never; an AXI clock above 225 MHz is unmodeled (VOS0\'s ODEN cannot be read); 8-15 not modeled (p201 lists 0-7)')
        return ('Any', G, 'kept and served, also at 0x100 (p201); TVI-FLASH-PROG: at a program or erase start, a value below Table 17\'s for the AXI clock at the voltage scale is a predicted-fault ("too tight", p164; p160); 11 read as at least as lax as 10 is inferred, no RM page: it is the reset value (p201), and every ArduPilot H7 programs flash at it, field evidence, not a page (' + RULED + ' Q2)')
    if reg.startswith('FLASH_KEYR'):
        return ('Any', G, 'the unlock keys (p182): KEY1 then KEY2 unlock FLASH_CR' + bank + '; a wrong key, a key while unlocked, a third key, another bank ' + bank + ' register written between the two, or a key of less than 32 bits is TVI-FLASH-KEY, a predicted-fault (locked until reset with a bus error, p182, p163); write-only: a read is TVI-FLASH-READ')
    if reg.startswith('FLASH_CR'):
        if f == 'LOCK' + bank:
            return ('Any', G, 'the stock model\'s lock (p' + str(page) + '); a CR' + bank + ' write that changes another field while locked is TVI-FLASH-LOCK, a predicted-fault ("can be programmed only when LOCK' + bank + ' is cleared", p182: not allowed after reset); silicon ignores it')
        if f == 'PSIZE' + bank:
            return ('Any', G, 'kept and served as written (p' + str(page) + '; the stock model\'s tag reads 11, TVI-WO-1304.measure.md F7); no value is an error: "There is no hardware limitation on programming parallelism" (Table 18 p164; ' + RULED + ' Q4, Q5)')
        if f == 'SPSS2':
            return ('Zero', U, '0 only: the special sector selection is not modeled (the stock model has no bit 14)')
        if f == 'CRC_EN':
            return ('Zero', T, '0: a stock tag; CRC computation is not modeled (TVI-WO-1304 (2), outside the allow-list)')
        if base.endswith('IE'):
            return ('Zero', I, '0 only: the flash interrupts are not audited')
        if bank == '1':
            if base == 'SNB':
                return ('Any', I, 'the stock model: stored; used only by a bank 1 sector erase, which is not modeled (SER1 0 only)')
            return ('Zero', I, BANK1)
        return {
            'PG': ('Any', I, 'the stock model: its program-write hook (EOP2 after a 32-byte flash word, TVI-WO-1304.measure.md F3); TVI-FLASH-PROG at its set; set with SER2 still set passes, RM0433 stating nothing (ArduPilot does it; ' + RULED + ' Q4); a store into bank 2 with PG2 = 0, which silicon rejects with PGSERR2 (p164), is not seen by the guard (a row, ' + RULED + ' Q4)'),
            'SER': ('Any', I, 'the stock model: a sector erase at START2'),
            'BER': ('Zero', I, '0 only: a bank erase is not audited'),
            'START': ('Any', I, 'the stock model: the erase runs at once and sets EOP2 (TVI-WO-1304.measure.md F2, F8: no erase time, so BSY2 and QW2 are never 1; a row, ' + RULED + ' Q4); TVI-FLASH-PROG at it'),
            'FW': ('Zero', I, '0 only: the force-write is not audited'),
            'SNB': ('Any', I, 'the stock model: the sector a sector erase sets to 0xFF (128 KiB)'),
        }[base]
    if reg.startswith('FLASH_SR'):
        what = {
            'EOP': 'reads the stock model\'s: set at the end of an erase, a program or an option byte change (p200), cleared by CLR_EOP',
            'BSY': 'reads 0: erase and program time are not modeled (' + RULED + ' Q4)',
            'QW': 'reads 0: erase and program time are not modeled (' + RULED + ' Q4)',
            'CRC_BUSY': 'reads 0: CRC computation is not modeled',
            'WBNE': 'reads the stock model\'s: set while a flash word is partly written with PG set',
        }.get(base, 'reads the stock model\'s: set only by the stock model\'s own checks')
        return ('Any', RO, 'read-only (p' + str(page) + '): a write is ignored and changes nothing (no record, RUL-18 as amended; the stock model\'s EOP is writable, TVI-WO-1304.measure.md F5); ' + what)
    if reg.startswith('FLASH_CCR'):
        flag = base[4:]
        if flag in ('EOP', 'PGSERR', 'INCERR', 'OPERR', 'SNECCERR', 'DBECCERR'):
            return ('Any', I, 'the stock model clears ' + flag + bank + ' (p' + str(page) + ')')
        return ('Any', G, flag + bank + ' is a stock tag reading 0, never set in the twin, so its clear changes nothing (p' + str(page) + ')')
    if reg == 'FLASH_OPTSR_CUR':
        return ('Any', RO, 'served from the declared optionBytes image, also at 0x11C, never the stock model\'s reset value (' + RULED + ' Q3); an undeclared read is TVI-FLASH-OPTSR; read-only: a write is ignored (p212-214)')
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
