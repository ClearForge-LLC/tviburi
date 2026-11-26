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
//!   path="idlenorule" - PLL2 with DIVN2 = 2 (a 6 MHz VCO, outside p407's 192-960 MHz), USART16SEL = 001 (pll2_q_ck, running),
//!                       then config: TVI-USART-IDLE, unmodeled, at the start: the RCC guard's KernelHz has no rule for its
//!                       frequency (TVI-WO-1305a (3); re-planted from csi_ker_ck, which now has one, by TVI-WO-1300c Q6).
//! TVI-WO-1300c: the one frequency rule, read through usart1 (the RCC guard's KernelHz; ruled 2026-11-26T04:03Z). Each sets a clock
//! tree, then config; a source with a frequency passes and the case times IDLE after a byte, one with none refuses at the start.
//!   path="hzcsi"      - CSION, USART16SEL = 100: csi_ker_ck, 4 MHz (a source the USART guard's copy refused; the union).
//!   path="hzsyscsi"   - CSION, sys_ck switched to the CSI (SW = 001): rcc_pclk2 = 4 MHz (the union).
//!   path="hzsyshse"   - HSEON, sys_ck switched to the HSE (SW = 010): rcc_pclk2 = 8 MHz, the board's hseFrequency (the union).
//!   path="hzcsiref"   - CSION, PLL2 from the CSI (PLLSRC = 01, DIVM2 = 2: a 2 MHz reference), x100 = 200 MHz, DIVQ2 /2:
//!                       pll2_q_ck = 100 MHz (a reference the USART and ADC guards' copies refused; the union).
//!   path="hzvcol"     - ArduPilot's PLL2 (PLLCKSELR 0x00408040, PLL2DIVR 0x0704022C, PLLCFGR 0x01BF0FFD: an 8 MHz reference on
//!                       VCOL, a 360 MHz VCO), USART16SEL = 001: pll2_q_ck = 72 MHz, acknowledged by name (Q1; p346, p347).
//!                       MARK_READ2 = RCC_PLLCFGR (0x01BF0FFD, as written: the RCC guard serves RGE, VCOSEL and FRACEN from its copy).
//!   path="hzwide"     - PLL2DIVR written once with DIVN2 = 359, which the stock model keeps as 103: silicon's 9 bits hold it,
//!                       so pll2_q_ck = 2 MHz x 360 / 10 = 72 MHz (Q2).
//!   path="hzdivr"     - the same, then PLL2DIVR written back as read (a read-modify-write of the stock's narrowed bits): no rule
//!                       (Q2): TVI-USART-IDLE at the start.
//!   path="hzpllon"    - (the RCC recorded) PLL2 on, then PLL2DIVR changed while it runs (TVI-RCC-PLL, a would-refuse; silicon
//!                       ignores the write): no rule (Q2): TVI-USART-IDLE at the start.
//!   path="hzcfgon"    - PLL2 from an 8 MHz reference (PLL2RGE 11) running, then PLLCFGR written with PLL2VCOSEL = 1: a change
//!                       p347 forbids once the PLL is on (TVI-RCC-PLL does not judge it), silicon keeps VCOH: no rule (the adversarial
//!                       pass): TVI-USART-IDLE at the start.
//!   path="hzvcoh"     - HSIDIV /2, PLL2 from 32 MHz / DIVM2 32 = a 1 MHz reference on VCOH: no rule (p346, p347; Q1).
//!   path="hzrge"      - PLL2 from an 8 MHz reference with PLL2RGE left at 00 (1-2 MHz): no rule (pp. 401-402; Q3).
//!   path="hzpll3q"    - PLL3 on, USART16SEL = 010 (pll3_q_ck, running): no rule on a USART (the WO's NOT list; Q10).
#![no_std]
#![no_main]
#![allow(dead_code)]

use core::ptr::{read_volatile, write_volatile};

const RCC_APB2ENR: *mut u32 = 0x5802_44F0 as *mut u32;
const RCC_CR: *mut u32 = 0x5802_4400 as *mut u32;
const RCC_D2CCIP2R: *mut u32 = 0x5802_4454 as *mut u32;
const RCC_CFGR: *mut u32 = 0x5802_4410 as *mut u32;
const RCC_PLLCKSELR: *mut u32 = 0x5802_4428 as *mut u32;
const RCC_PLLCFGR: *mut u32 = 0x5802_442C as *mut u32;
const RCC_PLL2DIVR: *mut u32 = 0x5802_4438 as *mut u32;
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

/// An RCC_CR bit set, then its ready flag (the next bit up, p383-386) awaited.
unsafe fn on(bit: u32) {
    write_volatile(RCC_CR, read_volatile(RCC_CR) | 1 << bit);
    while read_volatile(RCC_CR) >> (bit + 1) & 1 == 0 {}
}

