"""TVI-WO-1206: the builder's state for each of the 121 fields, after checking the reading workers' rows (their quotes by
spike/1206/check-reads.py; their claims by the builder against the armed run, the generated boots and the fixture). Emits
data/readings.tsv: field, state (acknowledged | stock-wrong | held), the A-number (acknowledged), the class string and the
behaves-as text the field's ledger row takes. Each text cites RM0433 Rev 9 by page and the stock source (renode-infrastructure
066a7f13) by file and line, from the checked reading.

Usage (from the repository root; everything it reads is in the repository, TVI-WO-1206a):
       python3 -I spike/1206/readings.py > spike/1206/data/readings.tsv
       python3 -I spike/1206/readings.py --table   (TVI-WO-1206.rm0433.md §3's table)
The readings are spike/1206/data/readings/reads (the first list's) and reads2 (the second's); the work lists items.tsv and
items2.tsv. Each row keeps its text on main at BASE (8e15435, read with git: what the row said about the guard's own rules
stays true) and appends its reading; where that text is wrong for a value now read, OLD replaces it.
spike/1206/rebuild.sh checks that this rebuilds every row byte for byte."""
import glob, json, os, subprocess, sys

DATA = 'spike/1206/data'
reads_dir, reads_dir2 = DATA + '/readings/reads', DATA + '/readings/reads2'  # the first and second lists' readings
items, items2 = DATA + '/items.tsv', DATA + '/items2.tsv'
BASE = '8e15435'  # main, the rows' text before this work order
R = {}
for p in glob.glob(os.path.join(reads_dir, '*.jsonl')) + glob.glob(os.path.join(reads_dir2, '*.jsonl')):
    for l in open(p):
        r = json.loads(l); R[r['field']] = r
work = {l.split('\t')[0]: l.rstrip('\n').split('\t') for f in (items, items2) for l in open(f) if not l.startswith('#')}

LINES = {'RCC_APB4ENR.LPUART1EN': ['STM32H7_RCC.cs L1010-1012', 'STM32H7_RCC.cs L1355-1364']}  # the reader cited APB4Reset's bit
VALUES = {}  # every value is now in items.tsv, from the logged boots (spike/1206/data/main-8e15435/gen-writes.tsv)


import re
# Where a row's text on main is wrong for a value now read, its replacement (from the checked reading)
OLD = {'I2C_CR2.AUTOEND': "the stock model: with AUTOEND = 1 a STOP after NBYTES (STOPF); with AUTOEND = 0 TC is set and the controller "
                          "waits for a START or a STOP (p2004-2005, p2011); a NACKed address's STOP comes whatever AUTOEND (p1975; TVI-WO-1306a)"}
