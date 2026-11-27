#!/usr/bin/env python3
# tests/1303/fields.py — the DMA guard's allow-list (RUL-16, RUL-17): model/GuardedSTM32DMA.cs's `Fields` rows, every bit
# 31:0 of every DMA1/DMA2 register, from RM0433 Rev 9 §15.5 (pp. 659-667) with the classes ruled 2026-11-24T14:10Z (Q2,
# Q5 (c), Q6), and TVI-WO-1303a's circular mode (ruled 2026-11-25T18:49Z, Q1-Q5; TVI-WO-1303a.measure.md §5). Stream registers are named DMA_SxNNN at stream 0's offset; the guard applies a row to all eight streams.
# Prints the rows; tests/1303/dma.sh `regen` checks the guard's table against this output byte for byte.
import sys
# TVI-WO-1206 readings BEGIN (spike/1206/apply.py writes this block; spike/1206/readings.py derives it)
READ_1206 = {
    ('DMA_HIFCR', 'CTCIF4'): ('acknowledged by name', "the stock model: writing 1 clears TCIFx and its interrupt (probed, TVI-WO-1303.measure.md D2; ArduPilot writes it); read for 0x0,0x1: a 1 clears the stream's TCIF and a 0 does nothing, on both sides; the guard then redraws the stream's interrupt line (TVI-WO-1206 A17; p657, p660, p661; STM32DMA.cs L114; STM32DMA.cs L115)"),
    ('DMA_HIFCR', 'CTCIF5'): ('acknowledged by name', "the stock model: writing 1 clears TCIFx and its interrupt (probed, TVI-WO-1303.measure.md D2; ArduPilot writes it); read for 0x0,0x1: a 1 clears the stream's TCIF and a 0 does nothing, on both sides; the guard then redraws the stream's interrupt line (TVI-WO-1206 A17; p657, p660, p661; STM32DMA.cs L114; STM32DMA.cs L115)"),
    ('DMA_HIFCR', 'CTCIF6'): ('acknowledged by name', "the stock model: writing 1 clears TCIFx and its interrupt (probed, TVI-WO-1303.measure.md D2; ArduPilot writes it); read for 0x0,0x1: a 1 clears the stream's TCIF and a 0 does nothing, on both sides; the guard then redraws the stream's interrupt line (TVI-WO-1206 A17; p657, p660, p661; STM32DMA.cs L114; STM32DMA.cs L115)"),
    ('DMA_HIFCR', 'CTCIF7'): ('acknowledged by name', "the stock model: writing 1 clears TCIFx and its interrupt (probed, TVI-WO-1303.measure.md D2; ArduPilot writes it); read for 0x0,0x1: a 1 clears the stream's TCIF and a 0 does nothing, on both sides; the guard then redraws the stream's interrupt line (TVI-WO-1206 A17; p657, p660, p661; STM32DMA.cs L114; STM32DMA.cs L115)"),
    ('DMA_LIFCR', 'CTCIF0'): ('acknowledged by name', "the stock model: writing 1 clears TCIFx and its interrupt (probed, TVI-WO-1303.measure.md D2; ArduPilot writes it); read for 0x0,0x1: a 1 clears the stream's TCIF and a 0 does nothing, on both sides; the guard then redraws the stream's interrupt line (TVI-WO-1206 A17; p657, p659, p661; STM32DMA.cs L105; STM32DMA.cs L106)"),
    ('DMA_LIFCR', 'CTCIF1'): ('acknowledged by name', "the stock model: writing 1 clears TCIFx and its interrupt (probed, TVI-WO-1303.measure.md D2; ArduPilot writes it); read for 0x0,0x1: a 1 clears the stream's TCIF and a 0 does nothing, on both sides; the guard then redraws the stream's interrupt line (TVI-WO-1206 A17; p657, p659, p661; STM32DMA.cs L105; STM32DMA.cs L106)"),
    ('DMA_LIFCR', 'CTCIF2'): ('acknowledged by name', "the stock model: writing 1 clears TCIFx and its interrupt (probed, TVI-WO-1303.measure.md D2; ArduPilot writes it); read for 0x0,0x1: a 1 clears the stream's TCIF and a 0 does nothing, on both sides; the guard then redraws the stream's interrupt line (TVI-WO-1206 A17; p657, p659, p661; STM32DMA.cs L105; STM32DMA.cs L106)"),
    ('DMA_LIFCR', 'CTCIF3'): ('acknowledged by name', "the stock model: writing 1 clears TCIFx and its interrupt (probed, TVI-WO-1303.measure.md D2; ArduPilot writes it); read for 0x0,0x1: a 1 clears the stream's TCIF and a 0 does nothing, on both sides; the guard then redraws the stream's interrupt line (TVI-WO-1206 A17; p657, p659, p661; STM32DMA.cs L105; STM32DMA.cs L106)"),
    ('DMA_SxCR', 'MINC'): ("stock, wrong (on a memory-to-peripheral stream with MINC = 0 and NDT > 1 the stock model sends the memory item once and then zeros, where silicon sends the item NDT times (p663); unaffected: the armed run's 32 such streams (DMA1 stream 4, SPI1 TX, NDT 256, 0xFF) are the dummy bytes of Read commands to U4, the stock GenericSpiFlash, which ignores MOSI while it reads (8,192 Read bytes handled); a device that reads MOSI there would see the difference (a finding, TVI-WO-1206 FEEDBACK))", 'the stock model: memory address increment (probed, D2); protected under EN; read for 0x0,0x1 (TVI-WO-1206; p646, p653, p657, p663; STM32DMA.cs L203; STM32DMA.cs L294)'),
    ('DMA_SxCR', 'MSIZE[1:0]'): ('acknowledged by name', "the stock model: the memory data size, byte, half-word or word (probed: word D2, half-word msize16, byte ArduPilot); 11 is reserved; protected under EN; read for 0x0,0x1: for the values written each request moves one item of PSIZE from an address stepped as written (PINC 0; MSIZE equal to PSIZE on every enabled stream; direct mode with DMDIS 0), as silicon's peripheral-to-memory transfers do (TVI-WO-1206 A18; p648, p663; STM32DMA.cs L205; STM32DMA.cs L365)"),
    ('DMA_SxCR', 'PINC'): ('acknowledged by name', "the stock model: peripheral address increment (probed, D2); protected under EN; read for 0x0: for the values written each request moves one item of PSIZE from an address stepped as written (PINC 0; MSIZE equal to PSIZE on every enabled stream; direct mode with DMDIS 0), as silicon's peripheral-to-memory transfers do (TVI-WO-1206 A18; p646, p663; STM32DMA.cs L202; STM32DMA.cs L295)"),
    ('DMA_SxCR', 'PSIZE[1:0]'): ('acknowledged by name', "the stock model: the peripheral data size, byte, half-word or word (probed: word D2, half-word msize16, byte ArduPilot); 11 is reserved; protected under EN; read for 0x0,0x1: for the values written each request moves one item of PSIZE from an address stepped as written (PINC 0; MSIZE equal to PSIZE on every enabled stream; direct mode with DMDIS 0), as silicon's peripheral-to-memory transfers do (TVI-WO-1206 A18; p648, p663; STM32DMA.cs L204; STM32DMA.cs L363)"),
    ('DMA_SxFCR', 'DMDIS'): ('acknowledged by name', "0 only: direct mode; FIFO mode is not audited; protected under EN; read for 0x0: for the values written each request moves one item of PSIZE from an address stepped as written (PINC 0; MSIZE equal to PSIZE on every enabled stream; direct mode with DMDIS 0), as silicon's peripheral-to-memory transfers do (TVI-WO-1206 A18; p648, p653, p667; STM32DMA.cs L230; STM32DMA.cs L354)"),
}
# TVI-WO-1206 readings END

