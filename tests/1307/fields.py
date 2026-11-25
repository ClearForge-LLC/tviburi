#!/usr/bin/env python3
"""TVI-WO-1307: the ADC guard's Fields table (RUL-16, RUL-17), generated, never typed: from RM0433 Rev 9's ADC register fields
(tests/1307/data/rm0433-rev9-adc-fields.tsv, written by tests/1307/extract.py) and the classes ruled 2026-11-25T21:17Z (Q1-Q14).
Prints the C# `R(...)` rows of model/GuardedSTM32H7_ADC.cs's `Fields`; tests/1307/adc.sh `regen` checks them byte for byte.
ADC_CR is printed per silicon revision (p1003-1011); the table carries rev V's rows as ADC_CR (one register per offset, the
release's rule), BOOST's rev Y reading in its text. Usage: python3 -I tests/1307/fields.py tests/1307/data/rm0433-rev9-adc-fields.tsv"""
import sys

G, RES, RO = 'modeled by the guard', 'reserved', 'read-only'
RULED = 'ruled 2026-11-25T21:17Z'
STORED = 'stored and served on read; '
ADSTART0 = 'written with a change while ADSTART = 1 it is TVI-ADC-STATE, unmodeled (p934; ' + RULED + ' Q5)'
NOT = '0 only: '
INJ = NOT + 'the injected group is not modeled (' + RULED + ' Q12)'
AWD = NOT + 'the analog watchdogs are not modeled (' + RULED + ' Q12)'
OVS = NOT + 'oversampling and the data shift are not modeled; data are right-aligned (' + RULED + ' Q12)'
DUAL = NOT + 'dual mode is not modeled: ADC2 is never started (' + RULED + ' Q1, Q12)'
INERT = 'stored and served on read; used by no modeled path while every watchdog enable is 0 (' + RULED + ' Q12)'

CR = {
    'ADCAL': ('Any', G, 'calibration: stays 1 for t_CAL = 165,010 (ADCALLIN = 1) or t_OFF_CAL = 1,280 (ADCALLIN = 0) ADC clock cycles '
              '(DS12117 p167, p273); set only with ADEN = 0, DEEPPWD = 0, ADVREGEN = 1 and the regulator started, else TVI-ADC-STATE (p1003, '
              'p928; ' + RULED + ' Q5)'),
    'ADCALDIF': ('Zero', G, NOT + 'differential calibration is not modeled (' + RULED + ' Q12)'),
    'DEEPPWD': ('Any', G, STORED + 'resets to 1 (p1003); changed only with the ADC disabled, else TVI-ADC-STATE (' + RULED + ' Q5)'),
    'ADVREGEN': ('Any', G, STORED + 'the regulator ready (LDORDY on rev V) 10 us after it is set with DEEPPWD = 0 (t_ADCVREG_STUP max, '
                 'DS12117); changed only with the ADC disabled (p1003; ' + RULED + ' Q5)'),
    'ADCALLIN': ('Any', G, STORED + 'chooses the calibration time and sets LINCALRDYW1-6 at its end (p931); changed only with the ADC '
                 'disabled (' + RULED + ' Q5)'),
    'BOOST': ('Any', G, STORED + 'rev V BOOST[1:0] (p1009), rev Y BOOST bit 8 with bit 9 reserved (p1005); checked against f_ADC at '
              'ADSTART (TVI-ADC-RATE), ArduPilot\'s 01 where rev V asks 10 acknowledged by name (' + RULED + ' Q7)'),
    'JADSTP': ('Zero', G, INJ),
    'ADSTP': ('Any', G, 'stops the regular conversions; set only with ADSTART = 1 and ADDIS = 0 (p1006; ' + RULED + ' Q5)'),
    'JADSTART': ('Zero', G, INJ),
    'ADSTART': ('Any', G, 'starts the regular conversions, paced at f_ADC (ruled Q4); set only with ADEN = 1 and ADDIS = 0 (p1007); '
                'TVI-ADC-KERNEL, -RATE, -PCSEL and -DMNGT judge it (' + RULED + ' Q3-Q8)'),
    'ADDIS': ('Any', G, 'clears ADEN and ADSTART at once; set only with ADEN = 1 and ADSTART = 0 (p1006; ' + RULED + ' Q5)'),
    'ADEN': ('Any', G, 'ADRDY one conversion cycle later (t_STAB, DS12117); set only with every command bit 0, ADVREGEN = 1 and the '
             'regulator started (p1007, p1011, p933; ' + RULED + ' Q5)'),
}
for i in range(1, 7):
    CR['LINCALRDYW%d' % i] = ('Any', G, STORED + '0 from reset, set by a calibration with ADCALLIN = 1 (p931); a toggle (the linearity '
                              'factor\'s word access) is TVI-ADC-STATE, unmodeled (' + RULED + ' Q5)')

