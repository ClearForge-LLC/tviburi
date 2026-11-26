//! Bare-metal probes for the flash guard (TVI-WO-1304). No dependencies; built with rustc alone, as timer.rs. Variant
//! chosen at build time with `--cfg path="..."`. Registers: RM0433 Rev 9 §4.9 (FLASH), §8.7 (RCC), §6.8.6 (PWR_D3CR);
//! pages in tests/1304/flash.sh. `pll400(hpre)` starts PLL1 from HSI (DIVM1 4, DIVN1 50, DIVP1 2: 400 MHz) and writes
//! D1CFGR's HPRE: 0x8 gives an AXI clock of 200 MHz, 0x9 100 MHz, 0 400 MHz; `switch(3)` moves sys_ck to PLL1. `vos1()`
//! writes PWR_D3CR = Scale 1, which only the tests' VOS-storing pwr keeps (ruled 2026-11-24T15:50Z Q7).
//!   path="order"        - VOS1, LATENCY 2 first, then the clock to 200 MHz, then the clock down to HSI and LATENCY 0:
//!                         both orders as p160-161 give them; passes. MARK_READ = ACR (0).
//!   path="clockfirst"   - VOS1, LATENCY 0, then the clock to 200 MHz: TVI-FLASH-WAIT, predicted-fault, at CFGR.
//!   path="lowerfast"    - VOS1, at 200 MHz with LATENCY 2, LATENCY lowered to 1: TVI-FLASH-WAIT at the ACR write.
//!   path="prescaler"    - VOS1, LATENCY 1, the clock to 100 MHz (HPRE /4), then HPRE /2 (200 MHz): TVI-FLASH-WAIT at
//!                         the D1CFGR write.
//!   path="alias"        - as lowerfast, the ACR write through its bank 2 alias 0x100: TVI-FLASH-WAIT there.
//!   path="vosunknown"   - (the stock pwr) LATENCY 2, the clock to 200 MHz: decided at VOS1, passes. MARK_READ = ACR.
//!   path="vosunknownlow"- (the stock pwr) LATENCY 1, the clock to 200 MHz: short even at VOS1: TVI-FLASH-WAIT.
//!   path="vos3"         - (the VOS-storing pwr, left at Scale 3) LATENCY 2, the clock to 200 MHz: Table 17 needs 4.
//!   path="over225"      - LATENCY 7, the clock to 400 MHz: above 225 MHz, TVI-FLASH-WAIT, unmodeled.
//!   path="lockedcr"     - FLASH_CR2.PG2 written while LOCK2 = 1: TVI-FLASH-LOCK, predicted-fault (p182, p229).
//!   path="lockedsame"   - FLASH_CR2 written with the value it holds while locked: passes. MARK_READ = CR2 (0x31).
//!   path="badkey"       - a first key other than KEY1: TVI-FLASH-KEY, predicted-fault (p182).
//!   path="keytwice"     - bank 2 unlocked, then KEY1 again: TVI-FLASH-KEY (p163).
//!   path="keymid"       - bank 1's KEY1, an ACR write (common to both banks, Table 20 p172: passes), then a CR1 write
//!                         before KEY2: TVI-FLASH-KEY at the CR1 write (p182).
//!   path="keyhalf"      - KEY1 written by a 16-bit store: TVI-FLASH-KEY (p182: "writing less than 32 bits").
//!   path="wrhlow"       - VOS1, 200 MHz, LATENCY 2 with WRHIGHFREQ 01, a sector erase started: TVI-FLASH-PROG at START2.
//!   path="wrhpg"        - the same with PG2 set: TVI-FLASH-PROG at the PG2 write.
//!   path="ap"           - unmodified ArduPilot's RCC sequence (TVI-WO-1300 §4) with its PWR_D3CR (dropped by the stock
//!                         pwr) and FLASH_ACR = 0x32 (TVI-WO-1304.measure.md §2.1), then its bank 2 operations: unlock
//!                         both banks, every clear, SR2 written, a sector erase (sector 7), a 32-byte program, the locks.
//!                         Passes, enforcing. MARK_READ = CR2 after its first write (0x724: PSIZE2 as written);
//!                         MARK_READ2 = SR2 after the SR2 write (0: read-only, F5).
//!   path="gen"          - the generated hse-crystal-16's PWR, RCC and FLASH_ACR writes (TVI-WO-1304.measure.md §2.3),
//!                         then its one OPTSR_CUR read: passes. MARK_READ = OPTSR_CUR.
//!   path="edge"         - VOS1, LATENCY 1, an AXI clock of 140 MHz, the top of 1 WS's band (Table 17 p160): passes.
//!   path="vos2"         - VOS2, LATENCY 1, 120 MHz: Table 17 needs 2 at VOS2 (1 at VOS1): TVI-FLASH-WAIT.
//!   path="d1cpre"       - VOS1, LATENCY 1, 100 MHz with D1CPRE /2, then D1CPRE /1: TVI-FLASH-WAIT at D1CFGR (p394).
//!   path="wrhrcc"       - VOS1, WRHIGHFREQ 01, PG2 set at HSI, then the clock to 200 MHz: TVI-FLASH-PROG at CFGR.
//!   path="locking"      - FLASH_CR2 written setting LOCK2 and PG2 at once: TVI-FLASH-LOCK (p229: "the other bits ... do
//!                         not change").
//!   path="worse"        - (recorded) LATENCY 1 at 200 MHz, then LATENCY 0: two would-refuses, the second a new mismatch.
//!   path="optsr"        - OPTSR_CUR read, then its alias 0x11C: MARK_READ, MARK_READ2.
//!   path="divp1zero"    - VOS1, LATENCY 4, PLL1 with DIVP1 = 0 (/1), then sys_ck to PLL1: the RCC guard has no frequency
//!                         ("DIVPx = 2,4,6,...128 when x = 1", Figure 47 p346; p404): TVI-FLASH-WAIT, unmodeled, at CFGR.
//!   path="divp1odd"     - the same with DIVP1 = 2 (/3: "odd division factors are not allowed", p404).
//!   path="optwrite"     - FLASH_OPTKEYR written: TVI-FLASH-REG, unmodeled (option-byte programming; ruled Q3).
//!   path="bank1"        - bank 1 unlocked, then PG1: TVI-FLASH-FIELD, unmodeled (bank 1 programming).
//!   path="readreg"      - FLASH_PRAR_CUR1 read: TVI-FLASH-READ, unmodeled.
#![no_std]
#![no_main]
#![allow(dead_code)]