TEXT = re.compile(r'R\("[^"]+", [^,]+, "[^"]*", "[^"]+", \d+, \d+, (?:new uint\[\] \{[^}]*\}|[^,]+), (?:"(?:[^"\\]|\\.)*"|Inherited), "((?:[^"\\]|\\.)*)", \d+\)')
def old_text(f):
    if f in OLD: return OLD[f]
    path, n = work[f][1].split(':')
    line = subprocess.run(['git', 'show', '%s:%s' % (BASE, path)], check=True, capture_output=True, text=True).stdout.split('\n')[int(n) - 1]
    return TEXT.search(line).group(1).replace('\\"', '"').replace('\\\\', '\\')


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
        "sets the peripheral's bus enable from this bit, so a 0 drops its accesses and reads 0, and the bit reads back from the guard's copy; "
        "no run reads such a peripheral while its clock is off (the only locked reads in any run are of i2c1, i2c2, usart2 and usart3, whose "
        "enables are the guard's own rows), and its locked writes (bdma, dma1, dma2) are ignored on both sides",
        'RCC_AHB1ENR.ADC12EN RCC_AHB1ENR.DMA1EN RCC_AHB1ENR.DMA2EN RCC_AHB1ENR.ETH1MACEN RCC_AHB4ENR.ADC3EN RCC_AHB4ENR.GPIOCEN '
        'RCC_AHB4ENR.GPIODEN RCC_AHB4ENR.GPIOEEN RCC_AHB4ENR.GPIOFEN RCC_APB1LENR.TIM2EN RCC_APB2ENR.TIM1EN RCC_APB2ENR.TIM8EN '
        'RCC_APB2ENR.TIM15EN RCC_APB2ENR.TIM16EN RCC_APB2ENR.TIM17EN RCC_APB2ENR.SPI4EN RCC_APB2ENR.USART6EN RCC_APB4ENR.I2C4EN '
        'RCC_APB4ENR.LPTIM2EN RCC_APB4ENR.LPTIM3EN RCC_APB4ENR.LPTIM4EN RCC_APB4ENR.LPTIM5EN RCC_APB4ENR.LPUART1EN RCC_APB4ENR.SYSCFGEN '
        'RCC_AHB4ENR.GPIOAEN RCC_AHB4ENR.GPIOBEN RCC_AHB4ENR.GPIOGEN RCC_AHB4ENR.GPIOHEN RCC_AHB4ENR.GPIOIEN RCC_AHB4ENR.GPIOJEN '
        'RCC_AHB4ENR.GPIOKEN RCC_AHB4ENR.CRCEN RCC_AHB4ENR.HSEMEN RCC_APB1LENR.TIM3EN RCC_APB1LENR.TIM4EN RCC_APB1LENR.TIM5EN '
        'RCC_APB1LENR.TIM6EN RCC_APB1LENR.TIM7EN RCC_APB1LENR.TIM12EN RCC_APB1LENR.TIM13EN RCC_APB1LENR.TIM14EN RCC_APB1LENR.LPTIM1EN '
        'RCC_APB1LENR.I2C3EN RCC_APB1LENR.UART4EN RCC_APB1LENR.UART5EN RCC_APB1LENR.UART7EN RCC_APB1LENR.UART8EN'),
    2: ("written 0 only: the bit is stored and reads back from the guard's copy as silicon's does, and the stock RCC registers no "
        "peripheral at it, so it gates nothing; no firmware accesses a peripheral behind it (ethernet, the one present, has no access in any "
        "run: " + ACCESS + ")",
        'RCC_AHB1ENR.ETH1RXEN RCC_AHB1ENR.ETH1TXEN RCC_AHB1ENR.USB1OTGHSULPIEN RCC_AHB1ENR.USB2OTGHSULPIEN RCC_AHB2ENR.DCMIEN '
        'RCC_AHB2ENR.HASHEN RCC_AHB2ENR.SDMMC2EN RCC_APB2ENR.HRTIMEN RCC_APB2ENR.DFSDM1EN RCC_APB2ENR.SAI1EN RCC_APB2ENR.SAI2EN '
        'RCC_APB2ENR.SAI3EN RCC_APB2ENR.SPI5EN RCC_APB4ENR.SAI4EN RCC_APB4ENR.SPI6EN RCC_APB4ENR.COMP12EN RCC_APB4ENR.VREFEN '
        'RCC_APB1LENR.CECEN RCC_APB1LENR.DAC12EN RCC_APB1LENR.SPDIFRXEN RCC_APB1LENR.SPI3EN'),
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
        "inside p346's input ranges: ArduPilot's VCOH on 16 MHz (HSI / 4), ten generated boards' VCOH on 12.8 MHz (HSI / 5, DIVN1 0xF: 204.8 MHz), "
        "hse-crystal-16's VCOH on 3.2 MHz (HSE 16 / 5) and hse-external-8's and sentinel-02-bench-hse8's VCOL on 1.6 MHz (8 / 5), each VCO in "
        "its range (p404; spike/1206/data/main-8e15435/gen-writes.tsv)",
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
        'TIMx_CR2.TI1S TIMx_DIER.TIE TIMx_CR1.OPM'),
    8: ("written 0 only: 7-bit addressing, no reload, and a 0 written to ADDRCF clears nothing, alike on both sides",
        'I2C_CR2.ADD10 I2C_CR2.RELOAD I2C_ICR.ADDRCF'),
    9: ("written 0 only: oversampling by 16, no receiver-timeout, transfer-complete or TX-FIFO interrupt, alike on both sides",
        'USART_CR1.OVER8 USART_CR1.RTOIE USART_CR1.TCIE USART_CR1.TXFNFIE'),
    10: ("a 1 clears its ISR flag and a 0 does nothing, on both sides; the twin never sets the flag, which is its own read-only row's matter. "
         "The guard type also binds uart4/5/7/8, where some of these bits are reserved (Table 402, p2018); none is accessed in any run",
         'USART_ICR.CMCF USART_ICR.CTSCF USART_ICR.EOBCF USART_ICR.FECF USART_ICR.LBDCF USART_ICR.NECF USART_ICR.ORECF USART_ICR.PECF '
         'USART_ICR.TCBGTCF USART_ICR.TXFECF USART_ICR.WUCF USART_ICR.RTOCF USART_ICR.UDRCF'),
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
         "manual. Bit 6 alike, as ruled 2026-11-27T00:43Z: ChibiOS writes ICR = 0x40 (TC's position in ISR) 2,901 times in the armed run, "
         "and p2012 gives it no behaviour either; ICR = 0x10 (NACKCF, its own field) is also written, 55 times on each bus. Bench row BR2 (TVI-WO-1206 FEEDBACK §5): read I2C_ISR after ICR = 0x2, 0x4 and 0x40 on the Nucleo",
         'I2C_ICR.Res2_1 I2C_ICR.Res6'),
    14: ("written 0 only: a 0 resets nothing, on either side (the stock callback acts only on a 1)",
         'RCC_AHB3RSTR.FMCRST RCC_APB4RSTR.SYSCFGRST'),
    15: ("written its reset value, or to a state both sides take at once: HSI on (HSIRDY), HSIDIV /1 (hsi_ck 64 MHz), CSI off, and HSECSSON "
         "a set-only bit kept on both, with no HSE failure in the twin to detect",
         'RCC_CR.HSION RCC_CR.HSIDIV RCC_CR.CSION RCC_CR.HSECSSON'),
    16: ("TIMPRE with every APB prescaler written /1 or /2 gives the timers the AHB clock either way (Table 57, p393), on both sides",
         'RCC_CFGR.TIMPRE'),
    17: ("a 1 clears the stream's TCIF and a 0 does nothing, on both sides; the guard then redraws the stream's interrupt line",
         'DMA_LIFCR.CTCIF0 DMA_LIFCR.CTCIF1 DMA_LIFCR.CTCIF2 DMA_LIFCR.CTCIF3 DMA_HIFCR.CTCIF4 DMA_HIFCR.CTCIF5 DMA_HIFCR.CTCIF6 DMA_HIFCR.CTCIF7'),
    18: ("for the values written each request moves one item of PSIZE from an address stepped as written (PINC 0; MSIZE equal to PSIZE on every "
         "enabled stream; direct mode with DMDIS 0), as silicon's peripheral-to-memory transfers do",
         'DMA_SxCR.MSIZE[1:0] DMA_SxCR.PSIZE[1:0] DMA_SxCR.PINC DMA_SxFCR.DMDIS'),
    19: ("written 0 only: the interrupt is off on both sides", 'SPI_IER.EOTIE SPI_IER.RXPIE SPI_IER.TXPIE'),
    20: ("for the values written: 8-bit frames (DSIZE 7, one byte per frame on both sides; 0, which p2220 calls 'not used', is written only with "
         "SPE = 0, acknowledged at stop by 0100b's A6, ruled 2026-11-23T11:00Z), a receive DMA request per frame received, and SUSPC clears SUSP",
         'SPI_CFG1.DSIZE SPI_CFG1.RXDMAEN SPI_IFCR.SUSPC'),
    21: ("written 0 only: no target mode, no clock-stretch disable, no receive DMA, no STOP interrupt, alike on both sides",
         'I2C_CR1.ADDRIE I2C_CR1.NOSTRETCH I2C_CR1.RXDMAEN I2C_CR1.STOPIE'),
    22: ("the interrupt is the flag AND the enable on both sides; ArduPilot's i2c1 takes all three (CR1 0xD3, 0xD7; i2c2 writes 0xD3, 0x90, 0x91, so RXIE 0 there), each of the armed run's 12,750 "
         "handler entries with an enabled flag set; when the flags fire is I2C_TXDR.TXDATA's and I2C_CR2.AUTOEND's matter",
         'I2C_CR1.TCIE I2C_CR1.TXIE'),
    23: ("each transfer goes to the address written, in the direction written, for NBYTES bytes (8,339 RXDR reads = 1,378 x 6 + 71 x 1 in the "
         "armed run; no START with NBYTES 0), as silicon's; at 0x90 ten generated boards address a TMP117 the platform has no model for, so the "
         "twin NACKs where silicon would ACK: the platform's missing device (TVI-WO-1306 Q9), not this field",
         'I2C_CR2.NBYTES I2C_CR2.RD_WRN I2C_CR2.SADD'),
    24: ("a 1 clears TC and a 0 does nothing, on both sides; every ISR read comes at least 37.5 ms after silicon's line would be idle, and TCIE is 0",
         'USART_ICR.TCCF'),
    25: ("by the architect's ruling, 2026-11-27T01:37Z, on field evidence: p2216 says \"the SUSP flag has to be cleared and the SPI disabled and "
         "re-enabled before any next transaction starts\" and p2175 \"SPI must always be disabled after such software suspension\"; spi1 restarts "
         "a transfer straight after a suspend with SPE kept at 1 679 times in the armed run (CR1 0x701 then 0x301). RM0433 gives no behaviour for "
         "that; the twin restarts the transfer. ArduPilot flies this sequence on silicon, which is field evidence, not the manual. Bench row BR3 "
         "(TVI-WO-1206 FEEDBACK §5): spi1's restart after a suspend without an SPE toggle, on the Nucleo",
         'SPI_CR1.CSTART'),
    26: ("by the architect's ruling, 2026-11-27T01:37Z, on field evidence: p2225 says SUSP \"has to be cleared prior SPI is disabled\"; every "
         "armed disable follows a suspend with SUSP still set (spi1 2,022 times, spi2 37,677). RM0433 gives no behaviour for that; the twin "
         "disables normally and keeps SUSP until the next start's IFCR write; at SPE 1 to 0 the stock model also clears TSIZE and EOT, which no run "
         "reads (no CR2, SR or RXDR read before the next enable, which rewrites CR2). ArduPilot flies this sequence on silicon, which is field evidence, "
         "not the manual. Bench row BR4 (TVI-WO-1206 FEEDBACK §5): disable with SUSP set, on the Nucleo",
         'SPI_CR1.SPE'),
    27: ("by the architect's ruling, 2026-11-27T01:37Z, on field evidence: p2180 orders the stream's enable before TXDMAEN \"to prevent DMA "
         "channel management raising error events\", naming no error; ArduPilot sets TXDMAEN first for every SPI transmit stream. RM0433 gives "
         "no behaviour for the other order; the twin raises no error, and its transmit stream is unpaced, the DMA guard's acknowledgement "
         "(ruled 2026-11-24T14:10Z Q5 (c), requests 38 and 40). ArduPilot flies this order on silicon, which is field evidence, not the manual. "
         "Bench row BR5 (TVI-WO-1206 FEEDBACK §5): read SPI_SR and the DMA flags after this order, on the Nucleo",
         'SPI_CFG1.TXDMAEN'),
    28: ("by the architect's ruling, 2026-11-27T01:37Z, on field evidence: p2097 says only \"Reserved, must be kept at reset value\"; ChibiOS "
         "writes USART_ICR = 0xFFFFFFFF at each init (49 writes in the armed run), and usart1's IDLE handler echoes its ISR read (0x206000D0 x19, "
         "0x20600090 x2). RM0433 gives no behaviour for those 1s; the twin ignores the bits. ChibiOS flies with these writes on silicon, which is "
         "field evidence, not the manual. Bench row BR2, extended (TVI-WO-1206 FEEDBACK §5): read USART_ISR after ICR = 0xFFFFFFFF on the Nucleo",
         'USART_ICR.Res10_10 USART_ICR.Res16_14 USART_ICR.Res19_18 USART_ICR.Res31_21'),
    29: ("by the architect's ruling, 2026-11-27T02:12Z, with the ambiguity in the open: p2179 says \"In reception, the RXP flag is not set "
         "when EOT is set. In the case when the number of data to be received (TSIZE) is not a multiple of packet size, the number of remaining "
         "data is indicated by the RXWNE and RXPLVL fields\"; the second sentence sets the case, so the twin follows the incomplete-packet "
         "reading: on a complete final packet it sets RXP and EOT together (SR 0x100F), which the manual does not settle. If silicon differs, "
         "the generated boards' bounded RXP poll returns SpiError::Rxp (cap-gen <board>/gen/board/src/board.rs L269) and their control flow "
         "changes; ArduPilot never reads SPI_SR, so this touches only the generated boards. SR.TXTF also reads 0 in the twin, a stock tag, "
         "where silicon sets it once TSIZE frames are queued (p2226), and no board tests it (all 14 crates mask 0x1 and 0x8 only). Bench row "
         "BR7 (TVI-WO-1206 FEEDBACK §5): poll RXP on the last frame of a TSIZE transfer, FTHLV 0, on the Nucleo",
         'SPI_CR2.TSIZE'),
    30: ("a 1 resets the block on both sides: the stock RCC calls the registered peripheral's own Reset() at every write with the bit 1 "
         "(STM32H7_RCC.cs L1377-1383; a field's write callback runs at each write of its register, PeripheralRegister.cs L335), as RM0433's "
         "'1: resets' the block; a 0 resets nothing. Whether silicon also holds the block in reset while the bit is 1 is not stated, and no run "
         "accesses a block while its bit is 1 (spike/1206/data/main-8e15435/reset-windows.tsv). Found by the independent adjudication of the "
         "second re-read (TVI-WO-1206 FEEDBACK §4)",
         '''RCC_AHB1RSTR.ADC12RST RCC_AHB1RSTR.DMA1RST RCC_AHB1RSTR.DMA2RST RCC_AHB1RSTR.ETH1MACRST RCC_AHB2RSTR.RNGRST RCC_AHB3RSTR.DMA2DRST RCC_AHB3RSTR.QSPIRST RCC_AHB4RSTR.ADC3RST RCC_AHB4RSTR.BDMARST RCC_AHB4RSTR.CRCRST RCC_AHB4RSTR.GPIOARST RCC_AHB4RSTR.GPIOBRST RCC_AHB4RSTR.GPIOCRST RCC_AHB4RSTR.GPIODRST RCC_AHB4RSTR.GPIOERST RCC_AHB4RSTR.GPIOFRST RCC_AHB4RSTR.GPIOGRST RCC_AHB4RSTR.GPIOHRST RCC_AHB4RSTR.GPIOIRST RCC_AHB4RSTR.GPIOJRST RCC_AHB4RSTR.GPIOKRST RCC_AHB4RSTR.HSEMRST RCC_APB1LRSTR.I2C1RST RCC_APB1LRSTR.I2C2RST RCC_APB1LRSTR.I2C3RST RCC_APB1LRSTR.LPTIM1RST RCC_APB1LRSTR.SPI2RST RCC_APB1LRSTR.TIM12RST RCC_APB1LRSTR.TIM13RST RCC_APB1LRSTR.TIM14RST RCC_APB1LRSTR.TIM2RST RCC_APB1LRSTR.TIM3RST RCC_APB1LRSTR.TIM4RST RCC_APB1LRSTR.TIM5RST RCC_APB1LRSTR.TIM6RST RCC_APB1LRSTR.TIM7RST RCC_APB1LRSTR.UART4RST RCC_APB1LRSTR.UART5RST RCC_APB1LRSTR.UART7RST RCC_APB1LRSTR.UART8RST RCC_APB1LRSTR.USART2RST RCC_APB1LRSTR.USART3RST RCC_APB2RSTR.SPI1RST RCC_APB2RSTR.SPI4RST RCC_APB2RSTR.TIM15RST RCC_APB2RSTR.TIM16RST RCC_APB2RSTR.TIM17RST RCC_APB2RSTR.TIM1RST RCC_APB2RSTR.TIM8RST RCC_APB2RSTR.USART1RST RCC_APB2RSTR.USART6RST RCC_APB3RSTR.LTDCRST RCC_APB4RSTR.I2C4RST RCC_APB4RSTR.LPTIM2RST RCC_APB4RSTR.LPTIM3RST RCC_APB4RSTR.LPTIM4RST RCC_APB4RSTR.LPTIM5RST RCC_APB4RSTR.LPUART1RST'''),
    31: ("as A1 for BDMA: a 0 drops its accesses, as silicon ignores them (p373; the armed run's nine BDMA writes while it is 0), and a 1 clocks "
         "it; p458's 1 also clocks DMAMUX2, which the twin does not model (TVI-WO-1206.rm0433 §5): its one write drops at any BDMAEN, the "
         "missing block's matter, not this field's",
         'RCC_AHB4ENR.BDMAEN'),
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
                      "every ARR change is written with CR1 = 0 (ARPE = 0, counter stopped) and followed by UG: ArduPilot's two configurations (0.104 s, 7.77 s) and every generated boot's and the fixture's (spike/1206/data/main-8e15435/gen-writes.tsv); later ARR writes repeat the value in force"),
    'TIMx_CR1.URS': ("with URS = 0 a UG sets UIF on silicon (p1597); the stock model sets it only with UIE = 1",
                     "no firmware reads TIMx_SR: 0 TIM1 reads in the armed run, no SR read in the generated boots or the fixture (%s); with DIER = 0 no interrupt or DMA request follows on either side" % EVID),
    'TIMx_DIER.UIE': ("with UIE = 0 silicon still sets UIF at an update event (p1597); the stock model does not at UG",
                      "no firmware reads TIMx_SR (%s); the interrupt is off on both sides" % EVID),
    **{'TIMx_DIER.CC%dIE' % k: ("with CC%dIE = 0 silicon still sets CC%dIF at a compare match (p1597); the stock model sets it only with the enable" % (k, k),
                                 "no firmware reads TIMx_SR (%s); the interrupt is off on both sides" % EVID) for k in (1, 2, 3, 4)},
    'TIMx_PSC.PSC[15:0]': ("the stock model applies a new PSC at the write, where silicon loads it at the next update event (p1531, p1609)",
                           "every PSC write is made with the counter stopped and followed by UG, which loads it on silicon: ArduPilot's two configurations and every generated boot's and the fixture's (spike/1206/data/main-8e15435/gen-writes.tsv)"),
    'I2C_CR1.ERRIE': ("the twin raises no bus error (BERR, ARLO), where silicon can on a misplaced START or STOP or a lost arbitration (p1997-1998)",
                      "the twin's bus has one controller and no line faults, and the other four error sources (OVR, TIMEOUT, PECERR, ALERT) need modes their own rows allow only at 0"),
    'I2C_CR2.AUTOEND': ("a STOP does not clear TC in the stock model, where silicon clears it when START or STOP is set (p2011); and TC (AUTOEND = 0) or STOPF (1) comes at the last RXDR read, not at the last byte received",
                        "the generated boards' polling sees TC or STOPF at once, as I2C_TXDR.TXDATA's row says, and takes the same branches; ArduPilot, after each software STOP, clears TCIE in its next write and PE in the one after (CR2 0x6448C, CR1 0x97, 0x96: the armed run), and PE = 0 clears TC on both sides (p2011), with no event interrupt taken between (the armed run, and the fixture 507 times); the generated boards write CR1 only 0x0 and 0x1 (no interrupts) and never a software STOP (spike/1206/data/main-8e15435/gen-writes.tsv)"),
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
SW.update({f: ("no peripheral is registered at this bit's alias, so the twin resets nothing where RM0433 says '1: resets' the block",
                 "no run accesses a block while its reset bit is 1 (spike/1206/data/main-8e15435/reset-windows.tsv: of the armed run's 82,558 "
                 "reset-register writes, the only accesses inside a window are 31 adcM1S2 reads, during APB1ResetLow and APB2Reset, which hold no "
                 "ADC bit; the fixture's 7,702 have none; the generated boots write no reset register), and no run "
                 "accesses a block whose reset alias has no registered peripheral (%s)" % ACCESS) for f in set("""RCC_AHB1RSTR.ADC12RST RCC_AHB1RSTR.DMA1RST RCC_AHB1RSTR.DMA2RST RCC_AHB1RSTR.ETH1MACRST RCC_AHB1RSTR.USB1OTGRST RCC_AHB1RSTR.USB2OTGRST RCC_AHB2RSTR.CRYPTRST RCC_AHB2RSTR.DCMIRST RCC_AHB2RSTR.HASHRST RCC_AHB2RSTR.RNGRST RCC_AHB2RSTR.SDMMC2RST RCC_AHB3RSTR.DMA2DRST RCC_AHB3RSTR.JPGDECRST RCC_AHB3RSTR.MDMARST RCC_AHB3RSTR.QSPIRST RCC_AHB3RSTR.SDMMC1RST RCC_AHB4RSTR.ADC3RST RCC_AHB4RSTR.BDMARST RCC_AHB4RSTR.CRCRST RCC_AHB4RSTR.GPIOARST RCC_AHB4RSTR.GPIOBRST RCC_AHB4RSTR.GPIOCRST RCC_AHB4RSTR.GPIODRST RCC_AHB4RSTR.GPIOERST RCC_AHB4RSTR.GPIOFRST RCC_AHB4RSTR.GPIOGRST RCC_AHB4RSTR.GPIOHRST RCC_AHB4RSTR.GPIOIRST RCC_AHB4RSTR.GPIOJRST RCC_AHB4RSTR.GPIOKRST RCC_AHB4RSTR.HSEMRST RCC_APB1HRSTR.CRSRST RCC_APB1HRSTR.FDCANRST RCC_APB1HRSTR.MDIOSRST RCC_APB1HRSTR.OPAMPRST RCC_APB1HRSTR.SWPRST RCC_APB1LRSTR.CECRST RCC_APB1LRSTR.DAC12RST RCC_APB1LRSTR.I2C1RST RCC_APB1LRSTR.I2C2RST RCC_APB1LRSTR.I2C3RST RCC_APB1LRSTR.LPTIM1RST RCC_APB1LRSTR.SPDIFRXRST RCC_APB1LRSTR.SPI2RST RCC_APB1LRSTR.SPI3RST RCC_APB1LRSTR.TIM12RST RCC_APB1LRSTR.TIM13RST RCC_APB1LRSTR.TIM14RST RCC_APB1LRSTR.TIM2RST RCC_APB1LRSTR.TIM3RST RCC_APB1LRSTR.TIM4RST RCC_APB1LRSTR.TIM5RST RCC_APB1LRSTR.TIM6RST RCC_APB1LRSTR.TIM7RST RCC_APB1LRSTR.UART4RST RCC_APB1LRSTR.UART5RST RCC_APB1LRSTR.UART7RST RCC_APB1LRSTR.UART8RST RCC_APB1LRSTR.USART2RST RCC_APB1LRSTR.USART3RST RCC_APB2RSTR.DFSDM1RST RCC_APB2RSTR.HRTIMRST RCC_APB2RSTR.SAI1RST RCC_APB2RSTR.SAI2RST RCC_APB2RSTR.SAI3RST RCC_APB2RSTR.SPI1RST RCC_APB2RSTR.SPI4RST RCC_APB2RSTR.SPI5RST RCC_APB2RSTR.TIM15RST RCC_APB2RSTR.TIM16RST RCC_APB2RSTR.TIM17RST RCC_APB2RSTR.TIM1RST RCC_APB2RSTR.TIM8RST RCC_APB2RSTR.USART1RST RCC_APB2RSTR.USART6RST RCC_APB3RSTR.LTDCRST RCC_APB4RSTR.COMP12RST RCC_APB4RSTR.I2C4RST RCC_APB4RSTR.LPTIM2RST RCC_APB4RSTR.LPTIM3RST RCC_APB4RSTR.LPTIM4RST RCC_APB4RSTR.LPTIM5RST RCC_APB4RSTR.LPUART1RST RCC_APB4RSTR.SAI4RST RCC_APB4RSTR.SPI6RST RCC_APB4RSTR.VREFRST""".split()) - set(ACK[30][1].split())})