/// sys_ck switched (RCC_CFGR.SW, p393), SWS awaited.
unsafe fn switch(sw: u32) {
    write_volatile(RCC_CFGR, (read_volatile(RCC_CFGR) & !7) | sw);
    while read_volatile(RCC_CFGR) >> 3 & 7 != sw {}
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
    #[cfg(any(path = "hzcsi", path = "hzsyscsi", path = "hzcsiref"))]
    on(7);                                                    // CSION
    #[cfg(path = "hzcsiref")]
    {
        write_volatile(RCC_PLLCKSELR, 0x0200_2201);           // PLLSRC = 01 (csi_ck), DIVM2 = 2; DIVM1, DIVM3 at reset (p398-399)
        write_volatile(RCC_PLL2DIVR, 0x0101_0063);            // x100 = 200 MHz, DIVQ2 /2
    }
    #[cfg(path = "hzcsi")]
    write_volatile(RCC_D2CCIP2R, 4 << 3);                     // USART16SEL = 100, csi_ker_ck
    #[cfg(path = "hzsyscsi")]
    switch(1);
    #[cfg(path = "hzsyshse")]
    {
        on(16);                                               // HSEON
        switch(2);
    }
    #[cfg(path = "hzvcoh")]
    write_volatile(RCC_CR, read_volatile(RCC_CR) | 1 << 3);   // HSIDIV = 01: hsi_ck 32 MHz (p385)
    #[cfg(any(path = "hzvcol", path = "hzrge", path = "hzpllon", path = "hzcfgon"))]
    write_volatile(RCC_PLLCKSELR, 0x0040_8040);               // ArduPilot's: HSI, DIVM2 = 8 (an 8 MHz reference)
    #[cfg(path = "hzvcol")]
    write_volatile(RCC_PLLCFGR, 0x01BF_0FFD);                 // ArduPilot's: PLLxRGE 11, PLL2VCOSEL = PLL3VCOSEL = 1
    #[cfg(any(path = "hzpllon", path = "hzcfgon"))]
    write_volatile(RCC_PLLCFGR, 0x01FF_00C0);                 // PLL2RGE = 11
    #[cfg(any(path = "hzvcol", path = "hzrge", path = "hzpllon", path = "hzcfgon"))]
    write_volatile(RCC_PLL2DIVR, 0x0704_022C);                // ArduPilot's: x45 = 360 MHz, DIVQ2 /5
    #[cfg(any(path = "hzwide", path = "hzdivr"))]
    write_volatile(RCC_PLL2DIVR, 0x0109_0367);                // DIVN2 = 359 (x360 = 720 MHz), DIVQ2 /10 (p407)
    #[cfg(path = "hzdivr")]
    write_volatile(RCC_PLL2DIVR, read_volatile(RCC_PLL2DIVR)); // written back as the stock model reads it
    #[cfg(path = "hzvcoh")]
    write_volatile(RCC_PLL2DIVR, 0x0101_00C7);                // x200 = 200 MHz on VCOH, DIVQ2 /2
    #[cfg(path = "idlenorule")]
    write_volatile(RCC_PLL2DIVR, 0x0101_0002);                // DIVN2 = 2 (x3): a 6 MHz VCO
    #[cfg(any(path = "hzvcol", path = "hzrge", path = "hzpllon", path = "hzwide", path = "hzdivr", path = "hzvcoh", path = "idlenorule", path = "hzcsiref", path = "hzcfgon"))]
    {
        on(26);                                               // PLL2ON
        write_volatile(RCC_D2CCIP2R, 1 << 3);                 // USART16SEL = 001, pll2_q_ck
    }
    #[cfg(path = "hzpllon")]
    write_volatile(RCC_PLL2DIVR, 0x0704_022D);                // DIVN2 changed while PLL2 runs
    #[cfg(path = "hzcfgon")]
    write_volatile(RCC_PLLCFGR, 0x01FF_00E0);                 // PLL2VCOSEL = 1 while PLL2 runs (p347)
    #[cfg(path = "hzpll3q")]
    {
        on(28);                                               // PLL3ON, its reset dividers: 2 MHz x 129, DIVQ3 /2
        write_volatile(RCC_D2CCIP2R, 2 << 3);                 // USART16SEL = 010, pll3_q_ck
    }
    write_volatile(RCC_APB2ENR, 1 << 4);
    #[cfg(any(path = "config", path = "kernel", path = "idlenorule", path = "hzcsi", path = "hzsyscsi", path = "hzsyshse", path = "hzvcol", path = "hzcsiref", path = "hzcfgon",
              path = "hzwide", path = "hzdivr", path = "hzpllon", path = "hzvcoh", path = "hzrge", path = "hzpll3q"))]
    {
        config();
        write_volatile(MARK_READ, r(CR1));
        #[cfg(path = "hzvcol")]
        write_volatile(MARK_READ2, read_volatile(RCC_PLLCFGR)); // RGE, VCOSEL and FRACEN served from the RCC guard's copy
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