use core::ptr::{read_volatile, write_volatile};

const RCC: usize = 0x5802_4400;
const PWR_D3CR: usize = 0x5802_4818;
const FLASH: usize = 0x5200_2000;
const ACR: usize = FLASH;
const KEYR1: usize = FLASH + 0x004;
const OPTKEYR: usize = FLASH + 0x008;
const CR1: usize = FLASH + 0x00C;
const CCR1: usize = FLASH + 0x014;
const OPTSR_CUR: usize = FLASH + 0x01C;
const PRAR_CUR1: usize = FLASH + 0x028;
const ACR_ALIAS: usize = FLASH + 0x100;
const KEYR2: usize = FLASH + 0x104;
const CR2: usize = FLASH + 0x10C;
const SR2: usize = FLASH + 0x110;
const CCR2: usize = FLASH + 0x114;
const OPTSR_ALIAS: usize = FLASH + 0x11C;
const KEY1: u32 = 0x4567_0123;
const KEY2: u32 = 0xCDEF_89AB;
const MARK_AFTER: *mut u32 = 0x2000_0104 as *mut u32;
const MARK_READ: *mut u32 = 0x2000_0108 as *mut u32;
const MARK_READ2: *mut u32 = 0x2000_010C as *mut u32;

unsafe fn w(a: usize, v: u32) {
    write_volatile(a as *mut u32, v);
}

unsafe fn r(a: usize) -> u32 {
    read_volatile(a as *const u32)
}