for f in 'RCC_CR.HSI48ON RCC_CR.HSEON RCC_CR.PLL1ON RCC_CR.PLL2ON RCC_CR.PLL3ON'.split():
    SW[f] = ("the stock ready flag follows the ON bit at once, where silicon sets it once the oscillator is stable or the PLL locked (p340, p343, p347-348)",
             "each firmware reads the flag once after the enable and finds it set; ArduPilot's ChibiOS (Aethelgard 4400h's tree, ~/.cache/aet-4400h/src/ap/modules/ChibiOS/os/hal/ports/STM32/STM32H7xx/hal_lld.c, sha256 33372d9fb751) waits on it unbounded "
             "(L306 HSI48RDY, L299 HSERDY, L398 the PLLs' rdymask) and the generated firmware with bounded polls (cap-gen <board>/gen/board/"
             "firmware/src/main.rs L49-53 HSE_POLLS, PLL_POLLS; L44 PLL_POLLS on the HSI boards), whose next step is the same once the flag is "
             "set: only the polls and the time differ, and the bounded polls' timeout is taken on neither side. That PLL2 and PLL3 lock at all "
             "with VCOL above 2 MHz is A13's and A11's field evidence")
SW['RCC_CFGR.SW'] = ("the stock SWS follows SW at once, where silicon switches once the new clock is selected (p392)",
                     "the switch is to a clock already ready (HSI, or PLL1 after PLL1RDY) and the guard's TVI-RCC-SW judges its readiness; " + "ArduPilot's ChibiOS (Aethelgard 4400h's tree, ~/.cache/aet-4400h/src/ap/modules/ChibiOS/os/hal/ports/STM32/STM32H7xx/hal_lld.c, sha256 33372d9fb751) waits on SWS unbounded (L266, L465) and the generated firmware with SWS_POLLS (main.rs L53), whose next step is the same: only the polls and the time differ")
