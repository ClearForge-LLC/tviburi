//! Bare-metal probes for the I2C guard (TVI-WO-1306). No dependencies; built with rustc alone, as usart.rs. Variant chosen
//! at build time with `--cfg path="..."`. Every path first enables I2C1's clock (RCC_APB1LENR I2C1EN bit 21, p463; its kernel
//! clock stays rcc_pclk1, I2C123SEL = 00, p416), then writes I2C1 (0x4000_5400) and the after-marker. The platform puts a
//! BME280 at 0x76 on i2c1 (tests/1306/i2c.sh). Registers: RM0433 Rev 9 §47.9 (pp. 2001-2014); pages and rulings in
//! tests/1306/i2c.sh. `config` is the generated firmware's sequence (TVI-WO-1306.measure.md §2.1): CR1 = 0, TIMINGR, CR1 = PE.
//!   path="config"      - config, then the BME280's chip id twice (write 0xD0, read 1 with AUTOEND, STOPF cleared, as the
//!                        generated firmware does before its next START): passes. MARK_READ = RXDR (0x60),
//!                        MARK_READ2 = TIMINGR (0x10C7123C, served from the guard's copy; the stock model reads 0).
//!   path="timingrpe"   - config, then TIMINGR changed with PE = 1: TVI-I2C-PE, unmodeled (p2008).
//!   path="timingrsame" - config, then TIMINGR written with its own value with PE = 1: passes (RUL-18).
//!   path="timingroff"  - config, PE cleared, PE checked, TIMINGR changed, PE set: passes. MARK_READ = TIMINGR (0x00300F38).
//!   path="dnfpe"       - config, then CR1.DNF = 5 with PE = 1: TVI-I2C-PE, unmodeled (p2003).
//!   path="anfoff"      - CR1 = ANFOFF|DNF (PE = 0), TIMINGR, then the same with PE: passes. MARK_READ = CR1 (0x1F01, from the copy).
//!   path="clearfield"  - config, then CR1 written with PE = 0 and DNF = 3 in one write: passes (judged as with PE = 0, Q2).
//!   path="ap"          - ArduPilot's I2C1 sequence (measure.md §2.1): CR1 = 0x90, TIMINGR 0x00300F38, CR1 = 0x91; a write of 1
//!                        and a read of 1 to the BME280 with software STOP; ICR = 0x40, 0x4, 0x2 (reserved bits, Q10); a probe of
//!                        0x30 (no device: NACKed, TVI-WO-1306a) ended by STOP (absorbed while the NACK's STOP is pending); a stop (CR1 = 0xD2) and a start again: passes.
//!                        MARK_READ = RXDR (0x60), MARK_READ2 = TIMINGR (0x00300F38).
//!   path="absentnext"  - config, the generated firmware's probe of 0x48 (no device): a START, NACKed (TVI-WO-1306a), ISR polled to
//!                        STOPF, ICR = 0x30; then the chip id: passes. MARK_READ = RXDR (0x60).
//!   path="absent"      - config, then a read START from 0x31 (no device): TVI-I2C-ABSENT, unmodeled (TVI-WO-1306a Q6; p1975).
//! The NACK (TVI-WO-1306a; ruled 2026-11-26T00:22Z): a write START to an address with no device sets NACKF nine SCL periods later
//! (p1955, p1971) and STOPF one period after that (inferred), whatever AUTOEND (p1975). MARK_READ3/MARK_READ4 at 0x2000_0110/0x114.
//!   path="nack"        - rcc_pclk1 / 2 (D2PPRE1 = 100, p396), CR1.DNF = 15, config, a write START to 0x31 with AUTOEND = 1: ISR at
//!                        once (MARK_READ, 0x1), polled to NACKF (MARK_READ2, 0x11), to STOPF (MARK_READ3, 0x31), ICR = 0x30
//!                        (MARK_READ4, 0x1). Its timing is tests/1306/i2c.sh `nacktime`, from the monitor.
//!   path="nackirq"     - CR1.NACKIE and STOPIE, config, a write START to 0x31; then idle: tests/1306/i2c.sh reads the event line
//!                        and the NVIC.
//!   path="nackstop"    - ChibiOS's NACK (TVI-WO-1306a.measure.md §2.1): CR1 0x90/0x91, a write START to 0x30, polled to NACKF
//!                        (MARK_READ, 0x11), ICR = ISR & 0xFE, CR2 |= STOP while the NACK's STOP is pending (absorbed: MARK_READ2,
//!                        0x1), polled to STOPF (MARK_READ3, 0x21), then PE cleared, checked, the driver started again and the chip
//!                        id read (MARK_READ4, 0x60): passes.
//!   path="nackpe"      - config, a write START to 0x31, then PE cleared, checked and set at once: the pending NACK is cancelled (p1959);
//!                        ISR polled (MARK_READ, 0x1); again with an I2C1RST pulse (RCC_APB1LRSTR bit 21, p438) for the clear: the
//!                        port's reset cancels it too (MARK_READ2, 0x1): passes.
//!   path="nacktransfer" - config, a write START to 0x31, then a START to the BME280 while the NACK's STOP is pending:
//!                        TVI-I2C-TRANSFER, unmodeled (Q4).
//!   path="nacktxdr"    - config, a write START to 0x31, then a TXDR write before the NACK's STOPF: TVI-I2C-TRANSFER, unmodeled.
//!   path="nackclock"   - I2C123SEL = 01 with PLL3's VCO at 20 MHz (outside p404's ranges), config, a write START to 0x31:
//!                        TVI-I2C-NACK, unmodeled (no frequency to time it by, Q2).
//!   path="icrres"      - config, then ICR = 0x80 (bit 7, reserved, not ChibiOS's): TVI-I2C-FIELD, unmodeled.
//!   path="field"       - CR1.TXDMAEN = 1 with PE = 0: TVI-I2C-FIELD, unmodeled (the allow-list).
//!   path="width"       - a byte write to CR1: TVI-I2C-WIDTH, unmodeled (p2001).
//!   path="read"        - a read at 0x2C (no register): TVI-I2C-READ, unmodeled.
//!   path="kernel"      - I2C123SEL = 01 (pll3_r_ck, PLL3 off), then config: TVI-I2C-KERNEL, predicted-fault (p1954, p371).
//!   path="reset"       - config, then PE cleared and set in the next two writes: TVI-I2C-RESET, unmodeled (p1960).
//!   path="resetok"     - config, then PE cleared, PE checked (a read), PE set: passes.
//!   path="startoff"    - CR1 = 0, then a START to the BME280: TVI-I2C-TRANSFER, unmodeled (p2003).
//!   path="txdroff"     - CR1 = 0, then a TXDR write: TVI-I2C-TRANSFER, unmodeled.
//!   path="empty"       - config, then a START with NBYTES = 0 and RELOAD = 0: TVI-I2C-TRANSFER, unmodeled; the stock model,
//!                        reached, would abort Renode (measure.md §2.3 I8b).
//!   path="midstart"    - config, a write of 2 with one byte sent, then a START: TVI-I2C-TRANSFER, unmodeled.
//!   path="midcr2"      - config, a write of 2 with one byte sent, then CR2.NBYTES rewritten: TVI-I2C-TRANSFER (p2004).
//!   path="txdrread"    - config, a read START, then a TXDR write: TVI-I2C-TRANSFER, unmodeled (p2014; the stock model would send it).
//!   path="stopidle"    - config, then STOP with no transfer: TVI-I2C-TRANSFER, unmodeled (p2005: controller mode only).
//!   path="startstop"   - config, then START and STOP in one CR2 write: TVI-I2C-TRANSFER, unmodeled.
//!   path="stalestop"   - config, a read with AUTOEND (STOPF, left set), a write of 2 with one byte sent, then a START: TVI-I2C-TRANSFER,
//!                        unmodeled, at the START (the STOPF left from before does not end the write).
//!   path="blindclear"  - config, a read of 1 with AUTOEND (STOPF once its byte is read), ICR.STOPCF with no ISR read before it,
//!                        then the chip id: passes (the transfer ended at the STOPF the ICR write cleared). MARK_READ = RXDR (0x60).
//!   path="softreset"   - config, a read of 2 from the BME280 with one byte read, then PE cleared: passes. MARK_READ = ISR after the
//!                        clear (0x1: RXNE reset, TXE set, p1959, p2011), MARK_READ2 = CR2 (0x204EC: the configuration kept).
#![no_std]
#![no_main]
#![allow(dead_code)]

