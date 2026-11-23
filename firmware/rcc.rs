//! Bare-metal probes for the RCC guard (TVI-WO-1300). No dependencies; built with rustc alone, as spi1.rs.
//! Variant chosen at build time with `--cfg path="..."`. Each path ends with SPI1's start (CR1 = SSI, CFG2 = MASTER|SSM,
//! CR1 |= SPE: the SPI guard's passing path), at which the test asks the RCC guard whether SPI1's kernel clock runs.
//!   path="clocked"   - PLL1 started (DIVN1/DIVQ1 written, PLL1ON, PLL1RDY polled), SPI123SEL left at 000 (pll1_q_ck),
//!                      SPI1EN: the kernel clock runs.
//!   path="scar"      - SPI1EN with SPI123SEL at 000 and PLL1 never started: no kernel clock (the scar, RUL-04).
//!   path="switch"    - SPI123SEL written 001 (pll2_p_ck) with PLL2 off: the mux cannot switch, no clock (p371).
//!   path="stuck"     - SPI123SEL written 100 (per_ck, whose hsi_ker_ck runs) while the mux's input pll1_q_ck has never
//!                      run: the switch does not take place, no clock (p371-372). The generated firmware's order.
//!   path="switched"  - PLL1 started first, then SPI123SEL written 100: both inputs run, the mux switches; the clock runs.
//!   path="alias"     - "clocked", with SPI1EN set through RCC_C1_APB2ENR (0x150): the same register (p382).
//!   path="field"     - "clocked", with SPI123SEL written 011 (I2S_CKIN, a pin the model does not have): refused by
//!                      TVI-RCC-FIELD, kind unmodeled.
//!   path="pll"       - "clocked", then DIVQ1EN cleared while PLL1 is on: silicon ignores the write (p401); refused by
//!                      TVI-RCC-PLL, kind predicted-fault.
//! Bits: RM0433 Rev 9 §8.7 (RCC_CR p383-386, PLLCFGR p400-401, PLL1DIVR p403-404, D2CCIP1R p414, APB2ENR p469).
#![no_std]
#![no_main]
#![allow(dead_code)]


use core::ptr::{read_volatile, write_volatile};

const RCC: usize = 0x5802_4400;
const RCC_CR: *mut u32 = RCC as *mut u32;
const RCC_PLLCFGR: *mut u32 = (RCC + 0x02C) as *mut u32;
const RCC_PLL1DIVR: *mut u32 = (RCC + 0x030) as *mut u32;
const RCC_D2CCIP1R: *mut u32 = (RCC + 0x050) as *mut u32;
const RCC_APB2ENR: *mut u32 = (RCC + 0x0F0) as *mut u32;
#[cfg(path = "alias")]
const RCC_C1_APB2ENR: *mut u32 = (RCC + 0x150) as *mut u32;
const SPI1_CR1: *mut u32 = 0x4001_3000 as *mut u32;
const SPI1_CFG2: *mut u32 = 0x4001_300C as *mut u32;
/// Marker in DTCM, read back by the test: the firmware got past its last guarded write.
const MARK_AFTER: *mut u32 = 0x2000_0104 as *mut u32;

const CR_PLL1ON: u32 = 1 << 24;
const CR_PLL1RDY: u32 = 1 << 25;
const SPI1EN: u32 = 1 << 12;
const SPI123SEL_SHIFT: u32 = 12;

unsafe fn pll1_on() {
    // DIVM1 stays at its reset 32 (hsi_ck 64 MHz / 32 = 2 MHz, PLL1RGE's reset range 1-2 MHz); DIVN1 = 399 (x400,
    // VCO 800 MHz), DIVP1 = 1 (/2), DIVQ1 = 3 (/4: pll1_q_ck 200 MHz), DIVR1 = 1. PLLCFGR stays at reset (DIVxyEN = 1).
    write_volatile(RCC_PLL1DIVR, (1 << 24) | (3 << 16) | (1 << 9) | 399);
    write_volatile(RCC_CR, read_volatile(RCC_CR) | CR_PLL1ON);
    while read_volatile(RCC_CR) & CR_PLL1RDY == 0 {}
}

unsafe fn spi123sel(v: u32) {
    write_volatile(RCC_D2CCIP1R, (read_volatile(RCC_D2CCIP1R) & !(7 << SPI123SEL_SHIFT)) | (v << SPI123SEL_SHIFT));
}

// The SPI guard's passing path (spi1.rs "modeled"): SSI before CFG2, so no master state with SSI = 0 precedes the enable.
unsafe fn spi1_start() {
    write_volatile(SPI1_CR1, 1 << 12); // SSI
    write_volatile(SPI1_CFG2, (1 << 22) | (1 << 26)); // MASTER, SSM
    write_volatile(SPI1_CR1, read_volatile(SPI1_CR1) | 1); // SPE: the start
}

#[no_mangle]
pub unsafe extern "C" fn reset() -> ! {
    #[cfg(any(path = "clocked", path = "field", path = "pll", path = "switched", path = "alias"))]
    pll1_on();
    #[cfg(path = "switch")]
    spi123sel(0b001);
    #[cfg(any(path = "stuck", path = "switched"))]
    spi123sel(0b100);
    #[cfg(path = "field")]
    spi123sel(0b011);
    #[cfg(path = "pll")]
    write_volatile(RCC_PLLCFGR, read_volatile(RCC_PLLCFGR) & !(1 << 17));
    #[cfg(not(path = "alias"))]
    write_volatile(RCC_APB2ENR, read_volatile(RCC_APB2ENR) | SPI1EN);
    #[cfg(path = "alias")]
    write_volatile(RCC_C1_APB2ENR, read_volatile(RCC_C1_APB2ENR) | SPI1EN);
    let _ = read_volatile(RCC_APB2ENR); // RM0433 p372 b): read back the enable before using the peripheral
    spi1_start();
    write_volatile(MARK_AFTER, 0xAF7E_0002);
    loop {}
}

#[panic_handler]
fn panic(_: &core::panic::PanicInfo) -> ! {
    loop {}
}
