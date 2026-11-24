//! Bare-metal probes for the DMA guard (TVI-WO-1303). No dependencies; built with rustc alone, as timer.rs. Variant chosen
//! at build time with `--cfg path="..."`. Every path first enables DMA1's and DMA2's clocks (RCC_AHB1ENR DMA1EN bit 0,
//! DMA2EN bit 1, p455), then configures streams, lets virtual time pass, and writes the after-marker. Registers: RM0433
//! Rev 9 §15.5 (DMA, pp. 659-667) and §17.6 (DMAMUX1, p708); pages in tests/1303/dma.sh. `cfg(c, s, id, ...)` routes DMA1
//! stream s through DMAMUX1 channel s to request `id`, writes FCR = 0x21, PAR, M0AR and NDTR, then CR without EN; `en`
//! sets EN by a read-modify-write, as ChibiOS does (TVI-WO-1303.measure.md §2.1).
//! CR values: M2M words with both increments 0x5680; P2M bytes MINC 0x400; M2P bytes MINC 0x440.
//!   path="axi"      - M2M, AXI SRAM to AXI SRAM, 4 words: passes; MARK_READ = the destination's first word.
//!   path="tcm"      - the same with the destination in DTCM: TVI-DMA-TCM, predicted-fault (p108), at the EN write.
//!   path="itcm"     - P2M on request 41 with M0AR in ITCM: TVI-DMA-TCM.
//!   path="partcm"   - M2P on request 42 with PAR in DTCM: TVI-DMA-TCM (both buses, p108).
//!   path="below"    - M2M, M0AR = 0x1FFFFFF0 over 8 words, ending in DTCM: TVI-DMA-TCM.
//!   path="wrap"     - M2M, M0AR = 0xFFFFFFF0 over 8 words: TVI-DMA-ADDRESS (wraps past 0xFFFFFFFF), unmodeled.
//!   path="unmapped" - M2M, M0AR = 0xA0000000 (Table 7: reserved): TVI-DMA-ADDRESS, unmodeled.
//!   path="ndt0"     - M2M with M0AR in DTCM and NDT = 0: nothing is addressed (p665): passes.
//!   path="en"       - P2M on 41 running (no model raises it), then PAR rewritten: TVI-DMA-EN, predicted-fault (p666).
//!   path="enoff"    - P2M on 41 running, then one CR write clearing EN and changing DIR: TVI-DMA-EN (judged before it).
//!   path="done"     - M2P on 42 (the twin completes it at EN), then CR with DIR changed and EN still set: TVI-DMA-DONE.
//!   path="same"     - P2M on 41 running; PAR and CR written again unchanged; then disabled: passes.
//!   path="disable"  - ArduPilot's USART1 RX and TX patterns (measure.md §2.1): CR = 0x100416 / 0x100456, EN by
//!                     read-modify-write, TX disabled by clearing EN and TCIE in one write: passes. MARK_READ = S0CR
//!                     (0x100417: TRBUFF, PL... kept by the guard's copy), MARK_READ2 = S0FCR (0x21).
//!   path="width"    - a byte write to S0NDTR: TVI-DMA-WIDTH, unmodeled (p659).
//!   path="read"     - a read at 0xD0 (no register): TVI-DMA-READ, unmodeled.
//!   path="field"    - CR with CIRC = 1: TVI-DMA-FIELD, unmodeled.
//!   path="msize16"  - M2M half-words (PSIZE = MSIZE = 01), 4 items: passes; MARK_READ = the destination's first word.
//!   path="route0"   - M2P with DMAMUX1 channel 0 left 0 (no request, p703): TVI-DMA-ROUTE, predicted-fault.
//!   path="route54"  - P2M on request 54 (reserved, Table 124 p697): TVI-DMA-ROUTE, predicted-fault.
//!   path="mux"      - P2M on request 3 (dmamux1_req_gen2, a stock tag): TVI-DMA-MUX, unmodeled.
//!   path="shared"   - S0 P2M on 41 running, then S1 P2M on 41: TVI-DMA-SHARED, predicted-fault (p704).
//!   path="sharedx"  - DMA1 S0 P2M on 41 running, then DMA2 S0 (channel 8) on 41: TVI-DMA-SHARED across controllers.
//!   path="request"  - P2M on 61 (spi3_rx_dma: no model raises it): TVI-DMA-REQUEST, unmodeled.
//!   path="txother"  - M2P on 44 (usart2_tx_dma: not acknowledged): TVI-DMA-REQUEST, unmodeled.
//!   path="reload"   - M2M AXI to AXI, 4 words (completed at EN), disabled, then enabled again without NDTR: silicon
//!                     reloads NDT = 4 (p665), the stock model never does: TVI-DMA-RELOAD, unmodeled.
//!   path="reassert" - M2P on 42 (completed at EN), then CR written again unchanged with EN = 1: silicon cleared EN at the
//!                     end (p665), so this starts a new transfer the twin does not make: TVI-DMA-DONE, unmodeled.
#![no_std]
#![no_main]
#![allow(dead_code)]