CFGR = {
    'JQDIS': ('Any', G, STORED + 'used by no modeled path: the injected group is 0 only (' + RULED + ' Q12)'),
    'AWD1CH': ('Channels', G, INERT), 'AWD1SGL': ('Any', G, INERT), 'AWD1EN': ('Zero', G, AWD), 'JAWD1EN': ('Zero', G, INJ),
    'JAUTO': ('Zero', G, INJ), 'JQM': ('Zero', G, INJ), 'JDISCEN': ('Zero', G, INJ),
    'DISCNUM': ('Any', G, STORED + 'used by no modeled path while DISCEN = 0 (' + RULED + ' Q12)'),
    'DISCEN': ('Zero', G, NOT + 'discontinuous mode is not modeled (' + RULED + ' Q12)'),
    'AUTDLY': ('Zero', G, NOT + 'the delayed conversion mode is not modeled (' + RULED + ' Q12)'),
    'CONT': ('Any', G, 'continuous: the sequence restarts at EOS (p937); single: ADSTART cleared at EOS (p1007); ' + ADSTART0),
    'OVRMOD': ('Any', G, 'OVR when a conversion ends with EOC set (p999): 0 keeps DR, 1 overwrites it (p1015); the eight-stage FIFO '
               '(p962) is not modeled, acknowledged by name (' + RULED + ' Q9); ' + ADSTART0),
    'EXTEN': ('Zero', G, NOT + 'hardware triggers are not modeled: no trigger line reaches the ADC (' + RULED + ' Q12)'),
    'EXTSEL': ('Any', G, STORED + 'used by no modeled path while EXTEN = 0 (' + RULED + ' Q12)'),
    'RES': ('Any', G, 'the resolution and the SAR time (Table 214 p955), by the declared revision; a code it reserves is TVI-ADC-RATE at '
            'ADSTART (p1016; ' + RULED + ' Q2); ' + ADSTART0),
    'DMNGT': ('Any', G, '00 DR only; 01 and 11 a DMA request per conversion, 01 stopping at the DMA\'s last transfer (p964-965); 10 '
              '(DFSDM) is TVI-ADC-DMNGT at ADSTART (p1016; ' + RULED + ' Q8); ' + ADSTART0),
}
ISR = 'set by the guard\'s conversion; cleared by writing 1 (p998-1000)'
INJF = 'never set: the injected group is not modeled; writing 1 changes nothing (' + RULED + ' Q12)'
AWDF = 'never set: the analog watchdogs are not modeled; writing 1 changes nothing (' + RULED + ' Q12)'
ISRS = {
    'LDORDY': ('Any', RO, 'rev V: set 10 us after ADVREGEN (t_ADCVREG_STUP max, DS12117); read-only, a write is ignored; reserved and 0 on '
               'rev Y (p998; ' + RULED + ' Q5)'),
    'JQOVF': ('Any', G, INJF), 'AWD3': ('Any', G, AWDF), 'AWD2': ('Any', G, AWDF), 'AWD1': ('Any', G, AWDF), 'JEOS': ('Any', G, INJF),
    'JEOC': ('Any', G, INJF),
    'OVR': ('Any', G, 'set when a conversion ends with EOC still set (p999); DMA requests blocked until it is cleared (p964; ' + RULED + ' Q9)'),
    'EOS': ('Any', G, ISR + '; set at the sequence\'s last conversion (p955)'),
    'EOC': ('Any', G, ISR + '; set at each conversion, also cleared by reading DR (p1000)'),
    'EOSMP': ('Any', G, ISR + '; set with EOC, not the SAR time earlier (EOSMPIE is 0 only; ' + RULED + ' Q12)'),
    'ADRDY': ('Any', G, ISR + '; set t_STAB after ADEN (DS12117)'),
}
IER = {'ADRDYIE': 'Any', 'EOCIE': 'Any', 'EOSIE': 'Any', 'OVRIE': 'Any'}


