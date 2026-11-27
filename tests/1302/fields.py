#!/usr/bin/env python3
"""TVI-WO-1302: the timer guard's Fields table (RUL-16, RUL-17), generated, never typed: from RM0433 Rev 9's TIM1/TIM8
register fields (tests/1302/data/rm0433-rev9-tim18-fields.tsv: §38.4's drawings, every register's bits checked to cover
31:0 or 15:0 once, pages as printed) and the classes ruled 2026-11-24T06:10Z (Q1, Q2, Q4, Q5, Q7). Prints the C# `R(...)`
rows of model/GuardedSTM32_Timer.cs's `Fields`; tests/1302/timer.sh `regen` checks them byte for byte.
Usage: python3 -I tests/1302/fields.py tests/1302/data/rm0433-rev9-tim18-fields.tsv"""
import re, sys
# TVI-WO-1206 readings BEGIN (spike/1206/apply.py writes this block; spike/1206/readings.py derives it)
READ_1206 = {
    ('TIMx_CCMR1', 'CC1S[1:0]'): ('acknowledged by name', '00 only: the channel is an output; input capture is not audited; read for 0x0: written 0 only, the mode the timer runs in on both sides: outputs (CCxS = 00), edge-aligned upcounting, updates enabled, no input XOR, no trigger interrupt (the guard holds SMCR.SMS at 0 and refuses EGR.TG) (TVI-WO-1206 A7; p1603; STM32_Timer.cs L429; STM32_Timer.cs L720)'),
    ('TIMx_CCMR1', 'CC2S[1:0]'): ('acknowledged by name', '00 only: the channel is an output; input capture is not audited; read for 0x0: written 0 only, the mode the timer runs in on both sides: outputs (CCxS = 00), edge-aligned upcounting, updates enabled, no input XOR, no trigger interrupt (the guard holds SMCR.SMS at 0 and refuses EGR.TG) (TVI-WO-1206 A7; p1601; STM32_Timer.cs L429; STM32_Timer.cs L720)'),
    ('TIMx_CCMR2', 'CC3S[1:0]'): ('acknowledged by name', '00 only: the channel is an output; input capture is not audited; read for 0x0: written 0 only, the mode the timer runs in on both sides: outputs (CCxS = 00), edge-aligned upcounting, updates enabled, no input XOR, no trigger interrupt (the guard holds SMCR.SMS at 0 and refuses EGR.TG) (TVI-WO-1206 A7; p1604; STM32_Timer.cs L429; STM32_Timer.cs L720)'),
    ('TIMx_CCMR2', 'CC4S[1:0]'): ('acknowledged by name', '00 only: the channel is an output; input capture is not audited; read for 0x0: written 0 only, the mode the timer runs in on both sides: outputs (CCxS = 00), edge-aligned upcounting, updates enabled, no input XOR, no trigger interrupt (the guard holds SMCR.SMS at 0 and refuses EGR.TG) (TVI-WO-1206 A7; p1604; STM32_Timer.cs L429; STM32_Timer.cs L720)'),
    ('TIMx_CR1', 'ARPE'): ("stock, wrong (with ARPE = 1 the stock model loads a new ARR only at overflow, not at UG (p1531, p1533); unaffected: every ARR change is written with CR1 = 0 (ARPE = 0, counter stopped) and followed by UG: ArduPilot's two configurations (0.104 s, 7.77 s) and every generated boot's and the fixture's (spike/1206/data/main-8e15435/gen-writes.tsv); later ARR writes repeat the value in force)", 'the stock model: ARR preload (STM32_Timer.cs L164, L365); not probed (both firmwares write 1); read for 0x0,0x1 (TVI-WO-1206; p1531, p1533, p1588; STM32_Timer.cs L164; STM32_Timer.cs L363-368)'),
    ('TIMx_CR1', 'CMS[1:0]'): ('acknowledged by name', '00 only: centre-aligned modes are not audited; read for 0x0: written 0 only, the mode the timer runs in on both sides: outputs (CCxS = 00), edge-aligned upcounting, updates enabled, no input XOR, no trigger interrupt (the guard holds SMCR.SMS at 0 and refuses EGR.TG) (TVI-WO-1206 A7; p1588; STM32_Timer.cs L163; STM32_Timer.cs L157)'),
    ('TIMx_CR1', 'DIR'): ('acknowledged by name', '0 only: downcounting is not audited; read for 0x0: written 0 only, the mode the timer runs in on both sides: outputs (CCxS = 00), edge-aligned upcounting, updates enabled, no input XOR, no trigger interrupt (the guard holds SMCR.SMS at 0 and refuses EGR.TG) (TVI-WO-1206 A7; p1589; STM32_Timer.cs L156-162; STM32_Timer.cs L26)'),
    ('TIMx_CR1', 'UDIS'): ('acknowledged by name', "0 only: the stock model's update-disable path is not audited; read for 0x0: written 0 only, the mode the timer runs in on both sides: outputs (CCxS = 00), edge-aligned upcounting, updates enabled, no input XOR, no trigger interrupt (the guard holds SMCR.SMS at 0 and refuses EGR.TG) (TVI-WO-1206 A7; p1531, p1589; STM32_Timer.cs L153; STM32_Timer.cs L52-55)"),
    ('TIMx_CR1', 'URS'): ('stock, wrong (with URS = 0 a UG sets UIF on silicon (p1597); the stock model sets it only with UIE = 1; unaffected: no firmware reads TIMx_SR: 0 TIM1 reads in the armed run, no SR read in the generated boots or the fixture (spike/1206/data: the armed run (main-8e15435/reads.tsv), the 14 generated boots and the fixture (gen-reads.tsv)); with DIER = 0 no interrupt or DMA request follows on either side)', 'the stock model: stored, gates the update interrupt of UG (STM32_Timer.cs L283); not probed (ArduPilot writes 1); read for 0x0,0x1 (TVI-WO-1206; p1533, p1589, p1597; STM32_Timer.cs L154; STM32_Timer.cs L283-287)'),
    ('TIMx_CR2', 'TI1S'): ('acknowledged by name', '0 only: the input XOR is not audited; read for 0x0: written 0 only, the mode the timer runs in on both sides: outputs (CCxS = 00), edge-aligned upcounting, updates enabled, no input XOR, no trigger interrupt (the guard holds SMCR.SMS at 0 and refuses EGR.TG) (TVI-WO-1206 A7; p1591; STM32_Timer.cs L175; STM32_Timer.cs L505)'),
    ('TIMx_DIER', 'CC1IE'): ('stock, wrong (with CC1IE = 0 silicon still sets CC1IF at a compare match (p1597); the stock model sets it only with the enable; unaffected: no firmware reads TIMx_SR (spike/1206/data: the armed run (main-8e15435/reads.tsv), the 14 generated boots and the fixture (gen-reads.tsv)); the interrupt is off on both sides)', 'the stock model: interrupt enables (STM32_Timer.cs L215-233); with CCxE = 0 a running channel no longer reaches the pin (the guard gates it); read for 0x0 (TVI-WO-1206; p1595, p1597; STM32_Timer.cs L217; STM32_Timer.cs L132-134)'),
    ('TIMx_DIER', 'CC2IE'): ('stock, wrong (with CC2IE = 0 silicon still sets CC2IF at a compare match (p1597); the stock model sets it only with the enable; unaffected: no firmware reads TIMx_SR (spike/1206/data: the armed run (main-8e15435/reads.tsv), the 14 generated boots and the fixture (gen-reads.tsv)); the interrupt is off on both sides)', 'the stock model: interrupt enables (STM32_Timer.cs L215-233); with CCxE = 0 a running channel no longer reaches the pin (the guard gates it); read for 0x0 (TVI-WO-1206; p1595, p1597; STM32_Timer.cs L218; STM32_Timer.cs L132-134)'),
    ('TIMx_DIER', 'CC3IE'): ('stock, wrong (with CC3IE = 0 silicon still sets CC3IF at a compare match (p1597); the stock model sets it only with the enable; unaffected: no firmware reads TIMx_SR (spike/1206/data: the armed run (main-8e15435/reads.tsv), the 14 generated boots and the fixture (gen-reads.tsv)); the interrupt is off on both sides)', 'the stock model: interrupt enables (STM32_Timer.cs L215-233); with CCxE = 0 a running channel no longer reaches the pin (the guard gates it); read for 0x0 (TVI-WO-1206; p1595, p1597; STM32_Timer.cs L219; STM32_Timer.cs L132-134)'),
    ('TIMx_DIER', 'CC4IE'): ('stock, wrong (with CC4IE = 0 silicon still sets CC4IF at a compare match (p1597); the stock model sets it only with the enable; unaffected: no firmware reads TIMx_SR (spike/1206/data: the armed run (main-8e15435/reads.tsv), the 14 generated boots and the fixture (gen-reads.tsv)); the interrupt is off on both sides)', 'the stock model: interrupt enables (STM32_Timer.cs L215-233); with CCxE = 0 a running channel no longer reaches the pin (the guard gates it); read for 0x0 (TVI-WO-1206; p1595, p1597; STM32_Timer.cs L220; STM32_Timer.cs L132-134)'),
    ('TIMx_DIER', 'TIE'): ('acknowledged by name', 'the stock model: interrupt enables (STM32_Timer.cs L215-233); with CCxE = 0 a running channel no longer reaches the pin (the guard gates it); read for 0x0: written 0 only, the mode the timer runs in on both sides: outputs (CCxS = 00), edge-aligned upcounting, updates enabled, no input XOR, no trigger interrupt (the guard holds SMCR.SMS at 0 and refuses EGR.TG) (TVI-WO-1206 A7; p1595, p1597; STM32_Timer.cs L222; STM32_Timer.cs L656)'),
    ('TIMx_DIER', 'UIE'): ('stock, wrong (with UIE = 0 silicon still sets UIF at an update event (p1597); the stock model does not at UG; unaffected: no firmware reads TIMx_SR (spike/1206/data: the armed run (main-8e15435/reads.tsv), the 14 generated boots and the fixture (gen-reads.tsv)); the interrupt is off on both sides)', 'the stock model: interrupt enables (STM32_Timer.cs L215-233); with CCxE = 0 a running channel no longer reaches the pin (the guard gates it); read for 0x0 (TVI-WO-1206; p1595, p1597; STM32_Timer.cs L216; STM32_Timer.cs L86)'),
    ('TIMx_PSC', 'PSC[15:0]'): ("stock, wrong (the stock model applies a new PSC at the write, where silicon loads it at the next update event (p1531, p1609); unaffected: every PSC write is made with the counter stopped and followed by UG, which loads it on silicon: ArduPilot's two configurations and every generated boot's and the fixture's (spike/1206/data/main-8e15435/gen-writes.tsv))", 'the stock model: CK_CNT = f / (PSC + 1) (p1609); probed (T1, the control); read for 0x0,0x3f,0xc7 (TVI-WO-1206; p1531, p1533, p1609; STM32_Timer.cs L349; STM32_Timer.cs L351-356)'),
}
# TVI-WO-1206 readings END