use core::ptr::{read_volatile, write_volatile};

const RCC_AHB1ENR: *mut u32 = 0x5802_44D8 as *mut u32;
const DMA1: usize = 0x4002_0000;
const DMA2: usize = 0x4002_0400;
const DMAMUX1: usize = 0x4002_0800;
const AXI: usize = 0x2400_0000;
const SRAM4: usize = 0x3800_1900;
const DTCM: usize = 0x2000_0400;
const USART1_RDR: usize = 0x4001_1024;
const USART1_TDR: usize = 0x4001_1028;
const MARK_AFTER: *mut u32 = 0x2000_0104 as *mut u32;
const MARK_READ: *mut u32 = 0x2000_0108 as *mut u32;
const MARK_READ2: *mut u32 = 0x2000_010C as *mut u32;
const CR: usize = 0x10;
const NDTR: usize = 0x14;
const PAR: usize = 0x18;
const M0AR: usize = 0x1C;
const FCR: usize = 0x24;
const M2M: u32 = 0x5680;
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

/// Routes stream `n` of `dma` through DMAMUX1 channel `ch` to request `id` (none when `id` is None), then FCR, PAR, M0AR,
/// NDTR and CR (`cr`, EN clear).
unsafe fn cfg(dma: usize, n: usize, ch: usize, id: Option<u32>, par: usize, m0: usize, items: u32, cr: u32) {
    if let Some(i) = id {
        w(DMAMUX1 + 4 * ch, i);
    }
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
    w(AXI, 0x1111_1111);
    w(AXI + 4, 0x2222_2222);
    w(AXI + 8, 0x3333_3333);
    w(AXI + 12, 0x4444_4444);
    #[cfg(path = "axi")]
    {
        cfg(DMA1, 0, 0, None, AXI, AXI + 0x1000, 4, M2M);
        en(DMA1, 0);
        write_volatile(MARK_READ, r(AXI + 0x1000));
    }
    #[cfg(path = "tcm")]
    {
        cfg(DMA1, 0, 0, None, AXI, DTCM, 4, M2M);
        en(DMA1, 0);
    }
    #[cfg(path = "itcm")]
    {
        cfg(DMA1, 0, 0, Some(41), USART1_RDR, 0x100, 4, P2M);
        en(DMA1, 0);
    }
    #[cfg(path = "partcm")]
    {
        cfg(DMA1, 0, 0, Some(42), DTCM, SRAM4, 4, M2P);
        en(DMA1, 0);
    }
    #[cfg(path = "below")]
    {
        cfg(DMA1, 0, 0, None, AXI, 0x1FFF_FFF0, 8, M2M);
        en(DMA1, 0);
    }
    #[cfg(path = "wrap")]
    {
        cfg(DMA1, 0, 0, None, AXI, 0xFFFF_FFF0, 8, M2M);
        en(DMA1, 0);
    }
    #[cfg(path = "unmapped")]
    {
        cfg(DMA1, 0, 0, None, AXI, 0xA000_0000, 4, M2M);
        en(DMA1, 0);
    }
    #[cfg(path = "ndt0")]
    {
        cfg(DMA1, 0, 0, None, AXI, DTCM, 0, M2M);
        en(DMA1, 0);
    }
    #[cfg(path = "en")]
    {
        cfg(DMA1, 0, 0, Some(41), USART1_RDR, SRAM4, 4, P2M);
        en(DMA1, 0);
        w(s(DMA1, 0, PAR), USART1_RDR as u32 + 4);
    }
    #[cfg(path = "enoff")]
    {
        cfg(DMA1, 0, 0, Some(41), USART1_RDR, SRAM4, 4, P2M);
        en(DMA1, 0);
        w(s(DMA1, 0, CR), M2P);
    }
    #[cfg(path = "done")]
    {
        cfg(DMA1, 0, 0, Some(42), USART1_TDR, SRAM4, 4, M2P);
        en(DMA1, 0);
        w(s(DMA1, 0, CR), P2M | 1);
    }
    #[cfg(path = "same")]
    {
        cfg(DMA1, 0, 0, Some(41), USART1_RDR, SRAM4, 4, P2M);
        en(DMA1, 0);
        w(s(DMA1, 0, PAR), USART1_RDR as u32);
        w(s(DMA1, 0, CR), r(s(DMA1, 0, CR)));
        w(s(DMA1, 0, CR), r(s(DMA1, 0, CR)) & !1);
    }
    #[cfg(path = "disable")]
    {
        cfg(DMA1, 0, 0, Some(41), USART1_RDR, SRAM4, 0x40, 0x0010_0416);
        en(DMA1, 0);
        cfg(DMA1, 4, 4, Some(42), USART1_TDR, SRAM4 + 0x80, 0x15, 0x0010_0456);
        en(DMA1, 4);
        w(s(DMA1, 4, CR), r(s(DMA1, 4, CR)) & !0x11);
        write_volatile(MARK_READ, r(s(DMA1, 0, CR)));
        write_volatile(MARK_READ2, r(s(DMA1, 0, FCR)));
    }
    #[cfg(path = "width")]
    {
        write_volatile(s(DMA1, 0, NDTR) as *mut u8, 4);
    }
    #[cfg(path = "read")]
    {
        write_volatile(MARK_READ, r(DMA1 + 0xD0));
    }
    #[cfg(path = "field")]
    {
        cfg(DMA1, 0, 0, Some(41), USART1_RDR, SRAM4, 4, P2M | 0x100);
    }
    #[cfg(path = "msize16")]
    {
        cfg(DMA1, 0, 0, None, AXI, AXI + 0x2000, 4, 0x2E80);
        en(DMA1, 0);
        write_volatile(MARK_READ, r(AXI + 0x2000));
    }
    #[cfg(path = "route0")]
    {
        cfg(DMA1, 0, 0, None, USART1_TDR, SRAM4, 4, M2P);
        en(DMA1, 0);
    }
    #[cfg(path = "route54")]
    {
        cfg(DMA1, 0, 0, Some(54), USART1_RDR, SRAM4, 4, P2M);
        en(DMA1, 0);
    }
    #[cfg(path = "mux")]
    {
        cfg(DMA1, 0, 0, Some(3), USART1_RDR, SRAM4, 4, P2M);
        en(DMA1, 0);
    }
    #[cfg(path = "shared")]
    {
        cfg(DMA1, 0, 0, Some(41), USART1_RDR, SRAM4, 4, P2M);
        en(DMA1, 0);
        cfg(DMA1, 1, 1, Some(41), USART1_RDR, SRAM4 + 0x40, 4, P2M);
        en(DMA1, 1);
    }
    #[cfg(path = "sharedx")]
    {
        cfg(DMA1, 0, 0, Some(41), USART1_RDR, SRAM4, 4, P2M);
        en(DMA1, 0);
        cfg(DMA2, 0, 8, Some(41), USART1_RDR, SRAM4 + 0x40, 4, P2M);
        en(DMA2, 0);
    }
    #[cfg(path = "request")]
    {
        cfg(DMA1, 0, 0, Some(61), USART1_RDR, SRAM4, 4, P2M);
        en(DMA1, 0);
    }
    #[cfg(path = "txother")]
    {
        cfg(DMA1, 0, 0, Some(44), USART1_TDR, SRAM4, 4, M2P);
        en(DMA1, 0);
    }
    #[cfg(path = "reload")]
    {
        cfg(DMA1, 0, 0, None, AXI, AXI + 0x1000, 4, M2M);
        en(DMA1, 0);
        w(s(DMA1, 0, CR), M2M);
        en(DMA1, 0);
    }
    #[cfg(path = "reassert")]
    {
        cfg(DMA1, 0, 0, Some(42), USART1_TDR, SRAM4, 4, M2P);
        en(DMA1, 0);
        w(s(DMA1, 0, CR), r(s(DMA1, 0, CR)));
    }
    spin();
    write_volatile(MARK_AFTER, 0xAF7E_0002);
    loop {}
}

#[panic_handler]
fn panic(_: &core::panic::PanicInfo) -> ! {
    loop {}
}
