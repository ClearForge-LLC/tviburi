"""TVI-WO-1206: the builder's state for each of the 121 fields, after checking the reading workers' rows (their quotes by
spike/1206/check-reads.py; their claims by the builder against the armed run, the generated boots and the fixture). Emits
data/readings.tsv: field, state (acknowledged | stock-wrong | held), the A-number (acknowledged), the class string and the
behaves-as text the field's ledger row takes. Each text cites RM0433 Rev 9 by page and the stock source (renode-infrastructure
066a7f13) by file and line, from the checked reading.

Usage: python3 -I spike/1206/readings.py READS_DIR spike/1206/data/items.tsv > spike/1206/data/readings.tsv
       python3 -I spike/1206/readings.py READS_DIR spike/1206/data/items.tsv --table   (TVI-WO-1206.rm0433.md §3's table)"""
import glob, json, os, sys

reads_dir, items = sys.argv[1:3]
R = {}
for p in glob.glob(os.path.join(reads_dir, '*.jsonl')):
    for l in open(p):
        r = json.loads(l); R[r['field']] = r
work = {l.split('\t')[0]: l.rstrip('\n').split('\t') for l in open(items) if not l.startswith('#')}

LINES = {'RCC_APB4ENR.LPUART1EN': ['STM32H7_RCC.cs L1010-1012', 'STM32H7_RCC.cs L1355-1364']}  # the reader cited APB4Reset's bit
VALUES = {'I2C_CR2.AUTOEND': '0x0,0x1'}  # the generated boards write 1 (CR2 0x201248C, 0x20124EC, 0x206248C: spike/1100/data/writes.tsv); 1100's queue has null


def cite(f, pages=None, lines=None):
    r = R[f]
    lines = lines or LINES.get(f)
    ps = pages or sorted({int(q['page']) for q in r['rm']})
    st = lines or [q for q in dict.fromkeys('%s %s' % (q['file'].split('/')[-1].split(' ')[0], q['lines'].split(' ')[0]) for q in r['stock'])][:2]
    return 'p' + ', p'.join(str(x) for x in ps) + '; ' + '; '.join(st)

EVID = 'spike/1206/data: the armed run (main-8e15435/reads.tsv), the 14 generated boots and the fixture (gen-reads.tsv)'
ACCESS = 'spike/1206/data/main-8e15435/instance-accesses.tsv: the armed run, the 14 generated boots and the fixture'