unsafe fn vos1() {
    w(PWR_D3CR, 0xC000);
}

unsafe fn pll400(d1cfgr: u32) {
    pll(50, d1cfgr);
}

// PLL1 from HSI / 4 (16 MHz) * DIVN1 / 2: sys_ck = 8 MHz * divn; then D1CFGR (D1CPRE, HPRE).
unsafe fn pll(divn: u32, d1cfgr: u32) {
    pll_p(divn, 1, d1cfgr);
}

// The same with DIVP1 = divp: pll1_p_ck = vco1_ck / (divp + 1) (p404). PLL1RGE = 11 for its 16 MHz reference (pp. 401-402):
// the RCC guard has no frequency for a range left at reset (TVI-WO-1300c, ruled 2026-11-26T04:03Z Q3, Q9).
unsafe fn pll_p(divn: u32, divp: u32, d1cfgr: u32) {
    w(RCC + 0x28, (r(RCC + 0x28) & !0x3F3) | (4 << 4));   // PLLCKSELR: PLLSRC = HSI, DIVM1 = 4 (p398-399)
    w(RCC + 0x2C, 0x01FF_000C);                           // PLLCFGR: its reset value with PLL1RGE = 11, 8-16 MHz (p400-402)
    w(RCC + 0x30, 0x0101_0000 | (divp << 9) | (divn - 1)); // PLL1DIVR: DIVP1, DIVN1 (p403-404)
    w(RCC, r(RCC) | 1 << 24);                             // PLL1ON (p385)
    while r(RCC) >> 25 & 1 == 0 {}
    w(RCC + 0x18, d1cfgr);                                // D1CFGR: D1CPRE, HPRE (p394-395)
}

unsafe fn switch(sw: u32) {
    w(RCC + 0x10, (r(RCC + 0x10) & !7) | sw);
    while r(RCC + 0x10) >> 3 & 7 != sw {}
}

unsafe fn unlock(keyr: usize) {
    w(keyr, KEY1);
    w(keyr, KEY2);
}

#[cfg(path = "ap")]
// ArduPilot Copter-4.7.2 (a799dfb6...): its RCC (offset, value) pairs in first-occurrence order up to its switch to
// PLL1 (firmware/rcc.rs, from TVI-WO-1300.rm0433.md §4, §5), its FLASH_ACR write in place (0xFFF, before CFGR = 0x8003;
// TVI-WO-1304.measure.md §2.1). The pairs after the switch are enables and resets, which no flash rule reads.
static SEQ: [(u32, u32); 32] = [
    (0x088, 0x000007FF), (0x088, 0x00000000), (0x0E0, 0x000007FF), (0x108, 0x312807FF),
    (0x0F4, 0x00010002), (0x11C, 0x0421DEAA), (0x000, 0x00000025), (0x010, 0x00000000),
    (0x000, 0x00000001), (0x004, 0x40000000), (0x074, 0x00000000), (0x02C, 0x01FF0000),
    (0x010, 0x08108800), (0x000, 0x00001025), (0x028, 0x00408040), (0x034, 0x00000000),
    (0x030, 0x01090231), (0x03C, 0x00000000), (0x038, 0x0704022C), (0x044, 0x00000000),
    (0x040, 0x07040E0E), (0x02C, 0x01BF0FFD), (0x000, 0x15003025), (0x018, 0x00000048),
    (0x01C, 0x00000440), (0x020, 0x00000040), (0x04C, 0x00000020), (0x050, 0x10010000),
    (0x054, 0x00E01009), (0x058, 0x10010100), (0xFFF, 0x00000032), (0x010, 0x00008003),
];
#[cfg(path = "gen")]
// the generated firmware for hse-crystal-16 (Aethelgard 1cb994c1): its PWR_D3CR, RCC and FLASH_ACR writes in order
// (TVI-WO-1304.measure.md §2.3; 0xFFE is PWR_D3CR, 0xFFF FLASH_ACR)
static SEQ: [(u32, u32); 9] = [
    (0xFFE, 0x00008000), (0x000, 0x00010001), (0x000, 0x00090001), (0x028, 0x00000052), (0x02C, 0x00030004),
    (0x030, 0x0103064F), (0x000, 0x01090001), (0xFFF, 0x00000011), (0x010, 0x00000003),
];

