//! Bare-metal probes for the USART guard (TVI-WO-1305). No dependencies; built with rustc alone, as dma.rs. Variant chosen
//! at build time with `--cfg path="..."`. Every path first enables USART1's clock (RCC_APB2ENR USART1EN bit 4, p470; its
//! kernel clock stays rcc_pclk2, USART16SEL = 000, p417), then writes USART1 (0x4001_1000) and the after-marker. Registers:
//! RM0433 Rev 9 §48.8 (pp. 2067-2099); pages and rulings in tests/1305/usart.sh. `config` is the generated firmware's
//! sequence (TVI-WO-1305.measure.md §2.1): BRR, CR2 = 0, CR1 = FIFOEN|TE|RE, then the same with UE.
//!   path="config"     - the generated sequence: passes. MARK_READ = CR1 (0x2000000D, FIFOEN served from the copy).
//!   path="m1ue"       - config, then CR1 with M1 set while UE = 1: TVI-USART-UE, unmodeled (p2068).
//!   path="over8ue"    - config, then CR1 with OVER8 set while UE = 1: TVI-USART-UE, unmodeled (p2068).
//!   path="psue"       - config, then CR1 with PCE and PS set while UE = 1: TVI-USART-UE, unmodeled (p2069; PCE alone would be
//!                       the acknowledged re-init pattern, PS is not in it).
//!   path="psoff"      - the same PCE|PS change made with UE = 0, then UE: passes. MARK_READ = CR1.
//!   path="stopue"     - config, then CR2.STOP = 10 while UE = 1: TVI-USART-UE (p2076).
//!   path="reinit"     - config, then ChibiOS's usart_init on the enabled port: BRR, CR2 = RXINV, CR3, CR1 with M0|PCE|FIFOEN,
//!                       all with UE = 1: passes, acknowledged by name (ruled 2026-11-24T18:55Z Q2 (c)). MARK_READ = BRR (the
//!                       stock model takes the write), MARK_READ2 = CR2.
//!   path="reinitstop" - the same re-init also changing STOP: TVI-USART-UE at the CR2 write (outside the pattern).
//!   path="disable"    - configured with PCE|PS (8O, PS outside the acknowledged pattern), then CR1 = 0 (ChibiOS's sdStop: UE,
//!                       FIFOEN, PCE and PS cleared in one write): passes (judged as UE = 0).
//!   path="same"       - configured with CR2.STOP = 10 and CR1.PCE|PS, then BRR, CR2 and CR1 written again unchanged with
//!                       UE = 1: passes (RUL-18; STOP and PS lie outside the acknowledged pattern, so only "unchanged" lets them).
//!   path="ap"         - ArduPilot's USART1 sequence (measure.md §2.1): BRR 0x271, CR2 0x40, CR3 0xC1, CR1 0x2000013D, ICR all
//!                       ones, then CR1's read-modify-write clearing RXNEIE: passes. MARK_READ = CR1 (0x2000011D: FIFOEN and
//!                       IDLEIE kept, as silicon), MARK_READ2 = CR2 (0x40).
//!   path="width"      - a byte write to CR1: TVI-USART-WIDTH, unmodeled (p2067).
//!   path="tdrbyte"    - config, TC cleared, then a byte write to TDR: passes (ruled Q7). MARK_READ = ISR (TC set again).
//!   path="read"       - a read at 0x30 (no register): TVI-USART-READ, unmodeled.
//!   path="field"      - CR3.HDSEL = 1 with UE = 0: TVI-USART-FIELD, unmodeled (the allow-list).
//!   path="nine"       - CR1 with M0 = 1 and PCE = 0 (9 data bits), UE = 0: TVI-USART-FIELD, unmodeled.
//!   path="kernel"     - USART16SEL = 010 (pll3_q_ck, PLL3 off), then config: TVI-USART-KERNEL, predicted-fault (p2019, p371).
//!   path="brr0"       - ArduPilot's usart2 start (TVI-WO-1305a.measure.md §1), on USART1: usart_init with BRR = 0, sdStop,
//!                       usart_init with BRR = 0 again, then usart_init on the enabled port with BRR 0x271; then a TDR byte:
//!                       passes, the start acknowledged by name. MARK_READ = BRR (0x271), MARK_READ2 = ISR (TC: the byte sent).
//!   path="brr8"       - a start with BRR = 8: TVI-USART-BRR, unmodeled (p2035).
//!   path="idlenorule" - CSION, USART16SEL = 100 (csi_ker_ck, running), then config: TVI-USART-IDLE, unmodeled, at the start:
//!                       the guard has no rule for csi_ker_ck's frequency (TVI-WO-1305a (3)).
#![no_std]
#![no_main]
#![allow(dead_code)]

use core::ptr::{read_volatile, write_volatile};

const RCC_APB2ENR: *mut u32 = 0x5802_44F0 as *mut u32;
const RCC_CR: *mut u32 = 0x5802_4400 as *mut u32;
const RCC_D2CCIP2R: *mut u32 = 0x5802_4454 as *mut u32;
const USART1: usize = 0x4001_1000;
const CR1: usize = 0x00;
const CR2: usize = 0x04;
const CR3: usize = 0x08;
const BRR: usize = 0x0C;
const ISR: usize = 0x1C;
const ICR: usize = 0x20;
const TDR: usize = 0x28;
const MARK_AFTER: *mut u32 = 0x2000_0104 as *mut u32;
const MARK_READ: *mut u32 = 0x2000_0108 as *mut u32;
const MARK_READ2: *mut u32 = 0x2000_010C as *mut u32;
const FIFOEN_TE_RE: u32 = 0x2000_000C;

unsafe fn w(off: usize, v: u32) {
    write_volatile((USART1 + off) as *mut u32, v);
}