# The acknowledgements (TVI-WO-1206.rm0433.md §3): A-number -> (reason, fields)
ACK = {
    1: ("a 0 gates the peripheral's clock and its registers then ignore writes and read invalid data (p373), a 1 clocks it; the stock RCC "
        "sets the peripheral's bus enable from this bit, so a 0 drops its accesses and reads 0, and the bit reads back from the guard's copy",
        'RCC_AHB1ENR.ADC12EN RCC_AHB1ENR.DMA1EN RCC_AHB1ENR.DMA2EN RCC_AHB1ENR.ETH1MACEN RCC_AHB4ENR.ADC3EN RCC_AHB4ENR.GPIOCEN '
        'RCC_AHB4ENR.GPIODEN RCC_AHB4ENR.GPIOEEN RCC_AHB4ENR.GPIOFEN RCC_APB1LENR.TIM2EN RCC_APB2ENR.TIM1EN RCC_APB2ENR.TIM8EN '
        'RCC_APB2ENR.TIM15EN RCC_APB2ENR.TIM16EN RCC_APB2ENR.TIM17EN RCC_APB2ENR.SPI4EN RCC_APB2ENR.USART6EN RCC_APB4ENR.I2C4EN '
        'RCC_APB4ENR.LPTIM2EN RCC_APB4ENR.LPTIM3EN RCC_APB4ENR.LPTIM4EN RCC_APB4ENR.LPTIM5EN RCC_APB4ENR.LPUART1EN RCC_APB4ENR.SYSCFGEN'),
    2: ("written 0 only: the bit is stored and reads back from the guard's copy as silicon's does, and the stock RCC registers no "
        "peripheral at it, so it gates nothing; no firmware accesses a peripheral behind it (ethernet, the one present, has no access in any "
        "run: " + ACCESS + ")",
        'RCC_AHB1ENR.ETH1RXEN RCC_AHB1ENR.ETH1TXEN RCC_AHB1ENR.USB1OTGHSULPIEN RCC_AHB1ENR.USB2OTGHSULPIEN RCC_AHB2ENR.DCMIEN '
        'RCC_AHB2ENR.HASHEN RCC_AHB2ENR.SDMMC2EN RCC_APB2ENR.HRTIMEN RCC_APB2ENR.DFSDM1EN RCC_APB2ENR.SAI1EN RCC_APB2ENR.SAI2EN '
        'RCC_APB2ENR.SAI3EN RCC_APB2ENR.SPI5EN RCC_APB4ENR.SAI4EN RCC_APB4ENR.SPI6EN RCC_APB4ENR.COMP12EN RCC_APB4ENR.VREFEN'),
    3: ("written 0, then 1 at 4.93 s (BDCR 0x8000), and read back 1 every 100 ms (90 reads): silicon's register holds it and the guard "
        "serves its copy (the guard keeps rtc on the bus itself); with RTCSEL = 00 rtc_ck has no source on either side (p427), and the "
        "firmware uses only the RTC's backup registers, which follow RCC_APB4ENR.RTCAPBEN (p366): no time register is read",
        'RCC_BDCR.RTCEN'),
    4: ("its reset value, written 0: stored and read back alike; with RTCSEL = 00 the RTC has no clock on either side (p427)",
        'RCC_BDCR.BDRST RCC_BDCR.LSEBYP RCC_BDCR.LSECSSON RCC_BDCR.LSEDRV RCC_BDCR.LSEON RCC_BDCR.RTCSEL'),
    5: ("for the value written the twin's bus frequency equals silicon's (the divider's table, pp394-396); the guard's KernelHz and BusHz "
        "decode every value as the manual does",
        'RCC_D1CFGR.D1CPRE RCC_D1CFGR.D1PPRE RCC_D1CFGR.HPRE RCC_D2CFGR.D2PPRE1 RCC_D2CFGR.D2PPRE2'),
    6: ("for the value written the twin's PLL1 frequency equals the manual's formula (p347: VCO = ref x (DIVN + 1), out = VCO / (DIVy + 1)), "
        "inside p346's input ranges: the armed run's VCOH on 16 MHz (HSI / 4), the generated boards' VCOH on 3.2 MHz (hse-crystal-16: HSE 16 / 5) "
        "and VCOL on 1.6 MHz (hse-external-8: 8 / 5), each VCO in its range (p404)",
        'RCC_PLL1DIVR.DIVN1 RCC_PLL1DIVR.DIVP1 RCC_PLL1DIVR.DIVQ1 RCC_PLL1DIVR.DIVR1'),
    13: ("by TVI-WO-1300c's ruling Q1 (2026-11-26T04:03Z), on field evidence: PLL2 runs VCOL (PLLCFGR 0x1BF0FFD, PLL2VCOSEL = 1) on an 8 MHz "
         "reference (PLLCKSELR 0x408040: HSI 64 MHz / DIVM2 8), where p346 gives VCOL 1 to 2 MHz and p347 says VCOL must be chosen only at or "
         "below 2 MHz; RM0433 then guarantees no frequency. The twin serves the formula's (pll2_q_ck 72 MHz, every USART's kernel clock), as "
         "the PLL2VCOSEL row acknowledges by name (as ChibiOS chooses it; bench row R11). PLL2's stock fields are narrower than silicon's, and "
         "every value written fits them",
         'RCC_PLL2DIVR.DIVN2 RCC_PLL2DIVR.DIVP2 RCC_PLL2DIVR.DIVQ2 RCC_PLL2DIVR.DIVR2'),
    7: ("written 0 only, the mode the timer runs in on both sides: outputs (CCxS = 00), edge-aligned upcounting, updates enabled, no input XOR, "
        "no trigger interrupt (the guard holds SMCR.SMS at 0 and refuses EGR.TG)",
        'TIMx_CCMR1.CC1S[1:0] TIMx_CCMR1.CC2S[1:0] TIMx_CCMR2.CC3S[1:0] TIMx_CCMR2.CC4S[1:0] TIMx_CR1.CMS[1:0] TIMx_CR1.DIR TIMx_CR1.UDIS '
        'TIMx_CR2.TI1S TIMx_DIER.TIE'),
    8: ("written 0 only: 7-bit addressing, no reload, and a 0 written to ADDRCF clears nothing, alike on both sides",
        'I2C_CR2.ADD10 I2C_CR2.RELOAD I2C_ICR.ADDRCF'),
    9: ("written 0 only: oversampling by 16, no receiver-timeout, transfer-complete or TX-FIFO interrupt, alike on both sides",
        'USART_CR1.OVER8 USART_CR1.RTOIE USART_CR1.TCIE USART_CR1.TXFNFIE'),
    10: ("a 1 clears its ISR flag and a 0 does nothing, on both sides; the twin never sets the flag, which is its own read-only row's matter. "
         "The guard type also binds uart4/5/7/8, where some of these bits are reserved (Table 402, p2018); none is accessed in any run",
         'USART_ICR.CMCF USART_ICR.CTSCF USART_ICR.EOBCF USART_ICR.FECF USART_ICR.LBDCF USART_ICR.NECF USART_ICR.ORECF USART_ICR.PECF '
         'USART_ICR.TCBGTCF USART_ICR.TXFECF USART_ICR.WUCF'),
    11: ("by the architect's ruling, 2026-11-27T00:10Z, on field evidence: p347 says \"To insure an optimal behavior of the PLL when one of "
         "the post-divider (DIVP, DIVQ or DIVR) is not used, the application shall set the enable bit (DIVyEN) as well as the corresponding "
         "post-divider bits (DIVP, DIVQ or DIVR) to '0'. If the above rules are not respected, the PLL output frequency is not guaranteed.\" "
         "The armed run writes PLLCFGR 0x1FF0000, PLL3DIVR 0x7040E0E, PLLCFGR 0x1BF0FFD (DIVP3EN = 0, DIVP3 = 7), then CR 0x15003025 (PLL1-3 on). "
         "PLL3 also runs VCOL on a 16 MHz reference (PLL3VCOSEL = 1, DIVM3 4), outside p346's 1 to 2 MHz (the PLL3VCOSEL row, Q1, R11). RM0433 guarantees no frequency for PLL3's outputs in this configuration. The twin serves the formula's 30 MHz on pll3_r_ck to I2C1/2 "
         "and the ADC. ArduPilot flies this configuration on silicon, which is field evidence, not the manual. Bench row BR1 "
         "(TVI-WO-1206 FEEDBACK §5): measure pll3_r_ck on the Nucleo with DIVP3EN = 0 and DIVP3 = 7, by I2C SCL timing",
         'RCC_PLL3DIVR.DIVN3 RCC_PLL3DIVR.DIVP3 RCC_PLL3DIVR.DIVQ3 RCC_PLL3DIVR.DIVR3'),
    12: ("by the architect's ruling of 2026-11-25T05:06Z Q10 (a clear register: the write clears nothing), on field evidence: p2012 says only "
         "\"Reserved, must be kept at reset value\", so RM0433 gives no behaviour for the 1s ChibiOS writes here (ICR = 0x2, 0x4); the twin "
         "ignores the bits, and a 0 behaves alike on both sides. ChibiOS flies with these writes on silicon, which is field evidence, not the "
         "manual. Bench row BR2 (TVI-WO-1206 FEEDBACK §5): read I2C_ISR after ICR = 0x2 and 0x4 on the Nucleo",
         'I2C_ICR.Res2_1'),
}