#[no_mangle]
pub unsafe extern "C" fn reset() -> ! {
    #[cfg(any(path = "ap", path = "gen"))]
    {
        #[cfg(path = "ap")]
        vos1();
        for &(off, v) in SEQ.iter() {
            match off {
                0xFFE => w(PWR_D3CR, v),
                0xFFF => {
                    w(ACR, v);
                    while r(ACR) & 0xF != v & 0xF {}
                }
                _ => {
                    w(RCC + off as usize, v);
                    if off == 0x000 {
                        // wait for every PLL the write turned on (PLLxRDY = PLLxON, bits 25/27/29), as the firmwares do
                        while (r(RCC) >> 1) & v & 0x1500_0000 != v & 0x1500_0000 {}
                    }
                    if off == 0x010 && v & 7 != 0 {
                        while r(RCC + 0x10) >> 3 & 7 != v & 7 {}
                    }
                }
            }
        }
    }
    #[cfg(path = "ap")]
    {
        // TVI-WO-1304.measure.md §2.1, ArduPilot's bank 2 operations as traced: the erase of sector 7
        unlock(KEYR1);
        unlock(KEYR2);
        w(CCR1, 0xFFFF_FFFF);
        w(CCR2, 0xFFFF_FFFF);
        w(SR2, 0xFFFF_FFFF);
        write_volatile(MARK_READ2, r(SR2));
        w(CR2, 0x724);
        write_volatile(MARK_READ, r(CR2));
        w(CR2, r(CR2) | 0x80);
        while r(SR2) & 0x5 != 0 {}
        w(CR1, 0x31);
        w(CR2, r(CR2) | 1);
        // ... then one 32-byte flash word programmed into it
        unlock(KEYR1);
        unlock(KEYR2);
        w(CCR2, 0xFFFF_FFFF);
        w(CR2, r(CR2) | 0x2);
        for i in 0..8 {
            w(0x081E_0000 + 4 * i, 0x625B_6851 ^ i as u32);
        }
        while r(SR2) & 0x1_0000 == 0 {}
        w(CCR2, 0xFFFF_FFFF);
        w(CR2, r(CR2) & !0x2);
        w(CR1, 0x31);
        w(CR2, r(CR2) | 1);
    }
    #[cfg(path = "gen")]
    {
        write_volatile(MARK_READ, r(OPTSR_CUR));
    }
    #[cfg(path = "order")]
    {
        vos1();
        w(ACR, 0x22);
        while r(ACR) & 0xF != 2 {}
        pll400(0x8);
        switch(3);
        switch(0);
        w(ACR, 0x00);
        write_volatile(MARK_READ, r(ACR));
    }
    #[cfg(path = "clockfirst")]
    {
        vos1();
        w(ACR, 0x00);
        pll400(0x8);
        switch(3);
    }
    #[cfg(any(path = "lowerfast", path = "alias"))]
    {
        vos1();
        w(ACR, 0x22);
        pll400(0x8);
        switch(3);
        #[cfg(path = "lowerfast")]
        w(ACR, 0x21);
        #[cfg(path = "alias")]
        w(ACR_ALIAS, 0x21);
    }
    #[cfg(path = "prescaler")]
    {
        vos1();
        w(ACR, 0x11);
        pll400(0x9);
        switch(3);
        w(RCC + 0x18, 0x8);
    }
    #[cfg(any(path = "vosunknown", path = "vos3"))]
    {
        w(ACR, 0x22);
        pll400(0x8);
        switch(3);
        write_volatile(MARK_READ, r(ACR));
    }
    #[cfg(path = "vosunknownlow")]
    {
        w(ACR, 0x11);
        pll400(0x8);
        switch(3);
    }
    #[cfg(path = "over225")]
    {
        w(ACR, 0x27);
        pll400(0x0);
        switch(3);
    }
    #[cfg(path = "lockedcr")]
    {
        w(CR2, r(CR2) | 0x2);
    }
    #[cfg(path = "lockedsame")]
    {
        w(CR2, r(CR2));
        write_volatile(MARK_READ, r(CR2));
    }
    #[cfg(path = "badkey")]
    {
        w(KEYR2, 0x1234_5678);
    }
    #[cfg(path = "keytwice")]
    {
        unlock(KEYR2);
        w(KEYR2, KEY1);
    }
    #[cfg(path = "keymid")]
    {
        w(KEYR1, KEY1);
        w(ACR, 0x37);   // a register common to both banks (Table 20 p172), at bank 1's offsets: not "of the same bank", passes
        w(CR1, 0x30);
    }
    #[cfg(path = "keyhalf")]
    {
        write_volatile(KEYR2 as *mut u16, KEY1 as u16);
    }
    #[cfg(any(path = "wrhlow", path = "wrhpg"))]
    {
        vos1();
        w(ACR, 0x12);
        pll400(0x8);
        switch(3);
        unlock(KEYR2);
        #[cfg(path = "wrhlow")]
        {
            w(CR2, 0x724);
            w(CR2, 0x7A4);
        }
        #[cfg(path = "wrhpg")]
        w(CR2, 0x22);
    }
    #[cfg(any(path = "divp1zero", path = "divp1odd"))]
    {
        vos1();
        w(ACR, 0x04);
        #[cfg(path = "divp1zero")]
        pll_p(25, 0, 0x8);   // a 400 MHz VCO, DIVP1 = 0 (/1), HPRE /2
        #[cfg(path = "divp1odd")]
        pll_p(25, 2, 0x8);   // DIVP1 = 2 (/3), a write the stock model ignores
        switch(3);
    }
    #[cfg(path = "edge")]
    {
        vos1();
        w(ACR, 0x01);
        pll(35, 0x8);   // 280 MHz, HPRE /2: an AXI clock of 140 MHz, the top of 1 WS's band at VOS1
        switch(3);
        write_volatile(MARK_READ, r(ACR));
    }
    #[cfg(path = "vos2")]
    {
        w(PWR_D3CR, 0x8000);
        w(ACR, 0x01);
        pll(30, 0x8);   // 240 MHz, HPRE /2: 120 MHz
        switch(3);
    }
    #[cfg(path = "d1cpre")]
    {
        vos1();
        w(ACR, 0x01);
        pll400(0x808);   // D1CPRE /2, HPRE /2: 100 MHz
        switch(3);
        w(RCC + 0x18, 0x8);
    }
    #[cfg(path = "wrhrcc")]
    {
        vos1();
        w(ACR, 0x12);
        unlock(KEYR2);
        w(CR2, 0x22);
        pll400(0x8);
        switch(3);
    }
    #[cfg(path = "locking")]
    {
        unlock(KEYR2);
        w(CR2, 0x33);
    }
    #[cfg(path = "worse")]
    {
        vos1();
        w(ACR, 0x11);
        pll400(0x8);
        switch(3);
        w(ACR, 0x10);
        write_volatile(MARK_READ, r(ACR));
    }
    #[cfg(path = "optsr")]
    {
        write_volatile(MARK_READ, r(OPTSR_CUR));
        write_volatile(MARK_READ2, r(OPTSR_ALIAS));
    }
    #[cfg(path = "optwrite")]
    {
        w(OPTKEYR, 0x0819_2A3B);
    }
    #[cfg(path = "bank1")]
    {
        unlock(KEYR1);
        w(CR1, 0x32);
    }
    #[cfg(path = "readreg")]
    {
        write_volatile(MARK_READ, r(PRAR_CUR1));
    }
    write_volatile(MARK_AFTER, 0xAF7E_0002);
    loop {}
}

#[panic_handler]
fn panic(_: &core::panic::PanicInfo) -> ! {
    loop {}
}
