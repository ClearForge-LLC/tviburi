//! Bare-metal probes for the advanced-control timer guard (TVI-WO-1302). No dependencies; built with rustc alone, as
//! gpio.rs. Variant chosen at build time with `--cfg path="..."`. Every path first enables the GPIO ports' clocks
//! (RCC_AHB4ENR = 0x7FF, p461) and TIM1's and TIM8's (RCC_APB2ENR TIM1EN bit 0, TIM8EN bit 1, p470), puts PA8 on AF1
//! (TIM1_CH1) and PC6 on AF3 (TIM8_CH1) (DS12117 Rev 10 Table 9 p89, p92), then makes its own accesses, lets virtual
//! time pass, and writes the after-marker. The test reads the pins' observers (pa8obs, pc6obs) at the end.
//! Registers: RM0433 Rev 9 §38.4 (TIM1/TIM8), pages in tests/1302/timer.sh. `pwm(t, ccr)` is CH1 in PWM mode 1
//! (OC1M = 0110, OC1PE = 1, p1602), ARR = 99, CCR1 = ccr, CC1E (p1605), UG, CEN; MOE is each path's.
//!   path="pwm"        - TIM1 PWM, CCR1 = 50, MOE = 1: PA8 toggles, as today. MARK_READ = CCMR1 (0x68: OC1PE kept).
//!   path="moe0"       - the same with MOE never set: PA8 stays low (Table 337 p1608: Hi-Z, driven low).
//!   path="cce0"       - MOE = 1, CC1E = 0, CC1IE = 1 (the stock model's channel still runs, leak L1): PA8 stays low.
//!   path="force"      - MOE = 1, CC1E = 0, OC1M = 0101 force active (the stock model sets the line, leak L2): PA8 low.
//!   path="polarity"   - MOE = 1, CC1P = 1, CCR1 > ARR (OCxREF held 1, p1558): PA8 low (OCx = OCxREF xor CCxP, p1608).
//!   path="ccrreset"   - MOE = 1, PWM mode 1 with CCR1 never written (reset 0, p1610; the stock model 0xFFFF): PA8 low.
//!                       MARK_READ = CCR1 (0).
//!   path="ccr0"       - MOE = 1, CCR1 > ARR (PA8 high), then CCR1 = 0 (OCxREF held 0, p1558): PA8 low.
//!   path="ossi"       - MOE = 0, OSSI = 1, OIS1 = 1, CC1E = 1: PA8 at its idle level, high (p1615, Table 337 p1608).
//!   path="ccne"       - CCER = CC1E | CC1NE: TVI-TIM-FIELD, unmodeled (ruled Q5 (a)).
//!   path="ccnp"       - CCER = CC1E | CC1NP: TVI-TIM-FIELD, unmodeled.
//!   path="lock1"      - BDTR = MOE | LOCK 1, then CR2.OIS1 = 1: TVI-TIM-LOCK, predicted-fault (p1615).
//!   path="lock2"      - BDTR = MOE | LOCK 2, then CCER.CC1P = 1: TVI-TIM-LOCK, predicted-fault (p1607, p1615).
//!   path="lock3"      - BDTR = MOE | LOCK 3, then CCMR1.OC1M = 0101: TVI-TIM-LOCK, predicted-fault (p1602, p1615).
//!   path="lockfrozen" - BDTR = MOE (LOCK 0, frozen by the first write), then BDTR = MOE | LOCK 1: TVI-TIM-LOCK (p1615).
//!   path="locksame"   - BDTR = MOE | LOCK 3, the same CCER, CCMR1 and BDTR written again, then MOE cleared (not
//!                       locked): passes; PA8 low.
//!   path="dmar"       - a write to DMAR (0x4C): TVI-TIM-DMAR, unmodeled (p1616).
//!   path="half"       - CCER written as a 16-bit store (CC1E): merged and passed (p1588): PA8 toggles.
//!   path="byte"       - CCER written as an 8-bit store: TVI-TIM-WIDTH, unmodeled (p1588: half-words or words).
//!   path="aoe"        - BDTR = MOE | AOE: TVI-TIM-FIELD, unmodeled.
//!   path="read"       - a read at 0x50 (no register, Table 338 p1628): TVI-TIM-READ, unmodeled.
//!   path="tim8"       - TIM8 PWM on PC6 with MOE never set: PC6 low; TIM8 is guarded too.
//!   path="tim8on"     - the same with MOE = 1: PC6 toggles (TIM8's routes land on the guard's outputs).
//!   path="reenable"   - MOE = 1, OC1M = force active, CC1E = 1 (PA8 high), CC1E = 0 (low), CC1E = 1 again: high, since
//!                       OCxREF does not depend on CCxE (p1607; Table 337 p1608).
//!   path="lockbelow"  - LOCK 1, then a CC1P change, an OSSI change and an OC1M change, each above level 1: pass (p1615).
//!   path="lockbelow2" - LOCK 2, then an OC1M change, above level 2: passes (p1615).
//!   path="halfhigh"   - TIM1 PWM with MOE = 1, then a 16-bit store of 0 to BDTR's upper half (0x46): merged, MOE kept,
//!                       PA8 toggles (p1588).
//!   path="gen-pwm"    - motors-pwm's TIM1 writes at Aethelgard 65c74788 (TVI-WO-1302.measure.md §2.1), PA8-PA11 on AF1:
//!                       passes, enforcing; PA8 toggles.
//!   path="ap-m"       - unmodified ArduPilot's TIM1 writes when built with motor outputs (measure.md §2.1, AP-m),
//!                       CCMR3 included and no CCRx: passes, enforcing; PA8 stays low (ruled Q2: CCRx reads 0 at reset,
//!                       and 0 holds OCxREF at 0, p1610, p1558), where the stock model holds it high.
#![no_std]
#![no_main]
#![allow(dead_code)]