# stock-wrong: field -> (what differs, why the flight path is unaffected)
NEVER = 'no firmware accesses it: no read or write in any run (%s)' % ACCESS
SW = {
    'RCC_AHB2ENR.CRYPTEN': ("a 0 leaves the twin's CRYP core reachable, where silicon ignores its writes and reads invalid data (p373)", NEVER.replace('it', 'crypto', 1)),
    'RCC_AHB1ENR.USB1OTGHSEN': ("a 0 leaves the twin's OTG_HS core reachable, where silicon ignores its writes and reads invalid data (p373)", NEVER.replace('it', 'usb1', 1)),
    'RCC_AHB1ENR.USB2OTGHSEN': ("a 0 leaves the twin's OTG core reachable, where silicon ignores its writes and reads invalid data (p373)", NEVER.replace('it', 'usb2', 1)),
    'RCC_AHB2ENR.SRAM1EN': ("the twin never gates SRAM1, where silicon needs the bit set before the CPU uses it (p369, p456)",
                            "AHB2ENR's first write (0x20000000, at 4.06 us) sets it, and every later one only sets bits, so it is 0 only from reset to its enable; the access log covers peripherals, not memory, so that window is not measured (a row)"),
    'RCC_AHB2ENR.SRAM2EN': ("the twin never gates SRAM2, where silicon needs the bit set before the CPU uses it (p369, p456)",
                            "every AHB2ENR write only sets bits (0x20000000, 0x60000000, 0xE0000000, 0xE0000040, the first at 4.06 us), so the bit is 0 only from reset to its enable; the access log covers peripherals, not memory, so that window is not measured (a row)"),
    'RCC_AHB2ENR.SRAM3EN': ("the twin never gates SRAM3, where silicon needs the bit set before the CPU uses it (p369, p456)",
                            "as SRAM2EN: 0 only from reset to its enable; that window is not measured (a row)"),
    'RCC_CSR.LSION': ("the twin's LSIRDY follows LSION alone, where silicon also runs the LSI for a started IWDG (p344, p429; the armed run starts it at 4.77 s)",
                      "no firmware reads RCC_CSR: no read in the armed run, the generated boots or the fixture (%s)" % EVID),
    'RCC_RSR.RMVF': ("RMVF reads back 0 (write-one-to-clear) where silicon's is set and reset by software (p451); the flags it clears read 0 after on both sides",
                     "RCC_RSR is read once, before the write, by the armed run and by the fixture, and never after (%s)" % EVID),
    'RCC_D3CFGR.D3PPRE': ("the stock model applies a new APB4 divider to its APB4 peripherals only at the next CR, CFGR, D1CFGR or D2CFGR write (no update on D3CFGR)",
                          "the armed run's CFGR write 3.98 us later applies it; no APB4 timer runs (lptimer2-5: no access in any run, %s); the guard's own APB4 frequency is computed at the write" % ACCESS),
    'TIMx_CR1.ARPE': ("with ARPE = 1 the stock model loads a new ARR only at overflow, not at UG (p1531, p1533)",
                      "every ARR change is written with CR1 = 0 (ARPE = 0, counter stopped) and followed by UG: ArduPilot's two configurations (0.104 s, 7.77 s) and every generated board's (spike/1100/data/writes.tsv); later ARR writes repeat the value in force"),
    'TIMx_CR1.URS': ("with URS = 0 a UG sets UIF on silicon (p1597); the stock model sets it only with UIE = 1",
                     "no firmware reads TIMx_SR: 0 TIM1 reads in the armed run, no SR read in the generated boots or the fixture (%s); with DIER = 0 no interrupt or DMA request follows on either side" % EVID),
    'TIMx_DIER.UIE': ("with UIE = 0 silicon still sets UIF at an update event (p1597); the stock model does not at UG",
                      "no firmware reads TIMx_SR (%s); the interrupt is off on both sides" % EVID),
    **{'TIMx_DIER.CC%dIE' % k: ("with CC%dIE = 0 silicon still sets CC%dIF at a compare match (p1597); the stock model sets it only with the enable" % (k, k),
                                 "no firmware reads TIMx_SR (%s); the interrupt is off on both sides" % EVID) for k in (1, 2, 3, 4)},
    'TIMx_PSC.PSC[15:0]': ("the stock model applies a new PSC at the write, where silicon loads it at the next update event (p1531, p1609)",
                           "every PSC write is made with the counter stopped and followed by UG, which loads it on silicon: ArduPilot's two configurations and every generated board's (spike/1100/data/writes.tsv)"),
    'I2C_CR1.ERRIE': ("the twin raises no bus error (BERR, ARLO), where silicon can on a misplaced START or STOP or a lost arbitration (p1997-1998)",
                      "the twin's bus has one controller and no line faults, and the other four error sources (OVR, TIMEOUT, PECERR, ALERT) need modes their own rows allow only at 0"),
    'I2C_CR2.AUTOEND': ("a STOP does not clear TC in the stock model, where silicon clears it when START or STOP is set (p2011); and TC (AUTOEND = 0) or STOPF (1) comes at the last RXDR read, not at the last byte received",
                        "the generated boards' polling sees TC or STOPF at once, as I2C_TXDR.TXDATA's row says, and takes the same branches; ArduPilot, after each software STOP, clears TCIE in its next write and PE in the one after (CR2 0x6448C, CR1 0x97, 0x96: the armed run), and PE = 0 clears TC on both sides (p2011), with no event interrupt taken between; the generated boards run I2C with CR1 = 0x1 (no interrupts) and end with AUTOEND = 1 (spike/1100/data/writes.tsv)"),
    'I2C_TXDR.TXDATA': ("TXIS stays 1 from START to the last byte and TXE always reads 1, with no bus time (p2011, p2014)",
                        "the bytes, their order and their addressing are those silicon sends; a firmware polling I2C_ISR sees TXIS, TXE and TC at once where silicon sets them a byte time later (the generated boots poll so: genlog hse-crystal-16, i2c1 at 0.000042 s), so it polls fewer times and takes the same branches; no rule of the twin times the bus"),
    'SPI_TXDR.TXDR': ("TXP always reads 1 and RXP, EOT and TXC are set at the write, with no bus time (p2226-2227)",
                      "the frames and their order are those silicon shifts; 16- and 32-bit writes refuse TVI-SPI-WIDTH; only flag timing and bus time differ"),
    'GPIOx_OSPEEDR.OSPEEDR[15:0]': ("the speed sets the output driver's edge rate on silicon (p542, figures in DS12117); the stock port stores it with no effect",
                                    "the twin's pins carry logic levels only, which the speed does not change; the field reads back as written on both sides"),
    'USART_BRR.BRR': ("the twin's transmitter has no frame time: a TDR write sends the byte and sets TC at once, so BRR times no transmission (p2034-2035)",
                      "the bytes and their order are those silicon sends; a firmware polling USART_ISR sees TC = 1 at once (d1-drv8876 reads 0x206000C0 between back-to-back TDR writes) where silicon sets it a frame later, so it polls fewer times and takes the same branches; IDLE's frame time is the kernel clock over BRR in the guard (TVI-USART-IDLE); baud accuracy is Aethelgard's"),
    'USART_CR2.STOP': ("silicon's frame and TC timing grow with the stop bits (p2021, p2076); the stock transmitter ignores them (the guard's IDLE frame time counts them)",
                       "STOP = 10 is written only on ArduPilot's usart2, which receives and sends no byte in the armed run (no RDR read; %s)" % EVID),
    'USART_TDR.TDR': ("TC is set at the write, TXFNF never drops, and a DMA stream is unpaced, where silicon times each frame (p2026, p2089)",
                      "the bytes and their order are those silicon sends for the run's 8N1 frames (all byte writes, each at most 0xFF); only flag timing differs"),
    'USART_CR1.PEIE': ("the twin raises no parity error, where silicon interrupts on a frame whose parity fails (p2042, p2069)",
                       "PCE = 1 only on ArduPilot's usart2 (CR1 0x2000151D, 0x2000153D), which receives no byte in the armed run (no RDR read); every other port runs PCE = 0, where both agree (%s)" % EVID),
    'USART_CR1.PS': ("the twin neither generates nor checks parity, and its RDR bit 8 reads 0, where silicon's reads the received parity bit (p2042, p2069)",
                     "as PEIE: parity is on only on ArduPilot's usart2, which receives no byte in the armed run"),
    'USART_CR1.RXFNEIE': ("the twin never sets ORE, so the interrupt never fires on an overrun as silicon's does (p2090)",
                          "an overrunning byte with the FIFO off refuses TVI-USART-OVERRUN; with the FIFO on, FIFOEN's own stock-wrong row holds it"),
}
HELD = set()  # the five held fields were ruled 2026-11-27T00:10Z (A11, A12)