unsafe fn r(off: usize) -> u32 {
    read_volatile((USART1 + off) as *const u32)
}

/// The generated firmware's sequence: BRR, CR2 = 0, CR1 without UE, then with UE.
unsafe fn config() {
    w(BRR, 0x22C);
    w(CR2, 0);
    w(CR1, FIFOEN_TE_RE);
    w(CR1, FIFOEN_TE_RE | 1);
}

/// ChibiOS's usart_init with BRR = 0, as ArduPilot's armed run starts usart2 at 0.2017 s (TVI-WO-1305a.measure.md §1).
unsafe fn init0() {
    w(BRR, 0);
    w(CR2, 0x40);
    w(CR3, 0x1);
    w(CR1, 0x12D);
    w(ICR, 0xFFFF_FFFF);
}

#[no_mangle]
pub unsafe extern "C" fn reset() -> ! {
    #[cfg(path = "kernel")]
    write_volatile(RCC_D2CCIP2R, 2 << 3);
    #[cfg(path = "idlenorule")]
    {
        write_volatile(RCC_CR, 1 << 7 | 1);
        write_volatile(RCC_D2CCIP2R, 4 << 3);
    }
    write_volatile(RCC_APB2ENR, 1 << 4);
    #[cfg(any(path = "config", path = "kernel", path = "idlenorule"))]
    {
        config();
        write_volatile(MARK_READ, r(CR1));
    }
    #[cfg(path = "m1ue")]
    {
        config();
        w(CR1, r(CR1) | 1 << 28);
    }
    #[cfg(path = "over8ue")]
    {
        config();
        w(CR1, r(CR1) | 1 << 15);
    }
    #[cfg(path = "psue")]
    {
        config();
        w(CR1, r(CR1) | 3 << 9);
    }
    #[cfg(path = "psoff")]
    {
        config();
        w(CR1, FIFOEN_TE_RE | 3 << 9);
        w(CR1, FIFOEN_TE_RE | 3 << 9 | 1);
        write_volatile(MARK_READ, r(CR1));
    }
    #[cfg(path = "stopue")]
    {
        config();
        w(CR2, 2 << 12);
    }
    #[cfg(path = "reinit")]
    {
        config();
        w(BRR, 0x2D0);
        w(CR2, 1 << 16);
        w(CR3, 0);
        w(CR1, FIFOEN_TE_RE | 1 << 12 | 1 << 10 | 1);
        write_volatile(MARK_READ, r(BRR));
        write_volatile(MARK_READ2, r(CR2));
    }
    #[cfg(path = "reinitstop")]
    {
        config();
        w(BRR, 0x2D0);
        w(CR2, 1 << 16 | 2 << 12);
    }
    #[cfg(path = "disable")]
    {
        w(BRR, 0x22C);
        w(CR1, FIFOEN_TE_RE | 3 << 9);
        w(CR1, FIFOEN_TE_RE | 3 << 9 | 1);
        w(CR1, 0);
        write_volatile(MARK_READ, r(CR1));
    }
    #[cfg(path = "same")]
    {
        w(BRR, 0x22C);
        w(CR2, 2 << 12);
        w(CR1, FIFOEN_TE_RE | 3 << 9);
        w(CR1, FIFOEN_TE_RE | 3 << 9 | 1);
        w(BRR, 0x22C);
        w(CR2, 2 << 12);
        w(CR1, FIFOEN_TE_RE | 3 << 9 | 1);
    }
    #[cfg(path = "ap")]
    {
        w(BRR, 0x271);
        w(CR2, 0x40);
        w(CR3, 0xC1);
        w(CR1, 0x2000_013D);
        w(ICR, 0xFFFF_FFFF);
        w(CR1, r(CR1) & !(1 << 5));
        write_volatile(MARK_READ, r(CR1));
        write_volatile(MARK_READ2, r(CR2));
    }
    #[cfg(path = "brr0")]
    {
        init0();
        w(CR1, 0);
        w(CR2, 0);
        w(CR3, 0);
        init0();
        let _ = (r(CR2), r(CR3), r(CR1));
        w(BRR, 0x271);
        w(CR2, 0x40);
        w(CR3, 0xC1);
        w(CR1, 0x2000_013D);
        w(ICR, 0xFFFF_FFFF);
        w(CR1, r(CR1) & !(1 << 5));
        w(ICR, 1 << 6);
        write_volatile((USART1 + TDR) as *mut u8, 0x41);
        write_volatile(MARK_READ, r(BRR));
        write_volatile(MARK_READ2, r(ISR));
    }
    #[cfg(path = "brr8")]
    {
        w(BRR, 8);
        w(CR2, 0);
        w(CR1, FIFOEN_TE_RE);
        w(CR1, FIFOEN_TE_RE | 1);
    }
    #[cfg(path = "width")]
    {
        write_volatile(USART1 as *mut u8, 0x0C);
    }
    #[cfg(path = "tdrbyte")]
    {
        config();
        w(ICR, 1 << 6);
        write_volatile((USART1 + TDR) as *mut u8, 0x41);
        write_volatile(MARK_READ, r(ISR));
    }
    #[cfg(path = "read")]
    {
        write_volatile(MARK_READ, read_volatile((USART1 + 0x30) as *const u32));
    }
    #[cfg(path = "field")]
    {
        w(CR3, 1 << 3);
    }
    #[cfg(path = "nine")]
    {
        w(CR1, FIFOEN_TE_RE | 1 << 12);
    }
    write_volatile(MARK_AFTER, 0xAF7E_0002);
    loop {}
}

#[panic_handler]
fn panic(_: &core::panic::PanicInfo) -> ! {
    loop {}
}