use core::ptr::{read_volatile, write_volatile};

const RCC_AHB4ENR: *mut u32 = 0x5802_44E0 as *mut u32;
const RCC_APB2ENR: *mut u32 = 0x5802_44F0 as *mut u32;
const GPIOA: usize = 0x5802_0000;
const GPIOC: usize = 0x5802_0800;
const TIM1: usize = 0x4001_0000;
const TIM8: usize = 0x4001_0400;
const CR1: usize = 0x00;
const CR2: usize = 0x04;
const DIER: usize = 0x0C;
const SR: usize = 0x10;
const EGR: usize = 0x14;
const CCMR1: usize = 0x18;
const CCMR2: usize = 0x1C;
const CCER: usize = 0x20;
const PSC: usize = 0x28;
const ARR: usize = 0x2C;
const CCR1: usize = 0x34;
const BDTR: usize = 0x44;
const DMAR: usize = 0x4C;
const CCMR3: usize = 0x54;
const MOE: u32 = 1 << 15;
const MARK_AFTER: *mut u32 = 0x2000_0104 as *mut u32;
const MARK_READ: *mut u32 = 0x2000_0108 as *mut u32;
const MARK_READ2: *mut u32 = 0x2000_010C as *mut u32;

unsafe fn w(base: usize, off: usize, v: u32) {
    write_volatile((base + off) as *mut u32, v);
}

unsafe fn r(base: usize, off: usize) -> u32 {
    read_volatile((base + off) as *const u32)
}

/// Lets virtual time pass, so the timer runs.
unsafe fn spin() {
    let mut i = 0u32;
    while i < 5000 {
        write_volatile(MARK_READ2, i);
        i += 1;
    }
}

