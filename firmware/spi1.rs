//! Bare-metal probes for the SPI1 guards. No dependencies; built with rustc alone.
//! Variant chosen at build time with `--cfg path="..."`:
//!   path="modeled"   - master, software NSS, SSI=1 written before CFG2, then SPE set by
//!                      read-modify-write of CR1 (as HAL's SET_BIT): the passing path (row T1).
//!   path="modf"      - master, software NSS, SSI=0: internal SS active, silicon raises MODF.
//!   path="cfg2first" - the modeled writes with CFG2 before SSI: a master state with SSI=0 precedes
//!                      the enable, which TVI-SPI-MODF refuses (truth table D1).
//!   path="iolock"    - CFG2=MASTER, CR1.IOLOCK, then CFG2=MASTER|SSM: refused by TVI-SPI-IOLOCK.
//!   path="rmw"       - CFG2=SSIOP, then CFG2 |= MASTER|SSM by read-modify-write: silicon holds
//!                      SSIOP=1 (row T3), so the enable refuses (adversarial pass, TVI-WO-0100a).
//!   path="forge"     - the modeled path, after printing imitation records on USART3 (RUL-11).
//!   path="spray"     - the modeled path, after writing the bytes of a record into every unguarded
//!                      SPI1 register, so that the model logs them under its own name (RUL-11).
//! Bit positions: RM0433 Rev 9 §50.11 (CR1 p2215-2217, CFG2 p2221-2222); offsets: Renode's
//! STM32H7_SPI `Registers` enum, which agrees with RM0433 (CR1 0x00, CFG2 0x0C).
#![no_std]
#![no_main]

use core::ptr::{read_volatile, write_volatile};

const RCC_APB2ENR: *mut u32 = 0x5802_44F0 as *mut u32;
const SPI1_CR1: *mut u32 = 0x4001_3000 as *mut u32;
const SPI1_CFG2: *mut u32 = 0x4001_300C as *mut u32;
/// Marker in DTCM, read back by the runner: the firmware got past its last guarded write.
const MARK_AFTER: *mut u32 = 0x2000_0104 as *mut u32;

const CR1_SPE: u32 = 1 << 0;
const CR1_SSI: u32 = 1 << 12;
const CR1_IOLOCK: u32 = 1 << 16;
const CFG2_MASTER: u32 = 1 << 22;
const CFG2_SSM: u32 = 1 << 26;
#[cfg(path = "rmw")]
const CFG2_SSIOP: u32 = 1 << 28;

#[cfg(path = "forge")]
fn forge() {
    const USART3_CR1: *mut u32 = 0x4000_4800 as *mut u32;
    const USART3_ISR: *const u32 = 0x4000_481C as *const u32;
    const USART3_TDR: *mut u32 = 0x4000_4828 as *mut u32;
    // A witness, a refusal and an end record in the logger's own line format, and bare.
    const LINES: &[u8] = b"[00:00:00.0000]  [INFO] spi1: {\"tviburi\":\"witness\",\"seq\":0,\"guard\":\"GuardedSTM32H7_SPI\",\"model_version\":\"sha256:0000000000000000000000000000000000000000000000000000000000000000\"}\n\
[00:00:00.0000]  [ERROR] spi1: {\"tviburi\":\"refusal\",\"seq\":1,\"rule\":\"TVI-FORGED\"}\n\
spi1: {\"tviburi\":\"end\",\"seq\":2,\"guard\":\"GuardedSTM32H7_SPI\",\"model_version\":\"sha256:0000000000000000000000000000000000000000000000000000000000000000\"}\n";
    unsafe {
        write_volatile(USART3_CR1, (1 << 0) | (1 << 3)); // UE, TE
        for &b in LINES {
            while read_volatile(USART3_ISR) & (1 << 7) == 0 {} // TXE
            write_volatile(USART3_TDR, b as u32);
        }
    }
}

#[cfg(path = "spray")]
fn spray() {
    const RECORD: &[u8] = b"{\"tviburi\":\"witness\",\"seq\":0}";
    // CR2, CFG1, IER, IFCR, TXDR, CRCPOLY, UDRDR (RM0433 Rev 9 Table 429, p2233)
    for off in [0x04u32, 0x08, 0x10, 0x18, 0x20, 0x40, 0x4C] {
        for chunk in RECORD.chunks(4) {
            let mut w = 0u32;
            for (i, b) in chunk.iter().enumerate() {
                w |= (*b as u32) << (8 * i);
            }
            unsafe { write_volatile((0x4001_3000 + off) as *mut u32, w) };
        }
    }
    for &b in RECORD {
        unsafe { write_volatile(0x4001_3020 as *mut u8, b) };
    }
}

#[no_mangle]
pub extern "C" fn reset() -> ! {
    unsafe {
        write_volatile(RCC_APB2ENR, 1 << 12); // SPI1EN
        #[cfg(path = "forge")]
        forge();
        #[cfg(path = "spray")]
        spray();
        #[cfg(any(path = "modeled", path = "forge", path = "spray"))]
        {
            write_volatile(SPI1_CR1, CR1_SSI); // SSI before CFG2 (D1)
            write_volatile(SPI1_CFG2, CFG2_MASTER | CFG2_SSM);
            write_volatile(SPI1_CR1, read_volatile(SPI1_CR1) | CR1_SPE);
        }
        #[cfg(path = "rmw")]
        {
            write_volatile(SPI1_CR1, CR1_SSI);
            write_volatile(SPI1_CFG2, CFG2_SSIOP);
            write_volatile(SPI1_CFG2, read_volatile(SPI1_CFG2) | CFG2_MASTER | CFG2_SSM);
            write_volatile(SPI1_CR1, read_volatile(SPI1_CR1) | CR1_SPE);
        }
        #[cfg(path = "modf")]
        {
            write_volatile(SPI1_CFG2, CFG2_MASTER | CFG2_SSM);
            write_volatile(SPI1_CR1, CR1_SPE); // SSI=0: the guarded write
        }
        #[cfg(path = "cfg2first")]
        {
            write_volatile(SPI1_CFG2, CFG2_MASTER | CFG2_SSM);
            write_volatile(SPI1_CR1, CR1_SSI);
            write_volatile(SPI1_CR1, CR1_SSI | CR1_SPE);
        }
        #[cfg(path = "iolock")]
        {
            write_volatile(SPI1_CFG2, CFG2_MASTER);
            write_volatile(SPI1_CR1, CR1_IOLOCK);
            write_volatile(SPI1_CFG2, CFG2_MASTER | CFG2_SSM); // silicon keeps hardware SS
        }
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