if len(sys.argv) > 3 and sys.argv[3] == '--table':  # TVI-WO-1206.rm0433.md §3, from the same data as the rows
    print('| A | Fields | Reading (both sides, for the values written) | Each row cites |')
    print('|---|---|---|---|')
    for n, (why, fs) in sorted(ACK.items()):
        print('| A%d | %s | %s | its own page(s) and stock line(s) |' % (n, ', '.join('`%s`' % f for f in fs.split()), why))
    sys.exit(0)
owner = {}
for n, (_, fs) in ACK.items():
    for f in fs.split():
        assert f not in owner, f; owner[f] = n
assert not set(owner) & set(SW) and not (set(owner) | set(SW)) & HELD
assert set(owner) | set(SW) | HELD == set(work), sorted(set(work) ^ (set(owner) | set(SW) | HELD))
print('# field\tstate\tA\tclass\tbehaves_as')
for f in sorted(work):
    raw = [v for v in (work[f][6].split(',') + work[f][7].split(',')) if v not in ('-', 'null')]
    vals = ','.join(sorted({hex(int(v, 0)) for v in raw if not v.split(' ')[-1].startswith('many')}, key=lambda x: int(x, 16)) + [v for v in raw if 'many' in v]) or '-'
    vals = VALUES.get(f, vals)
    if len(vals.split(',')) > 8:
        n = len(vals.split(','))
        vals = ('every value (%d distinct written)' % n) if R[f].get('all_values') is True else ('the %d distinct values written (spike/1206/data/items.tsv)' % n)
    if f in owner:
        n = owner[f]
        print('\t'.join([f, 'acknowledged', str(n), 'acknowledged by name',
                         'read for %s: %s (TVI-WO-1206 A%d; %s)' % (vals, ACK[n][0], n, cite(f))]))
    elif f in SW:
        what, why = SW[f]
        print('\t'.join([f, 'stock-wrong', '-', 'stock, wrong (%s; unaffected: %s)' % (what, why), 'read for %s (TVI-WO-1206; %s)' % (vals, cite(f))]))
    else:
        print('\t'.join([f, 'held', '-', '-', '-']))