/// CH1 in PWM mode 1 with preload, ARR = 99, CCR1 = `ccr` (none: left at reset), `ccer` written, then UG and CEN.
unsafe fn pwm(t: usize, ccr: Option<u32>, ccer: u32) {
    w(t, PSC, 0);
    w(t, ARR, 99);
    if let Some(c) = ccr {
        w(t, CCR1, c);
    }
    w(t, CCMR1, 0x68);
    w(t, CCER, ccer);
}

unsafe fn start(t: usize) {
    w(t, EGR, 1);
    w(t, CR1, 0x81);
}

#[no_mangle]
pub unsafe extern "C" fn reset() -> ! {
    write_volatile(RCC_AHB4ENR, 0x7FF);
    write_volatile(RCC_APB2ENR, 0x3);
    w(GPIOA, 0x24, (r(GPIOA, 0x24) & !0xFFFF) | 0x1111);                 // AFRH: PA8-PA11 AF1
    w(GPIOA, 0x00, (r(GPIOA, 0x00) & !(0xFF << 16)) | (0xAA << 16));      // MODER: PA8-PA11 AF
    w(GPIOC, 0x20, (r(GPIOC, 0x20) & !(0xF << 24)) | (3 << 24));          // AFRL: PC6 AF3
    w(GPIOC, 0x00, (r(GPIOC, 0x00) & !(3 << 12)) | (2 << 12));            // MODER: PC6 AF
    #[cfg(path = "pwm")]
    {
        pwm(TIM1, Some(50), 1);
        w(TIM1, BDTR, MOE);
        start(TIM1);
        write_volatile(MARK_READ, r(TIM1, CCMR1));
    }
    #[cfg(path = "moe0")]
    {
        pwm(TIM1, Some(50), 1);
        start(TIM1);
    }
    #[cfg(path = "cce0")]
    {
        pwm(TIM1, Some(50), 0);
        w(TIM1, DIER, 1 << 1);
        w(TIM1, BDTR, MOE);
        start(TIM1);
    }
    #[cfg(path = "force")]
    {
        pwm(TIM1, Some(50), 0);
        w(TIM1, CCMR1, 0x58);
        w(TIM1, BDTR, MOE);
        start(TIM1);
    }
    #[cfg(path = "polarity")]
    {
        pwm(TIM1, Some(200), 0x3);
        w(TIM1, BDTR, MOE);
        start(TIM1);
    }
    #[cfg(path = "ccrreset")]
    {
        pwm(TIM1, None, 1);
        w(TIM1, BDTR, MOE);
        start(TIM1);
        write_volatile(MARK_READ, r(TIM1, CCR1));
    }
    #[cfg(path = "ccr0")]
    {
        pwm(TIM1, Some(200), 1);
        w(TIM1, BDTR, MOE);
        start(TIM1);
        spin();
        w(TIM1, CCR1, 0);
    }
    #[cfg(path = "ossi")]
    {
        pwm(TIM1, Some(50), 1);
        w(TIM1, CR2, 1 << 8);
        w(TIM1, BDTR, 1 << 10);
        start(TIM1);
    }
    #[cfg(path = "ccne")]
    pwm(TIM1, Some(50), 0x5);
    #[cfg(path = "ccnp")]
    pwm(TIM1, Some(50), 0x9);
    #[cfg(path = "lock1")]
    {
        w(TIM1, BDTR, MOE | 1 << 8);
        w(TIM1, CR2, 1 << 8);
    }
    #[cfg(path = "lock2")]
    {
        pwm(TIM1, Some(50), 1);
        w(TIM1, BDTR, MOE | 2 << 8);
        w(TIM1, CCER, 0x3);
    }
    #[cfg(path = "lock3")]
    {
        pwm(TIM1, Some(50), 1);
        w(TIM1, BDTR, MOE | 3 << 8);
        w(TIM1, CCMR1, 0x58);
    }
    #[cfg(path = "lockfrozen")]
    {
        w(TIM1, BDTR, MOE);
        w(TIM1, BDTR, MOE | 1 << 8);
    }
    #[cfg(path = "locksame")]
    {
        pwm(TIM1, Some(50), 1);
        w(TIM1, BDTR, MOE | 3 << 8);
        start(TIM1);
        w(TIM1, CCER, 1);
        w(TIM1, CCMR1, 0x68);
        w(TIM1, BDTR, MOE | 3 << 8);
        w(TIM1, BDTR, 3 << 8);
    }
    #[cfg(path = "dmar")]
    w(TIM1, DMAR, 0);
    #[cfg(path = "half")]
    {
        pwm(TIM1, Some(50), 0);
        write_volatile((TIM1 + CCER) as *mut u16, 1);
        w(TIM1, BDTR, MOE);
        start(TIM1);
    }
    #[cfg(path = "byte")]
    write_volatile((TIM1 + CCER) as *mut u8, 1);
    #[cfg(path = "aoe")]
    w(TIM1, BDTR, MOE | 1 << 14);
    #[cfg(path = "read")]
    write_volatile(MARK_READ, r(TIM1, 0x50));
    #[cfg(path = "tim8")]
    {
        pwm(TIM8, Some(50), 1);
        start(TIM8);
    }
    #[cfg(path = "tim8on")]
    {
        pwm(TIM8, Some(50), 1);
        w(TIM8, BDTR, MOE);
        start(TIM8);
    }
    #[cfg(path = "reenable")]
    {
        pwm(TIM1, Some(50), 1);
        w(TIM1, CCMR1, 0x58);
        w(TIM1, BDTR, MOE);
        w(TIM1, CCER, 0);
        w(TIM1, CCER, 1);
    }
    #[cfg(path = "lockbelow")]
    {
        pwm(TIM1, Some(50), 1);
        w(TIM1, BDTR, MOE | 1 << 8);
        w(TIM1, CCER, 0x3);
        w(TIM1, BDTR, MOE | 1 << 8 | 1 << 10);
        w(TIM1, CCMR1, 0x58);
    }
    #[cfg(path = "lockbelow2")]
    {
        pwm(TIM1, Some(50), 1);
        w(TIM1, BDTR, MOE | 2 << 8);
        w(TIM1, CCMR1, 0x58);
    }
    #[cfg(path = "halfhigh")]
    {
        pwm(TIM1, Some(50), 1);
        w(TIM1, BDTR, MOE);
        start(TIM1);
        write_volatile((TIM1 + BDTR + 2) as *mut u16, 0);
    }
    #[cfg(path = "gen-pwm")]
    {
        w(TIM1, PSC, 0x3F);
        w(TIM1, ARR, 0x9C3);
        w(TIM1, CCR1, 0x3E8);
        w(TIM1, 0x38, 0x3E8);
        w(TIM1, 0x3C, 0x3E8);
        w(TIM1, 0x40, 0x3E8);
        w(TIM1, CCMR1, 0x6868);
        w(TIM1, CCMR2, 0x6868);
        w(TIM1, CCER, 0x1111);
        w(TIM1, BDTR, 0x8000);
        w(TIM1, EGR, 0x1);
        w(TIM1, CR1, 0x81);
    }
    #[cfg(path = "ap-m")]
    {
        w(TIM1, CCMR1, 0x6868);
        w(TIM1, CCMR2, 0x6868);
        w(TIM1, CCMR3, 0x0101_6868);
        w(TIM1, PSC, 0xC7);
        w(TIM1, ARR, 0x4E1F);
        w(TIM1, CR2, 0);
        w(TIM1, CCER, 0x1111);
        w(TIM1, EGR, 0x1);
        w(TIM1, SR, 0);
        w(TIM1, DIER, 0);
        w(TIM1, BDTR, 0x8000);
        w(TIM1, CR1, 0x85);
    }
    spin();
    write_volatile(MARK_AFTER, 0xAF7E_0002);
    loop {}
}

#[panic_handler]
fn panic(_: &core::panic::PanicInfo) -> ! {
    loop {}
}