tsv = sys.argv[1]
SEC = {0x00: '§38.4.1', 0x04: '§38.4.2', 0x08: '§38.4.3', 0x0C: '§38.4.4',
       0x10: '§38.4.5', 0x14: '§38.4.6', 0x18: '§38.4.8', 0x1C: '§38.4.10',
       0x20: '§38.4.11', 0x24: '§38.4.12', 0x28: '§38.4.13', 0x2C: '§38.4.14',
       0x30: '§38.4.15', 0x34: '§38.4.16', 0x38: '§38.4.17', 0x3C: '§38.4.18',
       0x40: '§38.4.19', 0x44: '§38.4.20', 0x48: '§38.4.21', 0x54: '§38.4.23',
       0x58: '§38.4.24', 0x5C: '§38.4.25', 0x60: '§38.4.26, §38.4.28',
       0x64: '§38.4.27, §38.4.29', 0x68: '§38.4.30, §38.4.31'}

G, T, RES, RO, U, I = ('modeled by the guard', 'tag', 'reserved', 'read-only', 'undefined in the stock model',
                       'inherited, unaudited (acknowledged by name)')
RULED = 'ruled 2026-11-24T06:10Z'
HIZ = ('a channel output silicon does not drive (Hi-Z, Table 337 p1608) is driven low: every committed motor pin has '
       'a fitted 442 kOhm pull-down (Aethelgard 65c74788, 4300j), so low is silicon\'s level there; an open link would '
       'float, which the twin cannot see (' + RULED + ' Q4)')


