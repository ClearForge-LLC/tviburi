//! Bare-metal probe for the SPI1 mode-fault guard. No dependencies; built with rustc alone.
//! Variant chosen at build time with `--cfg path="..."`:
//!   path="modf"    - master, software NSS, SSI=0: internal SS active, silicon raises MODF.
//!   path="modeled" - master, software NSS, SSI=1: internal SS inactive, no mode fault.
//! Register offsets: Renode's STM32H7_SPI `Registers` enum (measured by reflection).
//! Bit positions: RM0433 SPI register maps, matching the tag names Renode's model reports.
#![no_std]
#![no_main]

use core::ptr::write_volatile;

const RCC_APB2ENR: *mut u32 = 0x5802_44F0 as *mut u32;
const SPI1_CR1: *mut u32 = 0x4001_3000 as *mut u32;
const SPI1_CFG2: *mut u32 = 0x4001_300C as *mut u32;
/// Marker in DTCM, read back by the run script: the firmware got past the guarded write.
const MARK_AFTER: *mut u32 = 0x2000_0104 as *mut u32;

const CR1_SPE: u32 = 1 << 0;
const CFG2_MASTER: u32 = 1 << 22;
const CFG2_SSM: u32 = 1 << 26;

#[cfg(path = "modf")]
const CR1_SSI: u32 = 0;
#[cfg(path = "modeled")]
const CR1_SSI: u32 = 1 << 12;

#[no_mangle]
pub extern "C" fn reset() -> ! {
    unsafe {
        write_volatile(RCC_APB2ENR, 1 << 12); // SPI1EN
        write_volatile(SPI1_CFG2, CFG2_MASTER | CFG2_SSM);
        write_volatile(SPI1_CR1, CR1_SSI); // SSI before SPE, as the HAL does
        write_volatile(SPI1_CR1, CR1_SSI | CR1_SPE); // the guarded write
        write_volatile(MARK_AFTER, 0xAF7E_0002);
    }
    loop {
        core::hint::spin_loop();
    }
}

#[panic_handler]
fn panic(_: &core::panic::PanicInfo) -> ! {
    loop {}
}