use core::ptr::{read_volatile, write_volatile};

const RCC_APB1LENR: *mut u32 = 0x5802_44E8 as *mut u32;
const RCC_D2CCIP2R: *mut u32 = 0x5802_4454 as *mut u32;
const I2C1: usize = 0x4000_5400;
const CR1: usize = 0x00;
const CR2: usize = 0x04;
const TIMINGR: usize = 0x10;
const ISR: usize = 0x18;
const ICR: usize = 0x1C;
const RXDR: usize = 0x24;
const TXDR: usize = 0x28;
const MARK_AFTER: *mut u32 = 0x2000_0104 as *mut u32;
const MARK_READ: *mut u32 = 0x2000_0108 as *mut u32;
const MARK_READ2: *mut u32 = 0x2000_010C as *mut u32;
const MARK_READ3: *mut u32 = 0x2000_0110 as *mut u32;
const MARK_READ4: *mut u32 = 0x2000_0114 as *mut u32;
const RCC_CR: *mut u32 = 0x5802_4400 as *mut u32;
const RCC_D2CFGR: *mut u32 = 0x5802_441C as *mut u32;
const RCC_APB1LRSTR: *mut u32 = 0x5802_4490 as *mut u32;
const RCC_PLL3DIVR: *mut u32 = 0x5802_4440 as *mut u32;
const ABSENT: u32 = 0x31 << 1;
const TIMING: u32 = 0x10C7_123C;
const BME: u32 = 0x76 << 1;
const START: u32 = 1 << 13;
const STOP: u32 = 1 << 14;
const RD: u32 = 1 << 10;
const AUTOEND: u32 = 1 << 25;