SW['I2C_CR1.RXIE'] = ("the stock receive interrupt also requires a read transfer (STM32F7_I2C.cs L463: receiveInterruptEnabled && isReadTransfer && "
                     "rxData.Count > 0), where silicon's is RXNE AND RXIE (p2003, p2011)",
                     "RXNE with RD_WRN = 0 never arises: the stock StartTransfer clears the receive queue (L344), and no run reads I2C_ISR in that state; "
                     "that a read's NBYTES all arrive at START is the read transfer's matter (TVI-WO-1206 FEEDBACK R4), not this field's")
SW['RCC_AHB4ENR.BKPRAMEN'] = ("the twin's backup SRAM is plain memory the enable never gates, where silicon clocks it by this bit (p458)",
                              "written 0 only, and no run is seen to access the backup SRAM; the access log covers peripherals, not memory, so that is not measured (a row)")
SW['DMA_SxCR.MINC'] = ("on a memory-to-peripheral stream with MINC = 0 and NDT > 1 the stock model sends the memory item once and then zeros, where silicon "
                       "sends the item NDT times (p663)",
                       "the armed run's 32 such streams (DMA1 stream 4, SPI1 TX, NDT 256, 0xFF) are the dummy bytes of Read commands to U4, the "
                       "stock GenericSpiFlash, which ignores MOSI while it reads (8,192 Read bytes handled), as is the fixture's one (stream 1, request 38, NDT 256); a device that reads MOSI there would see "
                       "the difference (a finding, TVI-WO-1206 FEEDBACK)")