RULING = "ruled 2026-11-24T14:10Z"
CIRCULAR = "ruled 2026-11-25T18:49Z"
Z, A = "Zero", "Any"


def stream_flags(reg, off, sec, page, streams, cls):
    """LISR/HISR (flags) or LIFCR/HIFCR (clears): each stream's group of six bits starts at 0, 6, 16 or 22 (p659-661)."""
    names = {}
    for g, s in zip([0, 6, 16, 22], streams):
        for k, n in enumerate(["FEIF", None, "DMEIF", "TEIF", "HTIF", "TCIF"]):
            if n:
                names[g + k] = n + str(s)
    clear = reg.endswith("IFCR")
    out = []
    for bit in range(31, -1, -1):
        if bit in names:
            n, a, c, b = cls(("C" if clear else "") + names[bit])
            out.append((reg, off, sec, n, bit, bit, a, c, b, page))
        elif out and out[-1][3].startswith("Res") and out[-1][5] == bit + 1:
            m = out[-1][4]
            out[-1] = (reg, off, sec, "Res%d_%d" % (m, bit), m, bit, Z, "reserved", "reserved: must be kept at reset value", page)
        else:
            out.append((reg, off, sec, "Res%d" % bit, bit, bit, Z, "reserved", "reserved: must be kept at reset value", page))
    return out


