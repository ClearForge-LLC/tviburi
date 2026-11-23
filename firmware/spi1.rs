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
//!   path="spray"     - the modeled path, then the bytes of a record written into SPI1's registers,
//!                      so that the model logs them under its own name (RUL-11).
//!   path="ardupilot" - ArduPilot's ChibiOS SPI driver on SPI1, as measured (TVI-WO-0100b.rm0433.md
//!                      §1.1): spi_lld_start, spi_lld_wait_complete, spi_lld_exchange, the DMA RX
//!                      interrupt and spi_lld_stop, twice, with each read-modify-write the ELF does
//!                      (decoded at 0x8100e7c-ebc, 0x8100d28-d32, 0x8101082-88, 0x8100cba-cc0,
//!                      0x8100f72-84). The DMA's 8-bit TXDR and RXDR accesses are made by the CPU.
//!   path="generated" - Aethelgard's generated firmware on SPI1: its writes as `spike/1100/data/writes.tsv`
//!                      records them (hse-crystal-16), and its reads as its board crate makes them
//!                      (Aethelgard b9d5f4b6, boards/sentinel-01/src/board.rs L255-285, L632-716):
//!                      start, one 4-byte transfer with SR polled for RXP, EOT and 16 SR reads, end.
//!   path="field"     - the generated sequence with CFG2.CPOL=1, a field outside the allow-list:
//!                      refused by TVI-SPI-FIELD, kind unmodeled (RUL-17).
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

#[cfg(path = "ardupilot")]
unsafe fn ardupilot() {
    const CR2: *mut u32 = 0x4001_3004 as *mut u32;
    const CFG1: *mut u32 = 0x4001_3008 as *mut u32;
    const IER: *mut u32 = 0x4001_3010 as *mut u32;
    const IFCR: *mut u32 = 0x4001_3018 as *mut u32;
    const TXDR: *mut u8 = 0x4001_3020 as *mut u8;
    const RXDR: *const u8 = 0x4001_3030 as *const u8;
    for _ in 0..2 {
        // spi_lld_start
        write_volatile(SPI1_CR1, read_volatile(SPI1_CR1) & !CR1_SPE);
        write_volatile(SPI1_CR1, 0x100); // MASRX
        write_volatile(CR2, 0);
        write_volatile(CFG1, 0x2000_C007);
        write_volatile(SPI1_CFG2, 0x2040_0000); // SSOE | MASTER, SSM=0
        write_volatile(IER, 0x40); // OVRIE
        write_volatile(IFCR, 0xFFFF_FFFF);
        write_volatile(SPI1_CR1, read_volatile(SPI1_CR1) | CR1_SPE);
        // spi_lld_wait_complete
        while read_volatile(SPI1_CR1) & (1 << 9) != 0 {}
        write_volatile(IFCR, 0xFFFF_FFFF);
        // spi_lld_exchange: the DMA fills TXDR, then CSTART; the DMA drains RXDR
        write_volatile(TXDR, 0x05);
        write_volatile(TXDR, 0x00);
        write_volatile(SPI1_CR1, read_volatile(SPI1_CR1) | (1 << 9));
        read_volatile(RXDR);
        read_volatile(RXDR);
        // spi_lld_serve_dma_rx_interrupt
        write_volatile(SPI1_CR1, read_volatile(SPI1_CR1) | (1 << 10));
        // spi_lld_stop
        write_volatile(SPI1_CR1, read_volatile(SPI1_CR1) & !CR1_SPE);
        write_volatile(SPI1_CR1, 0);
        write_volatile(CR2, 0);
        write_volatile(CFG1, 0);
        write_volatile(SPI1_CFG2, 0);
        write_volatile(IER, 0);
    }
}

#[cfg(any(path = "generated", path = "field"))]
unsafe fn generated(cfg2: u32) {
    const CR2: *mut u32 = 0x4001_3004 as *mut u32;
    const CFG1: *mut u32 = 0x4001_3008 as *mut u32;
    const IFCR: *mut u32 = 0x4001_3018 as *mut u32;
    const TXDR: *mut u8 = 0x4001_3020 as *mut u8;
    const SR: *const u32 = 0x4001_3014 as *const u32;
    const RXDR: *const u8 = 0x4001_3030 as *const u8;
    write_volatile(SPI1_CR1, 0x1000);
    write_volatile(CFG1, 0x1007_0007);
    write_volatile(SPI1_CFG2, cfg2);
    write_volatile(CR2, 0x4);
    write_volatile(SPI1_CR1, 0x1001);
    write_volatile(SPI1_CR1, 0x1201);
    // spi_frame: TXDR 8-bit, poll SR (32-bit) for RXP, then RXDR 8-bit
    for b in [0x9F, 0, 0, 0] {
        write_volatile(TXDR, b);
        for _ in 0..1000 {
            if read_volatile(SR) & 1 != 0 {
                read_volatile(RXDR);
                break;
            }
        }
    }
    // SR.EOT, then one spi_sr_round of 16 SR reads before the disable
    for _ in 0..1000 {
        if read_volatile(SR) & (1 << 3) != 0 {
            break;
        }
    }
    for _ in 0..16 {
        read_volatile(SR);
    }
    write_volatile(IFCR, 0x8);
    write_volatile(SPI1_CR1, 0x1000);
}

#[cfg(path = "spray")]
fn spray() {
    const RECORD: &[u8] = b"{\"tviburi\":\"witness\",\"seq\":0}";
    // SR, IFCR, TXCRC, RXCRC: the registers whose every value the allow-list passes on to the stock
    // model (TVI-WO-0100b; RM0433 Rev 9 Table 429, p2233). TXDR takes the bytes below, 8-bit.
    for off in [0x14u32, 0x18, 0x44, 0x48] {
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
        #[cfg(any(path = "modeled", path = "forge", path = "spray"))]
        {
            write_volatile(SPI1_CR1, CR1_SSI); // SSI before CFG2 (D1)
            write_volatile(SPI1_CFG2, CFG2_MASTER | CFG2_SSM);
            write_volatile(SPI1_CR1, read_volatile(SPI1_CR1) | CR1_SPE);
            #[cfg(path = "spray")]
            spray(); // after the enable: TXDR takes data only while SPE=1 (TVI-SPI-ORDER)
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
        #[cfg(path = "ardupilot")]
        ardupilot();
        #[cfg(path = "generated")]
        generated(0x8440_0000); // AFCNTR | SSM | MASTER
        #[cfg(path = "field")]
        generated(0x8440_0000 | (1 << 25)); // CPOL
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