SW['TIMx_EGR.UG'] = ("at UG silicon sets UIF whatever UIE (p1597, p1599) and clears the prescaler's sub-count; the stock model sets UIF only with UIE = 1 "
                     "and keeps the clock's fractional sub-count",
                     "no firmware reads TIMx_SR or CNT (%s); every UG comes with CR1 = 0, the counter stopped, ARR and PSC already in force on both sides; "
                     "the sub-count can shift the phase by under one counter tick at ArduPilot's second setup (7.77 s), which no firmware reads" % EVID)
SW['SPI_CR1.CSUSP'] = ("the stock model sets SR.EOT at a suspend, where silicon sets only SUSP (p2216)",
                       "only ArduPilot suspends, and it never reads SPI_SR (0 reads in the armed run and the fixture); the generated boards read SR but never "
                       "write CSUSP (gen-writes.tsv); EOTIE is always 0; chip selects are untouched (the multiplexer drops the stock FinishTransmission)")
SW['SPI_IFCR.EOTC'] = ("the stock model also clears CSTART and resets its frame counter, where silicon clears EOT and TXTF only (p2227)",
                       "CSTART is already 0 at every IFCR write, and SR is never read between EOTC and the disable")
SW['USART_CR3.DMAT'] = ("the twin's memory-to-peripheral stream moves the whole transfer at EN, unpaced by any request, where silicon moves one byte per free "
                        "transmit slot (p2059): the DMA guard's acknowledgement, ruled 2026-11-24T14:10Z Q5 (c) (requests 42, 46)",
                        "each stream's bytes go out in order; ArduPilot sees usart1's TX transfer-complete 2 us after EN where silicon would take about "
                        "1.8 ms for a 37-byte burst (computed: (NDT - 16) frames at 115,200 baud past the 16-entry FIFO), and 15 to 22 of the armed run's "
                        "102 usart1 bursts (by FIFO drain or by line idle) start before silicon's previous one would have finished, as do some of the "
                        "fixture's 28; whether ArduPilot then chooses different bytes to send is not measured: this is what Q5 (c) accepts")