def flag(name):
    if name.startswith("TCIF"):
        return (name, A, "read-only", "read-only (silicon ignores a write; so does the guard's stock model): set when the stock model completes a transfer (probed, TVI-WO-1303.measure.md D2), at each wrap of a circular stream (p657; each wrap is inferred from p654 against p665), and by the guard when software clears EN while the stream runs (p654; TVI-WO-1305, ruled 2026-11-24T18:55Z Q6)")
    if name.startswith("HTIF"):
        return (name, A, "read-only", "read-only (silicon ignores a write): set by the guard once half of a circular stream's NDT has been transferred (p657), in each cycle (inferred from p647's 'the DMA requests continue to be served' with p657), served on read and cleared by CHTIFx; never set on a non-circular stream, where HTIE = 1 refuses (TVI-DMA-HALF; " + CIRCULAR + " Q3); the stock model never sets it")
    return (name, A, "read-only", "read-only (silicon ignores a write): the stock model never sets it; silicon sets TEIF, DMEIF and FEIF on errors, none modeled")


def clearbit(name):
    if name.startswith("CHTIF"):
        return (name, A, "modeled by the guard", "writing 1 clears the guard's HTIFx and its interrupt (p660-661); HTIF sets on circular streams only (" + CIRCULAR + " Q3)")
    if name.startswith("CTCIF"):
        return (name, A, "inherited, unaudited (acknowledged by name)", "the stock model: writing 1 clears TCIFx and its interrupt (probed, TVI-WO-1303.measure.md D2; ArduPilot writes it)")
    return (name, A, "stock, wrong (the stock model never sets TEIF, DMEIF or FEIF, so clearing one changes nothing, where silicon sets them on errors; acknowledged by name, " + RULING + " Q6; ArduPilot writes them)",
            "a stock tag: dropped; the flag it clears never sets in the twin")