unsafe fn w(off: usize, v: u32) {
    write_volatile((I2C1 + off) as *mut u32, v);
}

unsafe fn r(off: usize) -> u32 {
    read_volatile((I2C1 + off) as *const u32)
}

unsafe fn until(bits: u32) {
    for _ in 0..1000 {
        if r(ISR) & bits != 0 {
            return;
        }
    }
}

/// PLL3 on, from the HSI (64 MHz) through DIVM3's reset 32 (2 MHz, p398-399), times DIVN3 + 1 (p410), its R output over DIVR3 + 1
/// = 20 (p409), and I2C123SEL = 01 (pll3_r_ck, p416): with divn = 9 the VCO (20 MHz) is outside p404's ranges (`nackclock`).
unsafe fn pll3_30mhz(divn: u32) {
    write_volatile(RCC_PLL3DIVR, 19 << 24 | 1 << 16 | 1 << 9 | divn);
    write_volatile(RCC_CR, read_volatile(RCC_CR) | 1 << 28);
    write_volatile(RCC_D2CCIP2R, 1 << 12);
}

/// The generated firmware's sequence: CR1 = 0, TIMINGR, CR1 = PE.
unsafe fn config() {
    w(CR1, 0);
    w(TIMINGR, TIMING);
    w(CR1, 1);
}

/// The BME280's chip id: write the register address 0xD0, then read one byte with AUTOEND.
unsafe fn chip_id() -> u32 {
    w(CR2, 1 << 16 | START | BME);
    w(TXDR, 0xD0);
    until(1 << 6);
    w(CR2, AUTOEND | 1 << 16 | RD | START | BME);
    until(1 << 2);
    let v = r(RXDR);
    until(1 << 5);
    w(ICR, 1 << 5);
    v
}