SW['RCC_AHB1RSTR.USB1OTGRST'] = (SW['RCC_AHB1RSTR.USB1OTGRST'][0] + "; and the stock callback for this bit resets USB2's alias (STM32H7_RCC.cs L522), not USB1's",
                                 SW['RCC_AHB1RSTR.USB1OTGRST'][1])
HELD = set()  # the seven asked were ruled 2026-11-27T01:37Z (A25-A28)

if sys.argv[1:] == ['--table']:  # TVI-WO-1206.rm0433.md §3, from the same data as the rows
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
    vals = ','.join(sorted({hex(int(v, 0)) for v in raw if 'many' not in v}, key=lambda x: int(x, 16)) + [v for v in raw if 'many' in v]) or '-'
    vals = VALUES.get(f, vals)
    if len(vals.split(',')) > 8:
        n = len(vals.split(','))
        vals = ('every value (%d distinct written)' % n) if R[f].get('all_values') is True else ('the %d distinct values written (spike/1206/data/items.tsv)' % n)
    if f in owner:
        n = owner[f]
        print('\t'.join([f, 'acknowledged', str(n), 'acknowledged by name',
                         '%s; read for %s: %s (TVI-WO-1206 A%d; %s)' % (old_text(f), vals, ACK[n][0], n, cite(f))]))
    elif f in SW:
        what, why = SW[f]
        print('\t'.join([f, 'stock-wrong', '-', 'stock, wrong (%s; unaffected: %s)' % (what, why), '%s; read for %s (TVI-WO-1206; %s)' % (old_text(f), vals, cite(f))]))
    else:
        print('\t'.join([f, 'held', '-', '-', '-']))