def cr():
    reg, off, sec = "DMA_SxCR", 0x10, "§15.5.5"
    ack = lambda what: "stock, wrong (" + what + "; acknowledged by name, " + RULING + " Q6; ArduPilot writes it)"
    return [
        (reg, off, sec, "Res31_25", 31, 25, Z, "reserved", "reserved: must be kept at reset value", 662),
        (reg, off, sec, "MBURST[1:0]", 24, 23, Z, "tag", "00 only: a stock tag; burst transfers are not modeled", 662),
        (reg, off, sec, "PBURST[1:0]", 22, 21, Z, "tag", "00 only: a stock tag; burst transfers are not modeled", 662),
        (reg, off, sec, "TRBUFF", 20, 20, A, ack("TRBUFF is a stock tag: bufferable transfers are not modeled, and its value is kept by the guard and served on read"),
         "kept by the guard and served on read; no effect modeled", 662),
        (reg, off, sec, "CT", 19, 19, Z, "tag", "0 only: a stock tag; double-buffer mode is not modeled", 662),
        (reg, off, sec, "DBM", 18, 18, Z, "tag", "0 only: a stock tag; double-buffer mode is not modeled", 663),
        (reg, off, sec, "PL[1:0]", 17, 16, A, ack("PL is a stock tag: no priority between streams is modeled, and its value is kept by the guard and served on read"),
         "kept by the guard and served on read; no effect modeled; protected under EN (TVI-DMA-EN)", 663),
        (reg, off, sec, "PINCOS", 15, 15, Z, "tag", "0 only: a stock tag; the fixed 4-byte peripheral increment is not modeled", 663),
        (reg, off, sec, "MSIZE[1:0]", 14, 13, "Sizes", "inherited, unaudited (acknowledged by name)", "the stock model: the memory data size, byte, half-word or word (probed: word D2, half-word msize16, byte ArduPilot); 11 is reserved; protected under EN", 663),
        (reg, off, sec, "PSIZE[1:0]", 12, 11, "Sizes", "inherited, unaudited (acknowledged by name)", "the stock model: the peripheral data size, byte, half-word or word (probed: word D2, half-word msize16, byte ArduPilot); 11 is reserved; protected under EN", 663),
        (reg, off, sec, "MINC", 10, 10, A, "inherited, unaudited (acknowledged by name)", "the stock model: memory address increment (probed, D2); protected under EN", 663),
        (reg, off, sec, "PINC", 9, 9, A, "inherited, unaudited (acknowledged by name)", "the stock model: peripheral address increment (probed, D2); protected under EN", 663),
        (reg, off, sec, "CIRC", 8, 8, A, "modeled by the guard",
         "the guard (the stock model drops it): on a peripheral-to-memory stream, at the request that leaves NDT = 0, the programmed NDT is reloaded and requests go on being served (§15.3.10 p647; NDT p665); EN stays 1 and TCIF is set at each wrap (both inferred from p654 against p665: RM0433 states neither), HTIF at half (p657); at EN, refused on memory-to-peripheral (the twin moves it unpaced) and memory-to-memory (p645) (TVI-DMA-CIRC) and with an odd NDT (TVI-DMA-HALF); changed while the stream is enabled, TVI-DMA-CIRC (p664 states no rule; p665); "
         + CIRCULAR + " Q1-Q5", 664),
        (reg, off, sec, "DIR[1:0]", 7, 6, "Directions",
         "stock, wrong (a memory-to-peripheral transfer completes at the EN write, unpaced by any request, where silicon moves one item per request: acknowledged for requests 38 spi1_tx_dma, 40 spi2_tx_dma, 42 usart1_tx_dma and 46 usart3_tx_dma, as ArduPilot's recorded boot and armed run use them; a peripheral-to-memory stream on request 41 usart1_rx_dma, 43 usart2_rx_dma or 45 usart3_rx_dma moves the bytes the USART guard's ReceiveDmaRequest raises where the platform wires it, and nothing where it does not: acknowledged by name; any other request no model raises refuses TVI-DMA-REQUEST; "
         + RULING + " Q5 (c); 43, 45, 46 ruled 2026-11-24T18:55Z Q6)",
         "the stock model: 00 peripheral-to-memory, 01 memory-to-peripheral, 10 memory-to-memory (probed, D2, D9, D11); 11 is reserved; protected under EN", 664),
        (reg, off, sec, "PFCTRL", 5, 5, Z, "tag", "0 only: a stock tag; the peripheral as flow controller is not modeled", 664),
        (reg, off, sec, "TCIE", 4, 4, A, "modeled by the guard", "the transfer-complete interrupt: the guard drives the stream's line from its copy of TCIE and the stock model's TCIF, so a TCIE write moves the line (Table 116 p658), where the stock model recomputes its own line only when TCIF changes (STM32DMA.cs L119-137); ArduPilot's streams take it", 664),
        (reg, off, sec, "HTIE", 3, 3, A, "modeled by the guard",
         "the guard (the stock model drops it): the half-transfer interrupt of a circular stream, on the stream's line, which the guard drives (p657; Table 116 p658); 1 on a non-circular stream refuses at EN, and set while one is enabled (TVI-DMA-HALF): HTIF is modeled on circular streams only (" + CIRCULAR + " Q3)", 664),
        (reg, off, sec, "TEIE", 2, 2, A, ack("TEIE is a stock tag: no transfer error is modeled, so its interrupt never fires; its value is kept by the guard and served on read"),
         "kept by the guard and served on read; no transfer error is modeled", 664),
        (reg, off, sec, "DMEIE", 1, 1, A, ack("DMEIE is a stock tag: no direct-mode error is modeled, so its interrupt never fires; its value is kept by the guard and served on read"),
         "kept by the guard and served on read; no direct-mode error is modeled", 664),
        (reg, off, sec, "EN", 0, 0, A, "modeled by the guard",
         "the stock model starts the stream; at its rise the guard checks its sizes (TVI-DMA-SIZE, p663), its addresses (TVI-DMA-TCM, TVI-DMA-ADDRESS, p108, Table 7 p131), a reload (TVI-DMA-RELOAD, p665), its circular mode and half-transfer flag (TVI-DMA-CIRC, TVI-DMA-HALF) and its DMAMUX1 request (TVI-DMA-ROUTE, TVI-DMA-SHARED, TVI-DMA-REQUEST, TVI-DMA-MUX); while it is set, a changed protected field refuses (TVI-DMA-EN running, TVI-DMA-DONE once the twin completed the transfer); the guard clears it at the end of a non-circular peripheral-to-memory transfer, which requests pace (p654, p665; " + CIRCULAR + " Q4), and it stays 1 at a circular wrap (inferred from p654 against p665); the stock model never clears it at the end of a memory-to-peripheral or memory-to-memory transfer, which it completes at the EN write (stock-wrong, p665); cleared by software while the stream runs, the guard sets TCIF and restarts the stream's next transfer at its programmed addresses, as silicon's suspension does (p654, p666; TVI-WO-1305, ruled 2026-11-24T18:55Z Q6)", 665),
    ]


