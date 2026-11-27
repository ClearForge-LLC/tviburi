#!/usr/bin/env python3
"""TVI-WO-1305: the USART guard's Fields table (RUL-16, RUL-17), generated, never typed: from RM0433 Rev 9's USART register
fields (tests/1305/data/rm0433-rev9-usart-fields.tsv, written by tests/1305/extract.py) and the classes ruled
2026-11-24T18:55Z (Q8, Q2 (c)). Prints the C# `R(...)` rows of model/GuardedSTM32F7_USART.cs's `Fields`; tests/1305/usart.sh
`regen` checks them byte for byte.
Usage: python3 -I tests/1305/fields.py tests/1305/data/rm0433-rev9-usart-fields.tsv"""
import sys
# TVI-WO-1206 readings BEGIN (spike/1206/apply.py writes this block; spike/1206/readings.py derives it)
READ_1206 = {
    ('USART_BRR', 'BRR'): ("stock, wrong (the twin's transmitter has no frame time: a TDR write sends the byte and sets TC at once, so BRR times no transmission (p2034-2035); unaffected: the bytes and their order are those silicon sends; a firmware polling USART_ISR sees TC = 1 at once (d1-drv8876 reads 0x206000C0 between back-to-back TDR writes) where silicon sets it a frame later, so it polls fewer times and takes the same branches; IDLE's frame time is the kernel clock over BRR in the guard (TVI-USART-IDLE); baud accuracy is Aethelgard's)", "the stock model stores it: its BaudRate, the frequency property over BRR (baud accuracy is Aethelgard's); IDLE's frame time is the kernel clock over BRR (p2034; TVI-USART-IDLE where the guard has no rule for the kernel clock); USARTDIV must be >= 16 (p2035): a start with BRR < 16, or BRR rewritten below 16 on a running port, is TVI-USART-BRR, unmodeled, but for ArduPilot's measured start with BRR = 0 and no DMA request, acknowledged by name until BRR >= 16 is written while no byte moves (ruled at TVI-WO-1305's merge, Q2; bench row U2); protected under UE (p2082): a change with UE = 1 confined to BRR, CR1.M0/PCE/FIFOEN and CR2.RXINV is ChibiOS re-running usart_init on an enabled port (sdStart without sdStop), acknowledged by name with no record: the stock model takes it at once; RM0433 does not say what silicon does, and thousands of H7 flight controllers run this ArduPilot RC-protocol probing and change baud, field evidence, not a page (ruled 2026-11-24T18:55Z Q2 (c); bench row U1); any other change with UE = 1 is TVI-USART-UE, unmodeled; read for the 11 distinct values written (spike/1206/data/items.tsv) (TVI-WO-1206; p417, p2026, p2034, p2035, p2082, p2099; STM32F7_USART.cs L209-211; STM32F7_USART.cs L50-51)"),
    ('USART_CR1', 'OVER8'): ('acknowledged by name', '0 only: the stock model stores it and halves its baud divisor; oversampling by 8 is not audited (no firmware writes it, TVI-WO-1305.measure.md §2.1); protected under UE (p2068): changed with UE = 1 it is TVI-USART-UE, unmodeled (ruled 2026-11-24T18:55Z Q2); read for 0x0: written 0 only: oversampling by 16, no receiver-timeout, transfer-complete or TX-FIFO interrupt, alike on both sides (TVI-WO-1206 A9; p2034, p2035, p2068, p2072; STM32F7_USART.cs L317; STM32F7_USART.cs L416)'),
    ('USART_CR1', 'PEIE'): ("stock, wrong (the twin raises no parity error, where silicon interrupts on a frame whose parity fails (p2042, p2069); unaffected: PCE = 1 only on ArduPilot's usart2 (CR1 0x2000151D, 0x2000153D), which receives no byte in the armed run (no RDR read); every other port runs PCE = 0, where both agree (spike/1206/data: the armed run (main-8e15435/reads.tsv), the 14 generated boots and the fixture (gen-reads.tsv)))", 'the stock model stores it; no parity error is ever raised, so it never fires; read for 0x0,0x1 (TVI-WO-1206; p2042, p2069, p2073, p2091; STM32F7_USART.cs L158; STM32F7_USART.cs L227)'),
    ('USART_CR1', 'PS'): ("stock, wrong (the twin neither generates nor checks parity, and its RDR bit 8 reads 0, where silicon's reads the received parity bit (p2042, p2069); unaffected: as PEIE: parity is on only on ArduPilot's usart2, which receives no byte in the armed run)", 'the stock model stores it (its ParityBit); parity is not checked; protected under UE (p2069): changed with UE = 1 it is TVI-USART-UE, unmodeled (ruled 2026-11-24T18:55Z Q2); read for 0x0 (TVI-WO-1206; p2042, p2069, p2072, p2098; STM32F7_USART.cs L159; STM32F7_USART.cs L77-81)'),
    ('USART_CR1', 'RTOIE'): ('acknowledged by name', '0 only: the stock receiver timeout runs on RTOIE alone, ignoring RTOEN, at 8 times RTO bit times (STM32F7_USART.cs L118-131; U9), so it is not modeled; read for 0x0: written 0 only: oversampling by 16, no receiver-timeout, transfer-complete or TX-FIFO interrupt, alike on both sides (TVI-WO-1206 A9; p2068, p2075, p2088; STM32F7_USART.cs L318; STM32F7_USART.cs L118)'),
    ('USART_CR1', 'RXFNEIE'): ("stock, wrong (the twin never sets ORE, so the interrupt never fires on an overrun as silicon's does (p2090); unaffected: an overrunning byte with the FIFO off refuses TVI-USART-OVERRUN; with the FIFO on, FIFOEN's own stock-wrong row holds it)", "the stock model's RXNEIE: fires while a received byte waits; read for 0x0,0x1 (TVI-WO-1206; p2018, p2029, p2030, p2069, p2073, p2086, p2090; STM32F7_USART.cs L155; STM32F7_USART.cs L232)"),
    ('USART_CR1', 'TCIE'): ('acknowledged by name', 'the stock model: TC set at each TDR write and when TE is cleared (U14); read for 0x0: written 0 only: oversampling by 16, no receiver-timeout, transfer-complete or TX-FIFO interrupt, alike on both sides (TVI-WO-1206 A9; p2026, p2069, p2073, p2085, p2089; STM32F7_USART.cs L156; STM32F7_USART.cs L398)'),
    ('USART_CR1', 'TXFNFIE'): ('acknowledged by name', "the stock model's TXEIE: TXFNF always reads 1 (U14), so it fires while set; read for 0x0: written 0 only: oversampling by 16, no receiver-timeout, transfer-complete or TX-FIFO interrupt, alike on both sides (TVI-WO-1206 A9; p2069, p2073, p2089, p2094; STM32F7_USART.cs L157; STM32F7_USART.cs L397)"),
    ('USART_CR2', 'STOP'): ("stock, wrong (silicon's frame and TC timing grow with the stop bits (p2021, p2076); the stock transmitter ignores them (the guard's IDLE frame time counts them); unaffected: STOP = 10 is written only on ArduPilot's usart2, which receives and sends no byte in the armed run (no RDR read; spike/1206/data: the armed run (main-8e15435/reads.tsv), the 14 generated boots and the fixture (gen-reads.tsv)))", "the stock model stores it (its StopBits); it enters IDLE's frame time; protected under UE (p2076): changed with UE = 1 it is TVI-USART-UE, unmodeled (ruled 2026-11-24T18:55Z Q2); read for 0x0,0x2 (TVI-WO-1206; p2021, p2024, p2026, p2034, p2076; STM32F7_USART.cs L175; STM32F7_USART.cs L57-65)"),
    ('USART_CR3', 'DMAT'): ("stock, wrong (the twin's memory-to-peripheral stream moves the whole transfer at EN, unpaced by any request, where silicon moves one byte per free transmit slot (p2059): the DMA guard's acknowledgement, ruled 2026-11-24T14:10Z Q5 (c) (requests 42, 46); unaffected: the bytes and their order are those silicon sends; ArduPilot sees usart1's TX transfer-complete 2 us after EN where silicon takes about 1.8 ms, and 15 of 102 usart1 bursts start before silicon's previous one would have finished: what Q5 (c) accepts)", "the stock model stores it; a memory-to-peripheral stream moves its bytes at the DMA's EN, unpaced (TVI-WO-1303 Q5 (c)); read for 0x0,0x1 (TVI-WO-1206; p604, p2018, p2057, p2058, p2081; STM32F7_USART.cs L187; STM32F7_USART.cs L23-24)"),
    ('USART_ICR', 'CMCF'): ('acknowledged by name', "clears a flag the twin never sets, so the write changes nothing; ChibiOS writes it at every init (ruled 2026-11-24T18:55Z Q8); read for 0x0,0x1: a 1 clears its ISR flag and a 0 does nothing, on both sides; the twin never sets the flag, which is its own read-only row's matter. The guard type also binds uart4/5/7/8, where some of these bits are reserved (Table 402, p2018); none is accessed in any run (TVI-WO-1206 A10; p2074, p2087, p2096; STM32F7_USART.cs L261; STM32F7_USART.cs L239)"),
    ('USART_ICR', 'CTSCF'): ('acknowledged by name', "clears a flag the twin never sets, so the write changes nothing; ChibiOS writes it at every init (ruled 2026-11-24T18:55Z Q8); read for 0x0,0x1: a 1 clears its ISR flag and a 0 does nothing, on both sides; the twin never sets the flag, which is its own read-only row's matter. The guard type also binds uart4/5/7/8, where some of these bits are reserved (Table 402, p2018); none is accessed in any run (TVI-WO-1206 A10; p2089, p2096, p2097; STM32F7_USART.cs L258; STM32F7_USART.cs L235)"),
    ('USART_ICR', 'EOBCF'): ('acknowledged by name', "clears a flag the twin never sets, so the write changes nothing; ChibiOS writes it at every init (ruled 2026-11-24T18:55Z Q8); read for 0x0,0x1: a 1 clears its ISR flag and a 0 does nothing, on both sides; the twin never sets the flag, which is its own read-only row's matter. The guard type also binds uart4/5/7/8, where some of these bits are reserved (Table 402, p2018); none is accessed in any run (TVI-WO-1206 A10; p2053, p2088, p2096, p2097; STM32F7_USART.cs L367; STM32F7_USART.cs L358)"),
    ('USART_ICR', 'FECF'): ('acknowledged by name', "clears a flag the twin never sets, so the write changes nothing; ChibiOS writes it at every init (ruled 2026-11-24T18:55Z Q8); read for 0x0,0x1: a 1 clears its ISR flag and a 0 does nothing, on both sides; the twin never sets the flag, which is its own read-only row's matter. The guard type also binds uart4/5/7/8, where some of these bits are reserved (Table 402, p2018); none is accessed in any run (TVI-WO-1206 A10; p2030, p2091, p2096, p2097; STM32F7_USART.cs L250; STM32F7_USART.cs L228)"),
    ('USART_ICR', 'LBDCF'): ('acknowledged by name', "clears a flag the twin never sets, so the write changes nothing; ChibiOS writes it at every init (ruled 2026-11-24T18:55Z Q8); read for 0x0,0x1: a 1 clears its ISR flag and a 0 does nothing, on both sides; the twin never sets the flag, which is its own read-only row's matter. The guard type also binds uart4/5/7/8, where some of these bits are reserved (Table 402, p2018); none is accessed in any run (TVI-WO-1206 A10; p2043, p2089, p2096, p2097; STM32F7_USART.cs L365; STM32F7_USART.cs L356)"),
    ('USART_ICR', 'NECF'): ('acknowledged by name', "clears a flag the twin never sets, so the write changes nothing; ChibiOS writes it at every init (ruled 2026-11-24T18:55Z Q8); read for 0x0,0x1: a 1 clears its ISR flag and a 0 does nothing, on both sides; the twin never sets the flag, which is its own read-only row's matter. The guard type also binds uart4/5/7/8, where some of these bits are reserved (Table 402, p2018); none is accessed in any run (TVI-WO-1206 A10; p2090, p2096, p2097; STM32F7_USART.cs L251; STM32F7_USART.cs L229)"),
    ('USART_ICR', 'ORECF'): ('acknowledged by name', "clears a flag the twin never sets, so the write changes nothing; ChibiOS writes it at every init (ruled 2026-11-24T18:55Z Q8); read for 0x0,0x1: a 1 clears its ISR flag and a 0 does nothing, on both sides; the twin never sets the flag, which is its own read-only row's matter. The guard type also binds uart4/5/7/8, where some of these bits are reserved (Table 402, p2018); none is accessed in any run (TVI-WO-1206 A10; p2030, p2090, p2096, p2097; STM32F7_USART.cs L252; STM32F7_USART.cs L230)"),
    ('USART_ICR', 'PECF'): ('acknowledged by name', "clears a flag the twin never sets, so the write changes nothing; ChibiOS writes it at every init (ruled 2026-11-24T18:55Z Q8); read for 0x0,0x1: a 1 clears its ISR flag and a 0 does nothing, on both sides; the twin never sets the flag, which is its own read-only row's matter. The guard type also binds uart4/5/7/8, where some of these bits are reserved (Table 402, p2018); none is accessed in any run (TVI-WO-1206 A10; p2042, p2091, p2096, p2097; STM32F7_USART.cs L249; STM32F7_USART.cs L227)"),
    ('USART_ICR', 'RTOCF'): ('acknowledged by name', "the stock model clears RTOF; read for 0x0,0x1: a 1 clears its ISR flag and a 0 does nothing, on both sides; the twin never sets the flag, which is its own read-only row's matter. The guard type also binds uart4/5/7/8, where some of these bits are reserved (Table 402, p2018); none is accessed in any run (TVI-WO-1206 A10; p2075, p2088, p2097; STM32F7_USART.cs L366; STM32F7_USART.cs L368)"),
    ('USART_ICR', 'TCBGTCF'): ('acknowledged by name', "clears a flag the twin never sets, so the write changes nothing; ChibiOS writes it at every init (ruled 2026-11-24T18:55Z Q8); read for 0x1: a 1 clears its ISR flag and a 0 does nothing, on both sides; the twin never sets the flag, which is its own read-only row's matter. The guard type also binds uart4/5/7/8, where some of these bits are reserved (Table 402, p2018); none is accessed in any run (TVI-WO-1206 A10; p2085, p2086, p2096, p2097; STM32F7_USART.cs L364; STM32F7_USART.cs L361)"),
    ('USART_ICR', 'TCCF'): ('acknowledged by name', "the stock model clears TC; read for 0x0,0x1: a 1 clears TC and a 0 does nothing, on both sides; every ISR read comes at least 37.5 ms after silicon's line would be idle, and TCIE is 0 (TVI-WO-1206 A24; p2089, p2097; STM32F7_USART.cs L255-257; STM32F7_USART.cs L265)"),
    ('USART_ICR', 'TXFECF'): ('acknowledged by name', "clears a flag the twin never sets, so the write changes nothing; ChibiOS writes it at every init (ruled 2026-11-24T18:55Z Q8); read for 0x0,0x1: a 1 clears its ISR flag and a 0 does nothing, on both sides; the twin never sets the flag, which is its own read-only row's matter. The guard type also binds uart4/5/7/8, where some of these bits are reserved (Table 402, p2018); none is accessed in any run (TVI-WO-1206 A10; p2066, p2085, p2086, p2096, p2097; STM32F7_USART.cs L254; STM32F7_USART.cs L245)"),
    ('USART_ICR', 'UDRCF'): ('acknowledged by name', "clears a flag the twin never sets, so the write changes nothing; ChibiOS writes it at every init (ruled 2026-11-24T18:55Z Q8); read for 0x0,0x1: a 1 clears its ISR flag and a 0 does nothing, on both sides; the twin never sets the flag, which is its own read-only row's matter. The guard type also binds uart4/5/7/8, where some of these bits are reserved (Table 402, p2018); none is accessed in any run (TVI-WO-1206 A10; p2078, p2088, p2097; STM32F7_USART.cs L260; STM32F7_USART.cs L237)"),
    ('USART_ICR', 'WUCF'): ('acknowledged by name', "clears a flag the twin never sets, so the write changes nothing; ChibiOS writes it at every init (ruled 2026-11-24T18:55Z Q8); read for 0x0,0x1: a 1 clears its ISR flag and a 0 does nothing, on both sides; the twin never sets the flag, which is its own read-only row's matter. The guard type also binds uart4/5/7/8, where some of these bits are reserved (Table 402, p2018); none is accessed in any run (TVI-WO-1206 A10; p2063, p2079, p2087, p2096; STM32F7_USART.cs L263; STM32F7_USART.cs L242)"),
    ('USART_TDR', 'TDR'): ("stock, wrong (TC is set at the write, TXFNF never drops, and a DMA stream is unpaced, where silicon times each frame (p2026, p2089); unaffected: the bytes and their order are those silicon sends for the run's 8N1 frames (all byte writes, each at most 0xFF); only flag timing differs)", 'the stock model sends its low byte (9-bit words refuse at CR1.M0); a byte or half-word write passes: ArduPilot\'s DMA writes it by bytes, though p2067 says "by words", field evidence (ruled 2026-11-24T18:55Z Q7); read for the 111 distinct values written (spike/1206/data/items.tsv) (TVI-WO-1206; p2018, p2021, p2023, p2024, p2025, p2026, p2057, p2067, p2089, p2098; STM32F7_USART.cs L271-275; STM32F7_USART.cs L374-383)'),
}
# TVI-WO-1206 readings END

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
                       '(U8), the transmitter never fills; with FIFOEN = 0, a byte arriving while the last waits unread is TVI-USART-OVERRUN, '
                       'unmodeled (p2030; ruled at TVI-WO-1305\'s merge, Q3); ' + REINIT % page),
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
                'IDLE\'s frame time is the kernel clock over BRR (p2034; TVI-USART-IDLE where the guard has no rule for the kernel clock); '
                'USARTDIV must be >= 16 (p2035): a start with BRR < 16, or BRR rewritten below 16 on a running port, is TVI-USART-BRR, '
                'unmodeled, but for ArduPilot\'s measured start with BRR = 0 and no DMA request, acknowledged by name until BRR >= 16 is '
                'written while no byte moves (ruled at TVI-WO-1305\'s merge, Q2; bench row U2); ' + REINIT % page)
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
    c, behaves = READ_1206.get((reg, name), (c, behaves))  # TVI-WO-1206
    print('            R(%s, %s, %s, %s, %s, %s, %s, %s, %s, %s),' % (cs(reg), off, cs(sec), cs(name), msb, lsb, allowed, cs(c), cs(behaves), page))
