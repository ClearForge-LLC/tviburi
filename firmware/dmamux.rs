//! Bare-metal probes for the DMAMUX1 guard (TVI-WO-1308). No dependencies; built with rustc alone, as dma.rs. Variant chosen
//! at build time with `--cfg path="..."`. Every path first enables DMA1's and DMA2's clocks (RCC_AHB1ENR DMA1EN bit 0, DMA2EN
//! bit 1: "DMAn and DMAMUX1 clock enable", RM0433 Rev 9 p455), then routes and starts streams, lets virtual time pass, and
//! writes the after-marker. Registers: RM0433 Rev 9 §15.5 (DMA, pp. 659-667) and §17.6 (DMAMUX1, pp. 708-714); pages in
//! tests/1308/dmamux.sh. `cfg(dma, s, ch, id, par, m0, items, cr)` writes DMAMUX1_CxCR (channel ch) = id, then the stream's
//! FCR = 0x21, PAR, M0AR, NDTR and CR without EN, as ArduPilot orders them for its SPI and ADC streams (CxCR before PAR,
//! TVI-WO-1308.measure.md §2.1); `en` sets EN by a read-modify-write, as ChibiOS does.
//! CR values: P2M bytes MINC 0x400; M2P bytes MINC 0x440. An M2P stream here moves NDT = 0 items (nothing reaches the peripheral).
//!   path="pair"     - DMA1 S0 P2M on channel 0 = 41 (usart1_rx_dma) with PAR = SPI1_RXDR: TVI-DMAMUX-PAIR, unmodeled, at the
//!                     EN write (WO acceptance: a stream started on a channel whose route names another request). MARK_READ =
//!                     S0CR after the EN write (reached only when dmamux1 is recorded: the stream starts, EN reads 1).
//!   path="pairtx"   - DMA1 S0 M2P on 42 (usart1_tx_dma) with PAR = USART3_TDR: TVI-DMAMUX-PAIR, unmodeled.
//!   path="pinc"     - DMA1 S0 P2M on 41 with PAR = USART1_RDR but PINC = 1, PSIZE = MSIZE = word, NDTR 4 (CR 0x5600): the span
//!                     walks to 0x28 (TDR), 0x2C and 0x30: TVI-DMAMUX-PAIR, unmodeled, at the EN write (ruled Q22 (a)).
//!   path="pinc1"    - the same with NDTR 1: the span is USART1_RDR alone: passes. MARK_READ = S0CR (EN = 1).
//!   path="routes"   - the armed run's ten streams on their channels and PARs (measure.md §2.1): DMA1 S0-S7 on C0-C7 = 41, 43,
//!                     45, 9, 38, 42, 40, 46; DMA2 S2, S3 on C10, C11 = 39, 37: passes. MARK_READ = C11CR, MARK_READ2 = C3CR.
//!   path="fixture"  - the fixture's six (measure.md §2.1): C0 = 41, C1 = 38, C2 = 39, C3 = 40, C4 = 42 (DMA1 S0-S4), C9 = 37
//!                     (DMA2 S1): passes. MARK_READ = C9CR.
//!   path="live"     - S0 P2M on 41 running, then C0CR rewritten 43: TVI-DMAMUX-LIVE, unmodeled.
//!   path="livedone" - S0 M2P on 42 with NDT = 4, completed by the twin at EN (EN still set), then C0CR rewritten 46:
//!                     TVI-DMAMUX-LIVE, unmodeled.
//!   path="livesame" - S0 P2M on 41 running, C0CR rewritten 41 three times (ArduPilot's pattern); S0 disabled; C0CR = 43 with
//!                     the stream off: passes. MARK_READ = C0CR (43).
//!   path="reserved" - C0CR = 54 (Reserved, Table 124 p697), taken at the write (ruled Q18 (a)); then DMA1 S0 P2M started on
//!                     it with PAR = USART1_RDR: the DMA guard's TVI-DMA-ROUTE, predicted-fault, at the EN write.
//!   path="parmem"   - DMA1 S0 P2M on 41 (usart1_rx_dma) with PAR = 0x24000000 (AXI SRAM): a PAR in memory, paced by the
//!                     request, passes (ruled Q18 (b)). MARK_READ = S0CR (EN = 1).
//!   path="se"       - C0CR = 41 with SE (bit 16): TVI-DMAMUX-FIELD, unmodeled.
//!   path="ge"       - RG3CR = GE (bit 16), judged by RG0CR's rows: TVI-DMAMUX-FIELD, unmodeled.
//!   path="byte"     - a byte write of 41 to C2CR, a byte write of 0 to its third byte (merged into the word: 41 stays), and a
//!                     half-word write of 43 to C1CR (p708), then S2 P2M on C2 with
//!                     PAR = USART1_RDR: passes. MARK_READ = C1CR | C2CR << 8 (words), MARK_READ2 = C2CR's low byte, a byte read.
#![no_std]
#![no_main]
#![allow(dead_code)]