def sw(detail):
    return 'stock, wrong (' + detail + ')'


def cls(reg, f, msb, lsb, reset):
    """(allowed symbol or literal, class, behaves) for one field."""
    base = re.sub(r'\[.*', '', f)
    n = re.search(r'(\d)', base)
    ch = n.group(1) if n else ''
    if f == 'Reserved':
        return ('Zero', RES, 'reserved: dropped, reads 0')
    if reg == 'TIMx_CR1':
        return {
            'CEN': ('Any', sw('the first PWM-mode-1 rise comes one period after CEN, where silicon\'s OCxREF is high from CEN while CNT < CCRx (p1558); kept as the stock model\'s, coordinated with Aethelgard\'s motor witness, which predicts this convention (aethelgard-twin/src/timer.rs L6-24; ' + RULED + ' Q1)'), 'the stock model: starts and stops the counter (probed, TVI-WO-1302.measure.md T1)'),
            'UDIS': ('Zero', I, '0 only: the stock model\'s update-disable path is not audited'),
            'URS': ('Any', I, 'the stock model: stored, gates the update interrupt of UG (STM32_Timer.cs L283); not probed (ArduPilot writes 1)'),
            'OPM': ('Zero', I, '0 only: one-pulse mode is not audited'),
            'DIR': ('Zero', I, '0 only: downcounting is not audited'),
            'CMS': ('Zero', I, '00 only: centre-aligned modes are not audited'),
            'ARPE': ('Any', I, 'the stock model: ARR preload (STM32_Timer.cs L164, L365); not probed (both firmwares write 1)'),
            'CKD': ('Zero', T, '00: the clock division is a tag; it sets the dead-time and filter clock, neither modeled'),
            'UIFREMAP': ('Zero', U, '0 only: the stock model reserves the bit'),
        }[base]
    if reg == 'TIMx_CR2':
        if re.fullmatch(r'OIS[1-3]N', base):
            return ('Any', G, 'kept by the guard and served on read (a stock tag); drives nothing: no complementary output is modeled (ruled 2026-11-24T06:10Z Q5 (a)); locked from LOCK level 1 (p1615)')
        if re.fullmatch(r'OIS[1-4]', base):
            return ('Any', G, 'kept by the guard and served on read (a stock tag): the idle level of the output while MOE = 0 with OSSI = 1 (OIS1 p1591, OSSI p1615); locked from LOCK level 1 (p1615)')
        if base in ('OIS5', 'OIS6', 'MMS2'):
            return ('Zero', U, '0 only: the stock model reserves the bits; OC5/OC6 reach no pin and no trigger output is modeled')
        if base == 'TI1S':
            return ('Zero', I, '0 only: the input XOR is not audited')
        return ('Zero', T, '0 only: a stock tag (' + base + '); preloaded control, DMA selection and the trigger output are not modeled')
    if reg == 'TIMx_SMCR':
        if base in ('SMS', 'TS') and lsb in (16, 20):
            return ('Zero', U, '0 only: the stock model reserves the bits')
        if base in ('SMS', 'TS'):
            return ('Zero', I, '0 only: slave modes and trigger selection are not audited')
        return ('Zero', T, '0 only: a stock tag (' + base + '); the external trigger is not modeled')
    if reg == 'TIMx_DIER':
        if re.fullmatch(r'UIE|CC[1-4]IE|TIE', base):
            return ('Any', I, 'the stock model: interrupt enables (STM32_Timer.cs L215-233); with CCxE = 0 a running channel no longer reaches the pin (the guard gates it)')
        if base in ('COMIE', 'BIE', 'COMDE'):
            return ('Zero', U, '0 only: the stock model reserves the bit; COM and break are not modeled')
        return ('Zero', T, '0 only: a DMA request enable, a stock tag; DMA is the next 13NN')
    if reg == 'TIMx_SR':
        # TVI-WO-1205, ruled 2026-11-26T17:02Z: each SR field read against ArduPilot's armed run (Aethelgard 4400h's Sentinel-03:
        # SR written 0 three times and never read, DIER 0, CCMR1/2 0x6868 and CCMR3 0x01016868 (outputs), SMCR never written,
        # BDTR 0x8000, CR2 0, EGR 0x1; spike/1205/data/main-5d4a61a/tim1-accesses.tsv), the stock model (STM32_Timer.cs) and
        # RM0433 Rev 9 pp1566-1567 (break), p1592 (CCPC), pp1596-1598 (SR, EGR).
        ap = "ArduPilot's path is not affected: its armed run writes SR = 0, never reads SR and enables no TIM1 interrupt or DMA request (DIER = 0) (TVI-WO-1205, ruled 2026-11-26T17:02Z)"
        if re.fullmatch(r'UIF|CC[1-4]IF', base):
            return ('Any', sw('the stock model sets UIF at UG only with UIE = 1, and CCxIF at a compare only with CCxIE = 1 (STM32_Timer.cs L283, L132-134); silicon sets them regardless (p1597), so a firmware that polls them reads 0 in the twin. ' + ap), 'the stock model: cleared by writing 0, a 1 changes nothing (rc_w0)')
        if base == 'TIF':
            return ('Any', sw('the stock model sets TIF at a trigger only with TIE = 1 (STM32_Timer.cs L656-658); silicon sets it at a TRG event (an active TRGI edge with the slave mode controller enabled in all modes but gated, or a start or stop in gated mode, p1597) and at EGR.TG = 1 (p1598), so a firmware that polls it reads 0 in the twin. ' + ap + '; it never writes SMCR and never sets TG, so no trigger event occurs on silicon either'), 'the stock model: cleared by writing 0, a 1 changes nothing (rc_w0)')
        if re.fullmatch(r'CC[1-4]OF', base):
            return ('Any', 'acknowledged by name', 'the stock model, as silicon: set only in input capture mode when a capture finds CCxIF already set; reading CCRx clears CCxIF, and writing 0 clears it, a 1 changes nothing (rc_w0) (A10; p1596)')
        if base in ('CC5IF', 'CC6IF'):
            return ('Any', sw('the twin reads ' + base + ' 0: the stock model has no channels 5 and 6. Silicon sets it at a compare match (p1596, "Refer to CC1IF"; p1597); ArduPilot writes CCMR3 = 0x01016868 (channels 5 and 6 outputs) and never writes CCR5/CCR6, which reset to 0, so on silicon it would set at each counter match with 0 (inferred from p1597, not run). ' + ap), 'reads 0 and is never set (channels 5-6 are refused or unrouted); a write of 0 or 1 changes nothing (rc_w0)')
        why = {'BIF': 'silicon sets BIF when the break input goes active with BKE = 1, or at a software break, BG = 1, whatever BKE (p1597; p1567)',
               'B2IF': 'silicon sets B2IF when the break 2 input goes active with BK2E = 1, or at a software break, B2G = 1, whatever BK2E (p1596; p1567)',
               'SBIF': 'silicon sets SBIF at a system break (p1596), whose sources are the core lockup, the PVD, an SRAM, TCM or flash double ECC error and a CSS clock failure (p1566), none of them modeled',
               'COMIF': 'silicon sets COMIF at a COM event (p1597): COMG = 1 (p1598) or, with CCPC = 1, a TRGI edge (p1592)'}[base]
        return ('Any', sw('the twin reads ' + base + ' 0 and never sets it: break, break 2, system break and COM are not modeled, and ' + why + '. ' + ap + '; and it sets none of them on silicon either (BDTR = 0x8000: MOE only, no BKE or BK2E; EGR = UG only, no BG, B2G or COMG; CR2 = 0; SMCR never written)'), 'reads 0 and is never set; a write of 0 or 1 changes nothing (rc_w0)')
    if reg == 'TIMx_EGR':
        if base == 'UG':
            return ('Any', I, 'the stock model: reinitialises the counter (STM32_Timer.cs L266-295); probed (T1, the control)')
        return ('Zero', T if base in ('CC1G', 'CC2G', 'CC3G', 'CC4G', 'COMG', 'TG') else U, '0 only: a capture, COM, trigger or break event is not modeled')
    if reg in ('TIMx_CCMR1', 'TIMx_CCMR2'):
        if base.startswith('CC') and base.endswith('S'):
            return ('Zero', I, '00 only: the channel is an output; input capture is not audited')
        if re.fullmatch(r'OC\dM', base) and msb == lsb:
            return ('Zero', U, '0 only: the stock model reserves bits 16 and 24, so the modes 1000-1111 are not modeled')
        if re.fullmatch(r'OC\dM', base):
            return ('{0, 4, 5, 6}', G, 'the stock model\'s compare modes frozen, force inactive, force active and PWM mode 1; in PWM mode 1 a CCRx of 0 holds OCxREF at 0 (p1558), the guard lowering the stock line; locked at LOCK level 3 (p1602)')
        if re.fullmatch(r'OC\dPE', base):
            return ('Any', sw('OCxPE is a stock tag: no preload, so a CCRx write takes effect at once where silicon waits for the update event (p1603); acknowledged by name, ' + RULED + ' Q7; Aethelgard lists it unobservable'), 'kept by the guard and served on read; locked at LOCK level 3 (p1603)')
        return ('Zero', T, '0 only: a stock tag (' + base + '); fast enable and OCxREF clear are not modeled')
    if reg == 'TIMx_CCER':
        if re.fullmatch(r'CC[1-4]E', base):
            return ('Any', G, 'gates the channel output with MOE (Table 337 p1608), in the guard alone (the stock model sees every CCxE set, so its line stays OCxREF): disabled, the pin is not driven by the timer (Hi-Z) and the guard drives it low. ' + HIZ)
        if re.fullmatch(r'CC[1-4]P', base):
            return ('Any', G, 'kept by the guard and applied once to the driven level, OCx = OCxREF xor CCxP (Table 337 p1608); the stock model never applies it to an output; locked at LOCK level 2 (p1607)')
        if re.fullmatch(r'CC[1-3]NE|CC[1-4]NP', base):
            return ('Zero', sw('a CHxN pin in AF mode shows NOT CHx through the GPIO port\'s invertedAFPins, whatever CCxNE says, where silicon leaves it Hi-Z with CCxNE = 0 (Table 337 p1608): a GPIO-guard row, ' + RULED + ' Q5 (a)'), '0 only: the guard has no complementary output to gate')
        return ('Zero', U, '0 only: channels 5 and 6 are not modeled (the stock model reserves bits 16-31)')
    if reg == 'TIMx_CNT':
        if base == 'UIFCPY':
            return ('Zero', RO, 'ignored (read-only); reads 0 (UIFREMAP = 0)')
        return ('Any', I, 'the stock model: the counter value; not written by either firmware')
    if reg == 'TIMx_PSC':
        return ('Any', I, 'the stock model: CK_CNT = f / (PSC + 1) (p1609); probed (T1, the control)')
    if reg == 'TIMx_ARR':
        return ('Any', sw('the period is ARR ticks where silicon counts ARR + 1 (Figure 379 p1558); kept as the stock model\'s, coordinated with Aethelgard\'s motor witness (aethelgard-twin/src/timer.rs L6-24; ' + RULED + ' Q1)'), 'the stock model: the auto-reload value; probed (T1)')
    if reg == 'TIMx_RCR':
        return ('Zero', I, '0 only: the repetition counter is not audited')
    if re.fullmatch(r'TIMx_CCR[1-4]', reg):
        return ('Any', G, 'the stock model, set to 0 at reset by the guard (p1610; the stock reset is its initialLimit, 0xFFFF); in PWM mode 1 a value of 0 holds OCxREF at 0 (p1558): the guard lowers the stock line, which the stock model leaves at its last level; a value above ARR holds it at 1 (p1558), as the stock model does')
    if reg == 'TIMx_BDTR':
        if base == 'MOE':
            return ('Any', G, 'kept by the guard: with MOE = 0 the outputs are disabled (OSSI = 0, Hi-Z, driven low) or at their idle level OISx (OSSI = 1) (Table 337 p1608, p1614); set only by software, since AOE and the break inputs are refused')
        if base == 'OSSI':
            return ('Any', G, 'kept by the guard: with MOE = 0, an enabled output goes to its idle level OISx (1) or is released (0, Hi-Z, driven low) (p1615, Table 337 p1608); locked at LOCK level 2')
        if base == 'OSSR':
            return ('Any', G, 'kept by the guard: it acts only with CCxNE = 1 (Table 337 p1608), which is refused, so it changes no output; locked at LOCK level 2 (p1614)')
        if base == 'LOCK':
            return ('Any', G, 'kept by the guard: frozen by the first BDTR write after reset (p1615); level 1 locks DTG, BKE, BKP, AOE and OISx/OISxN, level 2 also CCxP, CCxNP, OSSR and OSSI, level 3 also OCxM and OCxPE (p1615); a write that would change a locked bit is a predicted-fault (silicon keeps the old value)')
        return ('Zero', T if base in ('DTG', 'BKE', 'BKP', 'AOE') else U, '0 only: ' + {'DTG': 'no dead time is modeled', 'AOE': 'MOE is never set by the update event', 'BKE': 'no break input is modeled', 'BKP': 'no break input is modeled'}.get(base, 'the break inputs and their filters are not modeled (the stock model reserves bits 16-31)'))
    if reg == 'TIMx_DCR':
        return ('Zero', U, '0 only: DMA bursts are not modeled (DMA is the next 13NN)')
    if reg == 'TIMx_CCMR3':
        return ('Any', sw('the stock model has no CCMR3 and drops a write; OC5REF and OC6REF reach no pin and nothing modeled while CC5E = CC6E = 0 and GC5Cx = 0, so the value is kept by the guard and served on read (' + RULED + ' Q7; ArduPilot writes 0x1016868)'), 'kept by the guard and served on read; drives nothing')
    if reg in ('TIMx_CCR5', 'TIMx_CCR6'):
        return ('Zero', U, '0 only: channels 5 and 6 are not modeled; GC5Cx would combine OC5REF into OC1-3')
    if reg in ('TIMx_AF1', 'TIMx_AF2'):
        return ('{' + str(reset) + '}', U, 'its reset value only: the break inputs and the ETR selection are not modeled')
    if reg == 'TIMx_TISEL':
        return ('Zero', U, '0 only: the input selection is not audited')
    raise SystemExit(f'no class for {reg}.{f}')