def rows():
    out = []
    out += stream_flags("DMA_LISR", 0x00, "§15.5.1", 659, [0, 1, 2, 3], flag)
    out += stream_flags("DMA_HISR", 0x04, "§15.5.2", 660, [4, 5, 6, 7], flag)
    out += stream_flags("DMA_LIFCR", 0x08, "§15.5.3", 660, [0, 1, 2, 3], clearbit)
    out += stream_flags("DMA_HIFCR", 0x0C, "§15.5.4", 661, [4, 5, 6, 7], clearbit)
    out += cr()
    out += [
        ("DMA_SxNDTR", 0x14, "§15.5.6", "Res31_16", 31, 16, Z, "reserved", "reserved: must be kept at reset value", 665),
        ("DMA_SxNDTR", 0x14, "§15.5.6", "NDT[15:0]", 15, 0, A, "modeled by the guard", "the stock model: the number of items, counted down by its transfers (probed, D2); reloaded with the programmed value by the guard at each wrap of a circular stream (p647, p665), otherwise never reloaded; protected under EN", 665),
        ("DMA_SxPAR", 0x18, "§15.5.7", "PAR[31:0]", 31, 0, A, "modeled by the guard", "the stock model's peripheral address (probed, D2, D9); at EN the guard refuses a span in ITCM or DTCM (p108) and one in no mapped region (Table 7 p131); protected under EN", 666),
        ("DMA_SxM0AR", 0x1C, "§15.5.8", "M0A[31:0]", 31, 0, A, "modeled by the guard", "the stock model's memory address (probed, D2); at EN the guard refuses a span in ITCM or DTCM (p108) and one in no mapped region (Table 7 p131); protected under EN", 666),
        ("DMA_SxM1AR", 0x20, "§15.5.9", "M1A[31:0]", 31, 0, Z, "tag", "0 only: a stock tag that reads 0; double-buffer mode is not modeled", 666),
        ("DMA_SxFCR", 0x24, "§15.5.10", "Res31_8", 31, 8, Z, "reserved", "reserved: must be kept at reset value", 667),
        ("DMA_SxFCR", 0x24, "§15.5.10", "FEIE", 7, 7, Z, "tag", "0 only: a stock tag; FIFO errors are not modeled", 667),
        ("DMA_SxFCR", 0x24, "§15.5.10", "Res6", 6, 6, Z, "reserved", "reserved: must be kept at reset value", 667),
        ("DMA_SxFCR", 0x24, "§15.5.10", "FS[2:0]", 5, 3, A, "read-only", "read-only: served by the guard as 100 (FIFO empty, the reset value; not relevant in direct mode); the stock model reads 000", 667),
        ("DMA_SxFCR", 0x24, "§15.5.10", "DMDIS", 2, 2, Z, "inherited, unaudited (acknowledged by name)", "0 only: direct mode; FIFO mode is not audited; protected under EN", 667),
        ("DMA_SxFCR", 0x24, "§15.5.10", "FTH[1:0]", 1, 0, A, "stock, wrong (the stock model resets FCR to 0x00 where silicon resets it to 0x21 (p667); the guard serves FCR from its copy, reset 0x21)", "kept by the guard and served on read; not used in direct mode (p667), the only mode allowed (DMDIS = 0); protected under EN", 667),
    ]
    return out


def lit(v):
    if isinstance(v, int):
        return "0x%02X" % v if v < 0x100 else "0x%X" % v
    return v


def q(s):
    return '"' + s.replace("\\", "\\\\").replace('"', '\\"') + '"'


def main():
    for r in rows():
        reg, off, sec, name, msb, lsb, allowed, cls, behaves, page = r
        cls, behaves = READ_1206.get((reg, name), (cls, behaves))  # TVI-WO-1206
        print("            R(%s, %s, %s, %s, %d, %d, %s, %s, %s, %d)," % (q(reg), lit(off), q(sec), q(name), msb, lsb, allowed, q(cls), q(behaves), page))


if __name__ == "__main__":
    main()