use core::ptr::{read_volatile, write_volatile};

const RCC_AHB1ENR: *mut u32 = 0x5802_44D8 as *mut u32;
const DMA1: usize = 0x4002_0000;
const DMA2: usize = 0x4002_0400;
const DMAMUX1: usize = 0x4002_0800;
const SRAM4: usize = 0x3800_1900;
const SPI1_TXDR: usize = 0x4001_3020;
const SPI1_RXDR: usize = 0x4001_3030;
const SPI2_TXDR: usize = 0x4000_3820;
const SPI2_RXDR: usize = 0x4000_3830;
const USART1_RDR: usize = 0x4001_1024;
const USART1_TDR: usize = 0x4001_1028;
const USART2_RDR: usize = 0x4000_4424;
const USART3_RDR: usize = 0x4000_4824;
const USART3_TDR: usize = 0x4000_4828;
const ADC1_DR: usize = 0x4002_2040;
const MARK_AFTER: *mut u32 = 0x2000_0104 as *mut u32;
const MARK_READ: *mut u32 = 0x2000_0108 as *mut u32;
const MARK_READ2: *mut u32 = 0x2000_010C as *mut u32;
const CR: usize = 0x10;
const NDTR: usize = 0x14;
const PAR: usize = 0x18;
const M0AR: usize = 0x1C;
const FCR: usize = 0x24;
const P2M: u32 = 0x400;
const M2P: u32 = 0x440;

unsafe fn w(a: usize, v: u32) {
    write_volatile(a as *mut u32, v);
}

unsafe fn r(a: usize) -> u32 {
    read_volatile(a as *const u32)
}

fn s(dma: usize, n: usize, off: usize) -> usize {
    dma + n * 0x18 + off
}

/// Routes DMAMUX1 channel `ch` to request `id`, then stream `n` of `dma`: FCR, PAR, M0AR, NDTR and CR (`cr`, EN clear).
unsafe fn cfg(dma: usize, n: usize, ch: usize, id: u32, par: usize, m0: usize, items: u32, cr: u32) {
    w(DMAMUX1 + 4 * ch, id);
    w(s(dma, n, FCR), 0x21);
    w(s(dma, n, PAR), par as u32);
    w(s(dma, n, M0AR), m0 as u32);
    w(s(dma, n, NDTR), items);
    w(s(dma, n, CR), cr);
}

/// EN by a read-modify-write of CR.
unsafe fn en(dma: usize, n: usize) {
    w(s(dma, n, CR), r(s(dma, n, CR)) | 1);
}

/// One stream routed and started: P2M with 4 items, M2P with none.
unsafe fn start(dma: usize, n: usize, ch: usize, id: u32, par: usize, p2m: bool) {
    cfg(dma, n, ch, id, par, SRAM4 + 0x40 * ch, if p2m { 4 } else { 0 }, if p2m { P2M } else { M2P });
    en(dma, n);
}

/// Lets virtual time pass.
unsafe fn spin() {
    let mut i = 0u32;
    while i < 2000 {
        write_volatile(MARK_READ2, read_volatile(MARK_READ2));
        i += 1;
    }
}