def cls(reg, f, msb, lsb, page):
    """(allowed symbol, class, behaves) for one field."""
    if f == 'Reserved':
        if reg in ('ADC_DR', 'ADCx_CSR', 'ADCx_CDR', 'ADCx_CDR2') or reg.startswith('ADC_JDR'):
            return ('Any', RO, 'reserved in a read-only register: a write is ignored and changes nothing (no record, RUL-18 as amended)')
        return ('Zero', RES, 'reserved: must be kept at reset value')
    if reg == 'ADC_CR':
        return CR[f]
    if reg == 'ADC_CFGR':
        return CFGR[f]
    if reg == 'ADC_ISR':
        return ISRS[f]
    if reg == 'ADC_IER':
        if f in IER:
            return ('Any', G, 'the interrupt line (the entry\'s nvic or exti input) = OR of ADRDY, EOC, EOS, OVR and their enables (p1001-1002)')
        return ('Zero', G, NOT + 'its flag is never set, or set without its timing (EOSMP) (' + RULED + ' Q12)')
    if reg == 'ADC_CFGR2':
        return ('Zero', G, OVS)
    if reg in ('ADC_SMPR1', 'ADC_SMPR2'):
        return ('Any', G, 'the sampling time per channel: 1.5-810.5 ADC clock cycles (p1019-1020); ' + ADSTART0)
    if reg == 'ADC_PCSEL':
        return ('Any', G, STORED + 'a sequenced channel not preselected is TVI-ADC-PCSEL at ADSTART (p935, p1021); ' + ADSTART0)
    if reg in ('ADC_LTR1', 'ADC_HTR1', 'ADC_LTR2', 'ADC_HTR2', 'ADC_LTR3', 'ADC_HTR3'):
        return ('Any', G, INERT)
    if reg.startswith('ADC_SQR'):
        if f == 'L':
            return ('Any', G, 'the sequence length (p1023); ' + ADSTART0)
        return ('Channels', G, 'the sequence: a channel 0-19 (p1023-1026); ' + ADSTART0)
    if reg == 'ADC_DR':
        return ('Any', RO, 'the last conversion, right-aligned (p1027); reading it clears EOC; holding an undeclared channel\'s conversion the '
                'read is TVI-ADC-LEVEL (' + RULED + ' Q10); a half-word read by ArduPilot\'s DMA (DMNGT = 11) acknowledged by name (p926; '
                + RULED + ' Q14)')
    if reg == 'ADC_JSQR' or reg.startswith('ADC_OFR'):
        return ('Zero', G, INJ if reg == 'ADC_JSQR' else NOT + 'the offsets are not modeled (' + RULED + ' Q12)')
    if reg.startswith('ADC_JDR'):
        return ('Any', RO, 'reads 0: the injected group is not modeled (' + RULED + ' Q12)')
    if reg in ('ADC_AWD2CR', 'ADC_AWD3CR'):
        return ('Zero', G, AWD)
    if reg == 'ADC_DIFSEL':
        return ('Zero', G, NOT + 'differential inputs are not modeled (' + RULED + ' Q12)')
    if reg in ('ADC_CALFACT', 'ADC_CALFACT2'):
        return ('Zero', G, NOT + 'reads 0: the calibration factors are not modeled; a calibration only takes its time (' + RULED + ' Q5, Q12)')
    if reg == 'ADCx_CSR':
        return ('Any', RO, 'the master\'s ISR flags (p1036); the slave\'s read 0: ADC2 is not modeled')
    if reg in ('ADCx_CDR', 'ADCx_CDR2'):
        return ('Any', RO, 'dual-mode data: a read is TVI-ADC-READ, unmodeled (' + RULED + ' Q12)')
    if reg == 'ADCx_CCR':
        if f in ('VBATEN', 'TSEN', 'VREFEN'):
            return ('Any', G, STORED + 'the internal channels\' enable (ADC3: VBAT/4 17, VSENSE 18, VREFINT 19, p920); written only with the '
                    'ADCs disabled (p1038), but ArduPilot\'s ADC3 pattern (these bits with ADEN = 1, ADSTART = 0) acknowledged by name (' + RULED + ' Q6)')
        if f == 'PRESC':
            return ('Prescalers', G, 'divides the kernel clock when CKMODE = 00 (p1039); 1100-1111 reserved; written only with the ADCs disabled (' + RULED + ' Q5)')
        if f == 'CKMODE':
            return ('Any', G, '00: the kernel clock ADCSEL selects (p419) / PRESC; 01/10/11: rev Y adc_hclk, rev V adc_sclk, /1/2/4; rev V /2 '
                    'again (p920, p1039); written only with the ADCs disabled (' + RULED + ' Q2-Q5)')
        return ('Zero', G, DUAL)
    raise SystemExit('fields.py: no class for %s.%s' % (reg, f))


rows = [l.rstrip('\n').split('\t') for l in open(sys.argv[1], encoding='utf-8') if not l.startswith('#')][1:]
rows = [['ADC_CR' if r[0] == 'ADC_CR_RevV' else r[0]] + r[1:] for r in rows if r[0] != 'ADC_CR_RevY']
nres = {}
for r in rows:
    nres[r[0]] = nres.get(r[0], 0) + (r[3] == 'Reserved')
for reg, off, sec, f, msb, lsb, page, _ in rows:
    allowed, c, behaves = cls(reg, f, int(msb), int(lsb), int(page))
    # A register's reserved ranges each get a name of their own (one ledger key each), as tests/1306/fields.py names them.
    name = f if f != 'Reserved' or nres[reg] == 1 else ('Res%s' % msb if msb == lsb else 'Res%s_%s' % (msb, lsb))
    print('            R("%s", 0x%03X, "%s", "%s", %s, %s, %s, "%s", "%s", %s),' % (reg, int(off, 16), sec, name, msb, lsb, allowed, c,
                                                                                    behaves.replace('"', '\\"'), page))