#[no_mangle]
pub unsafe extern "C" fn reset() -> ! {
    #[cfg(path = "kernel")]
    write_volatile(RCC_D2CCIP2R, 1 << 12);
    write_volatile(RCC_APB1LENR, 1 << 21);
    #[cfg(any(path = "config", path = "kernel"))]
    {
        config();
        chip_id();
        write_volatile(MARK_READ, chip_id());
        write_volatile(MARK_READ2, r(TIMINGR));
    }
    #[cfg(path = "timingrpe")]
    {
        config();
        w(TIMINGR, TIMING + 1);
    }
    #[cfg(path = "timingrsame")]
    {
        config();
        w(TIMINGR, TIMING);
    }
    #[cfg(path = "timingroff")]
    {
        config();
        w(CR1, 0);
        if r(CR1) & 1 == 0 {
            w(TIMINGR, 0x0030_0F38);
        }
        w(CR1, 1);
        write_volatile(MARK_READ, r(TIMINGR));
    }
    #[cfg(path = "dnfpe")]
    {
        config();
        w(CR1, 5 << 8 | 1);
    }
    #[cfg(path = "anfoff")]
    {
        w(CR1, 1 << 12 | 0xF << 8);
        w(TIMINGR, TIMING);
        w(CR1, 1 << 12 | 0xF << 8 | 1);
        write_volatile(MARK_READ, r(CR1));
    }
    #[cfg(path = "clearfield")]
    {
        config();
        w(CR1, 3 << 8);
    }
    #[cfg(path = "ap")]
    {
        w(CR1, 0x90);
        w(TIMINGR, 0x0030_0F38);
        w(CR1, 0x91);
        w(CR2, 1 << 16 | BME);
        w(CR2, r(CR2) | START);
        w(TXDR, 0xD0);
        until(1 << 6);
        w(ICR, 0x40);
        w(CR2, 1 << 16 | RD | BME);
        w(CR2, r(CR2) | START);
        until(1 << 2);
        write_volatile(MARK_READ, r(RXDR));
        w(ICR, 0x4);
        until(1 << 6);
        w(CR2, r(CR2) | STOP);
        w(ICR, 0x2);
        w(ICR, 1 << 5);
        w(CR2, 2 << 16 | 0x30 << 1);
        w(CR2, r(CR2) | START);
        w(CR2, r(CR2) | STOP);
        w(CR1, 0xD2);
        w(CR1, 0x90);
        w(TIMINGR, 0x0030_0F38);
        w(CR1, 0x91);
        write_volatile(MARK_READ2, r(TIMINGR));
    }
    #[cfg(path = "absentnext")]
    {
        config();
        w(CR2, 1 << 16 | START | 0x48 << 1);
        until(1 << 5);
        w(ICR, 0x30);
        write_volatile(MARK_READ, chip_id());
    }
    #[cfg(path = "absent")]
    {
        config();
        w(CR2, 1 << 16 | RD | START | ABSENT);
    }
    #[cfg(path = "nack")]
    {
        write_volatile(RCC_D2CFGR, 0b100 << 4);
        w(CR1, 0xF << 8);
        w(TIMINGR, TIMING);
        w(CR1, 0xF << 8 | 1);
        w(CR2, AUTOEND | 1 << 16 | START | ABSENT);
        write_volatile(MARK_READ, r(ISR));
        until(1 << 4);
        write_volatile(MARK_READ2, r(ISR));
        until(1 << 5);
        write_volatile(MARK_READ3, r(ISR));
        w(ICR, 0x30);
        write_volatile(MARK_READ4, r(ISR));
    }
    #[cfg(path = "nackirq")]
    {
        w(CR1, 3 << 4);
        w(TIMINGR, TIMING);
        w(CR1, 3 << 4 | 1);
        w(CR2, 1 << 16 | START | ABSENT);
    }
    #[cfg(path = "nackstop")]
    {
        w(CR1, 0x90);
        w(TIMINGR, 0x0030_0F38);
        w(CR1, 0x91);
        w(CR2, 0x30 << 1);
        w(CR2, r(CR2) | 1 << 16);
        w(CR2, r(CR2) | START);
        until(1 << 4);
        let isr = r(ISR);
        write_volatile(MARK_READ, isr);
        w(ICR, isr & 0xFE);
        w(CR2, r(CR2) | STOP);
        write_volatile(MARK_READ2, r(ISR));
        until(1 << 5);
        write_volatile(MARK_READ3, r(ISR));
        w(CR1, r(CR1) & !0x46);
        w(CR1, r(CR1) & !1);
        w(CR1, 0x90);
        w(TIMINGR, 0x0030_0F38);
        w(CR1, 0x91);
        write_volatile(MARK_READ4, chip_id());
    }
    #[cfg(path = "nackpe")]
    {
        config();
        w(CR2, 1 << 16 | START | ABSENT);
        w(CR1, 0);
        if r(CR1) & 1 == 0 {
            w(CR1, 1);
        }
        until(1 << 4);
        write_volatile(MARK_READ, r(ISR));
        w(CR2, 1 << 16 | START | ABSENT);
        write_volatile(RCC_APB1LRSTR, 1 << 21);
        write_volatile(RCC_APB1LRSTR, 0);
        config();
        until(1 << 4);
        write_volatile(MARK_READ2, r(ISR));
    }
    #[cfg(path = "nacktransfer")]
    {
        config();
        w(CR2, 1 << 16 | START | ABSENT);
        w(CR2, 1 << 16 | START | BME);
    }
    #[cfg(path = "nacktxdr")]
    {
        config();
        w(CR2, 1 << 16 | START | ABSENT);
        w(TXDR, 0xD0);
    }
    #[cfg(path = "nackclock")]
    {
        pll3_30mhz(9);
        config();
        w(CR2, 1 << 16 | START | ABSENT);
    }
    #[cfg(path = "icrres")]
    {
        config();
        w(ICR, 0x80);
    }
    #[cfg(path = "field")]
    {
        w(CR1, 1 << 14);
    }
    #[cfg(path = "width")]
    {
        write_volatile((I2C1 + CR1) as *mut u8, 1);
    }
    #[cfg(path = "read")]
    {
        write_volatile(MARK_READ, r(0x2C));
    }
    #[cfg(path = "reset")]
    {
        config();
        w(CR1, 0);
        w(CR1, 1);
    }
    #[cfg(path = "resetok")]
    {
        config();
        w(CR1, 0);
        if r(CR1) & 1 == 0 {
            w(CR1, 1);
        }
    }
    #[cfg(path = "startoff")]
    {
        w(CR1, 0);
        w(TIMINGR, TIMING);
        w(CR2, 1 << 16 | START | BME);
    }
    #[cfg(path = "txdroff")]
    {
        w(CR1, 0);
        w(TXDR, 0xD0);
    }
    #[cfg(path = "empty")]
    {
        config();
        w(CR2, AUTOEND | START | BME);
    }
    #[cfg(path = "midstart")]
    {
        config();
        w(CR2, 2 << 16 | START | BME);
        w(TXDR, 0xD0);
        w(CR2, AUTOEND | 1 << 16 | RD | START | BME);
    }
    #[cfg(path = "midcr2")]
    {
        config();
        w(CR2, 2 << 16 | START | BME);
        w(TXDR, 0xD0);
        w(CR2, 3 << 16 | BME);
    }
    #[cfg(path = "txdrread")]
    {
        config();
        w(CR2, AUTOEND | 1 << 16 | RD | START | BME);
        w(TXDR, 0xF7);
    }
    #[cfg(path = "stopidle")]
    {
        config();
        w(CR2, STOP);
    }
    #[cfg(path = "startstop")]
    {
        config();
        w(CR2, 1 << 16 | START | STOP | BME);
    }
    #[cfg(path = "stalestop")]
    {
        config();
        w(CR2, AUTOEND | 1 << 16 | RD | START | BME);
        let _ = r(RXDR);
        w(CR2, 2 << 16 | START | BME);
        w(TXDR, 0xD0);
        w(CR2, 1 << 16 | RD | START | BME);
    }
    #[cfg(path = "blindclear")]
    {
        config();
        w(CR2, AUTOEND | 1 << 16 | RD | START | BME);
        let _ = r(RXDR);
        w(ICR, 1 << 5);
        write_volatile(MARK_READ, chip_id());
    }
    #[cfg(path = "softreset")]
    {
        config();
        w(CR2, 1 << 16 | START | BME);
        w(TXDR, 0xD0);
        until(1 << 6);
        w(CR2, 2 << 16 | RD | START | BME);
        until(1 << 2);
        let _ = r(RXDR);
        w(CR1, 0);
        write_volatile(MARK_READ, r(ISR));
        write_volatile(MARK_READ2, r(CR2));
    }
    write_volatile(MARK_AFTER, 0xAF7E_0002);
    loop {}
}

#[panic_handler]
fn panic(_: &core::panic::PanicInfo) -> ! {
    loop {}
}