rows, tim8, tim1 = [], [], []
for line in open(tsv):
    c = line.rstrip('\n').split('\t')
    if c[0] == 'register' or 'input-mode' in (c[8] if len(c) > 8 else '') or 'UIFREMAP=1' in (c[8] if len(c) > 8 else ''):
        continue
    reg, off, f, msb, lsb, acc, reset, page = c[0], int(c[1], 16), c[2], int(c[3]), int(c[4]), c[5], c[6], int(c[7])
    if reg.startswith('TIM8_'):
        tim8.append((off, f, msb, lsb, reset))
        continue   # TIM8's AF1, AF2 and TISEL must have TIM1's layout and resets (asserted below): one table serves both
    if reg.startswith('TIM1_'):
        tim1.append((off, f, msb, lsb, reset))
    reg = re.sub(r'^TIM1_', 'TIMx_', reg)
    if off == 0x4C:
        continue   # DMAR: every access refuses (TVI-TIM-DMAR); the table cannot say "no value"
    rv = int(reset.replace(' ', ''), 16)
    fv = (rv >> lsb) & ((1 << (msb - lsb + 1)) - 1)
    rows.append((reg, off, f, msb, lsb, acc, fv, page))

assert sorted((o, m, l, r) for o, f, m, l, r in tim8) == sorted((o, m, l, r) for o, f, m, l, r in tim1), 'TIM8 AF1/AF2/TISEL differ from TIM1\'s'