#[no_mangle]
pub unsafe extern "C" fn reset() -> ! {
    write_volatile(RCC_AHB1ENR, 0x3);
    #[cfg(path = "pair")]
    {
        cfg(DMA1, 0, 0, 41, SPI1_RXDR, SRAM4, 4, P2M);
        en(DMA1, 0);
        write_volatile(MARK_READ, r(s(DMA1, 0, CR)));
    }
    #[cfg(any(path = "pinc", path = "pinc1"))]
    {
        #[cfg(path = "pinc")]
        cfg(DMA1, 0, 0, 41, USART1_RDR, SRAM4, 4, 0x5600);
        #[cfg(path = "pinc1")]
        cfg(DMA1, 0, 0, 41, USART1_RDR, SRAM4, 1, 0x5600);
        en(DMA1, 0);
        write_volatile(MARK_READ, r(s(DMA1, 0, CR)));
    }
    #[cfg(path = "pairtx")]
    {
        cfg(DMA1, 0, 0, 42, USART3_TDR, SRAM4, 4, M2P);
        en(DMA1, 0);
    }
    #[cfg(path = "routes")]
    {
        start(DMA1, 0, 0, 41, USART1_RDR, true);
        start(DMA1, 1, 1, 43, USART2_RDR, true);
        start(DMA1, 2, 2, 45, USART3_RDR, true);
        start(DMA1, 3, 3, 9, ADC1_DR, true);
        start(DMA1, 4, 4, 38, SPI1_TXDR, false);
        start(DMA1, 5, 5, 42, USART1_TDR, false);
        start(DMA1, 6, 6, 40, SPI2_TXDR, false);
        start(DMA1, 7, 7, 46, USART3_TDR, false);
        start(DMA2, 2, 10, 39, SPI2_RXDR, true);
        start(DMA2, 3, 11, 37, SPI1_RXDR, true);
        write_volatile(MARK_READ, r(DMAMUX1 + 4 * 11));
        write_volatile(MARK_READ2, r(DMAMUX1 + 4 * 3));
    }
    #[cfg(path = "fixture")]
    {
        start(DMA1, 0, 0, 41, USART1_RDR, true);
        start(DMA1, 1, 1, 38, SPI1_TXDR, false);
        start(DMA1, 2, 2, 39, SPI2_RXDR, true);
        start(DMA1, 3, 3, 40, SPI2_TXDR, false);
        start(DMA1, 4, 4, 42, USART1_TDR, false);
        start(DMA2, 1, 9, 37, SPI1_RXDR, true);
        write_volatile(MARK_READ, r(DMAMUX1 + 4 * 9));
    }
    #[cfg(path = "live")]
    {
        start(DMA1, 0, 0, 41, USART1_RDR, true);
        w(DMAMUX1, 43);
    }
    #[cfg(path = "livedone")]
    {
        cfg(DMA1, 0, 0, 42, USART1_TDR, SRAM4, 4, M2P);
        en(DMA1, 0);
        w(DMAMUX1, 46);
    }
    #[cfg(path = "livesame")]
    {
        start(DMA1, 0, 0, 41, USART1_RDR, true);
        w(DMAMUX1, 41);
        w(DMAMUX1, 41);
        w(DMAMUX1, 41);
        w(s(DMA1, 0, CR), P2M);
        w(DMAMUX1, 43);
        write_volatile(MARK_READ, r(DMAMUX1));
    }
    #[cfg(path = "reserved")]
    {
        w(DMAMUX1, 54);
        write_volatile(MARK_READ, r(DMAMUX1));
        cfg(DMA1, 0, 0, 54, USART1_RDR, SRAM4, 4, P2M);
        en(DMA1, 0);
    }
    #[cfg(path = "parmem")]
    {
        cfg(DMA1, 0, 0, 41, 0x2400_0000, SRAM4, 4, P2M);
        en(DMA1, 0);
        write_volatile(MARK_READ, r(s(DMA1, 0, CR)));
    }
    #[cfg(path = "se")]
    {
        w(DMAMUX1, 41 | 1 << 16);
    }
    #[cfg(path = "ge")]
    {
        w(DMAMUX1 + 0x10C, 1 << 16);
    }
    #[cfg(path = "byte")]
    {
        write_volatile((DMAMUX1 + 8) as *mut u8, 41);
        write_volatile((DMAMUX1 + 10) as *mut u8, 0); // C2CR's third byte (SE, SPOL, NBREQ at 0): merged, DMAREQ_ID kept
        write_volatile((DMAMUX1 + 4) as *mut u16, 43);
        w(s(DMA1, 2, FCR), 0x21);
        w(s(DMA1, 2, PAR), USART1_RDR as u32);
        w(s(DMA1, 2, M0AR), SRAM4 as u32);
        w(s(DMA1, 2, NDTR), 4);
        w(s(DMA1, 2, CR), P2M);
        en(DMA1, 2);
        write_volatile(MARK_READ, r(DMAMUX1 + 4) | r(DMAMUX1 + 8) << 8);
        write_volatile(MARK_READ2, read_volatile((DMAMUX1 + 8) as *const u8) as u32);
    }
    spin();
    write_volatile(MARK_AFTER, 0xAF7E_0002);
    loop {}
}

#[panic_handler]
fn panic(_: &core::panic::PanicInfo) -> ! {
    loop {}
}