# 16-bit registers: bits 31:16 reserved
by = {}
for r in rows:
    by.setdefault((r[0], r[1]), []).append(r)
for (reg, off), rs in list(by.items()):
    if max(r[3] for r in rs) == 15:
        rows.append((reg, off, 'Reserved', 31, 16, 'Res', 0, rs[0][7]))
rows.sort(key=lambda r: (r[1], -r[3]))
for (reg, off), rs in sorted({(r[0], r[1]): 0 for r in rows}.items(), key=lambda x: x[0][1]):
    m = 0
    for r in rows:
        if r[1] == off:
            mask = ((1 << (r[3] + 1)) - 1) & ~((1 << r[4]) - 1)
            assert m & mask == 0, (reg, r)
            m |= mask
    assert m == 0xFFFFFFFF, (reg, hex(m))

out = []
for reg, off, f, msb, lsb, acc, fv, page in rows:
    allowed, k, behaves = cls(reg, f, msb, lsb, fv)
    if allowed.startswith('{'):
        allowed = 'new uint[] ' + allowed.replace('{', '{ ').replace('}', ' }')
    name = f if f != 'Reserved' else f'Res{msb}_{lsb}'
    k, behaves = READ_1206.get((reg, name), (k, behaves))  # TVI-WO-1206
    esc = lambda s: s.replace('\\', '\\\\').replace('"', '\\"')
    out.append(f'            R("{reg}", 0x{off:02X}, "{SEC[off]}", "{name}", {msb}, {lsb}, {allowed}, "{esc(k)}", "{esc(behaves)}", {page}),')
print('\n'.join(out))
