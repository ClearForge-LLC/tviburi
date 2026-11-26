//! Bare-metal probes for the PWR guard (TVI-WO-1308 (3)). No dependencies; built with rustc alone, as flash.rs. Variant chosen
//! at build time with `--cfg path="..."`. Registers: RM0433 Rev 9 §6.8 (PWR, pp. 304-313), §8.7 (RCC), §4.9.1 (FLASH_ACR); pages
//! in tests/1308/pwr.sh. `supply(v)` writes PWR_CR3 = v and waits for PWR_CSR1.ACTVOSRDY (p262); `vos(v)` writes PWR_D3CR.VOS
//! and waits for VOSRDY (p310); `pll(divn, d1, d2, d3)` starts PLL1 from HSI / 4 (16 MHz, PLL1RGE 11) with DIVP1 /2, sys_ck =
//! 8 MHz x divn, and writes D1CFGR, D2CFGR, D3CFGR first; `switch(sw)` moves sys_ck and waits for SWS.
//!   path="second"    - (WO acceptance 2) CR3 = 0x02, then 0x06: the lower byte changed after its first write: TVI-PWR-SUPPLY,
//!                      predicted-fault, ignored (CR3 keeps 0x02, read by the monitor).
//!   path="same"      - ArduPilot's two CR3 writes, 0x02 then 0x01000002 (the lower byte unchanged, USB33DEN): passes.
//!                      MARK_READ = CR3 (0x05000002: USB33RDY at once).
//!   path="ap"        - ArduPilot's PWR sequence exactly (measure.md §3.1), then its RCC sequence to PLL1 (400/200/100 MHz,
//!                      flash.rs's `ap` pairs with FLASH_ACR = 0x32), then RCC_BDCR.RTCEN with DBP 1: passes, revision V.
//!                      MARK_READ = CR2 (0: BREN not taken), MARK_READ2 = D3CR (0xE000).
//!   path="gen"       - the 14 generated boots' PWR sequence (read CR3, CR3 = 0x42, read CR3, ACTVOSRDY, VOS 10, VOSRDY), then
//!                      hse-crystal-16's RCC writes to 64 MHz (flash.rs's `gen`): passes. MARK_READ = the first CR3 read (0x46),
//!                      MARK_READ2 = the second (0x42).
//!   path="order"     - (WO acceptance 3, the passing order) CR3, VOS1, VOSRDY, LATENCY 2, then 400/200/100 MHz; then down: sys_ck
//!                      back to HSI and VOS3: passes. MARK_READ = D3CR after VOS1 (0xE000), MARK_READ2 = CSR1 at the end (0x6000).
//!   path="clockfirst"- (WO acceptance 3) CR3, VOS left at Scale 3, then sys_ck to 400 MHz: TVI-RCC-VOS, predicted-fault, at CFGR.
//!   path="invalid"   - CR3 = 0x03 (LDOEN = BYPASS = 1, Table 34 p261): TVI-PWR-SUPPLY, predicted-fault.
//!   path="vosearly"  - VOS = 11 before any supply configuration (ACTVOSRDY 0): TVI-PWR-VOS, unmodeled (p262).
//!   path="voslow"    - CR3, VOS1, 400/200/100 MHz, then VOS = 01 with the clocks left: TVI-PWR-VOS, predicted-fault (p310).
//!   path="cr2dbp"    - CR2.MONEN = 1 with DBP = 0: TVI-PWR-DBP, unmodeled (p306).
//!   path="bdcr"      - RCC_BDCR.RTCEN = 1 with DBP = 0: TVI-RCC-DBP, unmodeled (p427).
//!   path="bdcrok"    - DBP = 1, then RCC_BDCR.RTCEN = 1: passes. MARK_READ = BDCR (0x8000).
//!   path="flashvos3" - CR3, VOS = 00 ("Reserved (Scale 3 selected)", p310), LATENCY 1 (enough at VOS1), then 200 MHz with HPRE /2
//!                      (AXI 100 MHz) and every APB /2 (50 MHz), all within VOS3's limits: TVI-FLASH-WAIT at CFGR, naming VOS3
//!                      (Table 17 needs 2 there).
//!   path="apbvos3"   - CR3, VOS3, D3PPRE /2 (APB4, where RTCAPBEN is enabled from reset, p471, to 32 MHz), then USART3EN with
//!                      APB1 at 64 MHz: VOS3's APB limit (50 MHz) holds "when at least one peripheral is enabled" (DS12117 p210
//!                      note 6), judged per APB bus: TVI-RCC-VOS, predicted-fault, at the RCC_APB1LENR write, not before it.
//!   path="vos0"      - CR3, VOS1, LATENCY 4, then 480 MHz (a 960 MHz VCO /2), AHB 240, every APB 120: above VOS1's limits, within
//!                      VOS0's, which needs SYSCFG_PWRCR.ODEN (p280-281), which the twin cannot read: TVI-RCC-VOS, unmodeled, at CFGR.
//!   path="revy200"   - (revision Y) CR3, VOS3, LATENCY 4, then 200 MHz with every prescaler /1 (AHB and APB 200 MHz): rev Y's
//!                      datasheet states only a CPU limit (DS12117 Rev 10 p129-130), which 200 MHz meets: passes. MARK_READ = CFGR.
//!   path="flashlow"  - CR3, VOS1, LATENCY 0 at HSI 64 MHz (enough at VOS1), then VOS lowered to Scale 2 (64 MHz needs 1 WS there,
//!                      Table 17 p160): TVI-FLASH-WAIT, predicted-fault, at the PWR_D3CR write (p310).
//!   path="clockearly"- no supply configuration (ACTVOSRDY 0), then sys_ck to 160 MHz on PLL1 (AHB 80, APB 40: within VOS3):
//!                      TVI-RCC-VOS, unmodeled, at CFGR (p308: CR3 "shall be written before changing ... ck_sys"; p262).
//!   path="vos0byp"   - CR3 = 0x01 (LDO bypassed, Table 34 p261), VOS1, then 480/240/120 MHz: within VOS0's limits, but "VOS0 is
//!                      available only when the LDO regulator is ON" (DS12117 Rev 10 p210 note 4): TVI-RCC-VOS, predicted-fault.
//!   path="bdcrbyte"  - a byte write of RCC_BDCR's byte 1 (RTCEN) with DBP = 0: TVI-RCC-DBP, unmodeled (p427; a byte access, p427).
//!   path="noclock"   - (flash recorded) CR3, VOS1, PLL1 with DIVP1 = 2 (/3, "odd division factors are not allowed", p404: no
//!                      frequency rule) switched in, then VOS lowered to Scale 3: TVI-PWR-VOS, unmodeled (the clocks cannot be computed).
//!   path="nopwr"     - (no pwr at all on the platform) LATENCY 1, then an AXI clock of 200 MHz: TVI-FLASH-WAIT, decided at VOS1, its
//!                      record naming no peripheral at PWR_D3CR.
//!   path="hse400"    - (hseFrequency 16 MHz) CR3, VOS1, PLL1 from the HSE: 16 / 7 x 350 / 2 = 400 MHz exactly, AHB 200, every APB 100:
//!                      Scale 1's limits exactly (the twin's double is 400000000.00000006 before rounding): passes. MARK_READ = CFGR.
//!   path="hse400v3"  - the same sys_ck with D1CPRE /2 at Scale 3: CPU 200, AHB 100, every APB 50 (RTCAPBEN enabled): passes.
//!   path="hse25"     - (hseFrequency 25 MHz) CR3, VOS1, 25 / 11 x 352 / 2 = 400 MHz exactly, AHB 200, APB 100: passes.
//!   path="bothrec"   - (rcc recorded) CR3 at Scale 3, USART3 started (USART3EN, BRR, TE | UE), then USART3EN cleared in one RCC_APB1LENR
//!                      write: it stops USART3's kernel clock (TVI-RCC-CLOCK, p376) and leaves APB4 (RTCAPBEN) the first APB above
//!                      VOS3's 50 MHz at 64 MHz, a new verdict (TVI-RCC-VOS): both evaluated in recorded mode (model/Refusal.cs).
//!   path="notjudged" - (a platform with no PWR guard, ruled Q20 (A)) sys_ck to 400 MHz, AHB 200, every APB 200 MHz, above every
//!                      scale's limit, LATENCY 2: no TVI-RCC-VOS, passes. MARK_READ = CFGR (SWS = PLL1), MARK_READ2 = D1CFGR.
#![no_std]
#![no_main]
#![allow(dead_code)]

use core::ptr::{read_volatile, write_volatile};

const PWR: usize = 0x5802_4800;
const CR1: usize = PWR;
const CSR1: usize = PWR + 0x04;
const CR2: usize = PWR + 0x08;
const CR3: usize = PWR + 0x0C;
const CPUCR: usize = PWR + 0x10;
const D3CR: usize = PWR + 0x18;
const RCC: usize = 0x5802_4400;
const BDCR: usize = RCC + 0x70;
const APB1LENR: usize = RCC + 0xE8;
const ACR: usize = 0x5200_2000;
const MARK_AFTER: *mut u32 = 0x2000_0104 as *mut u32;
const MARK_READ: *mut u32 = 0x2000_0108 as *mut u32;
const MARK_READ2: *mut u32 = 0x2000_010C as *mut u32;

unsafe fn w(a: usize, v: u32) {
    write_volatile(a as *mut u32, v);
}

unsafe fn r(a: usize) -> u32 {
    read_volatile(a as *const u32)
}

unsafe fn supply(v: u32) {
    w(CR3, v);
    while r(CSR1) & 1 << 13 == 0 {}
}

unsafe fn vos(v: u32) {
    w(D3CR, v << 14);
    while r(D3CR) & 1 << 13 == 0 {}
}

unsafe fn pll(divn: u32, d1: u32, d2: u32, d3: u32) {
    pllp(divn, 1, d1, d2, d3);
}

// The same with DIVP1 = divp (pll1_p_ck = vco1_ck / (divp + 1), p404).
unsafe fn pllp(divn: u32, divp: u32, d1: u32, d2: u32, d3: u32) {
    w(RCC + 0x18, d1);                                     // D1CFGR: D1CPRE, D1PPRE, HPRE (p394-395)
    w(RCC + 0x1C, d2);                                     // D2CFGR: D2PPRE2, D2PPRE1 (p396)
    w(RCC + 0x20, d3);                                     // D3CFGR: D3PPRE (p397)
    w(RCC + 0x28, (r(RCC + 0x28) & !0x3F3) | (4 << 4));   // PLLCKSELR: PLLSRC = HSI, DIVM1 = 4 (p398-399)
    w(RCC + 0x2C, 0x01FF_000C);                           // PLLCFGR: PLL1RGE = 11, 8-16 MHz (p400-402)
    w(RCC + 0x30, 0x0101_0000 | (divp << 9) | (divn - 1)); // PLL1DIVR: DIVP1, DIVN1 (p403-404)
    w(RCC, r(RCC) | 1 << 24);                             // PLL1ON (p385)
    while r(RCC) >> 25 & 1 == 0 {}
}

// PLL1 from the HSE (HSEON, HSERDY, p384): DIVM1, PLLSRC = HSE, PLL1RGE = 01 for a 2-4 MHz reference (p401-402), DIVN1, DIVP1 /2.
unsafe fn hsepll(divm: u32, divn: u32, d1: u32, d2: u32, d3: u32) {
    w(RCC, r(RCC) | 1 << 16);
    while r(RCC) >> 17 & 1 == 0 {}
    w(RCC + 0x18, d1);
    w(RCC + 0x1C, d2);
    w(RCC + 0x20, d3);
    w(RCC + 0x28, (r(RCC + 0x28) & !0x3F3) | (divm << 4) | 2);
    w(RCC + 0x2C, 0x01FF_0004);
    w(RCC + 0x30, 0x0101_0200 | (divn - 1));
    w(RCC, r(RCC) | 1 << 24);
    while r(RCC) >> 25 & 1 == 0 {}
}

unsafe fn switch(sw: u32) {
    w(RCC + 0x10, (r(RCC + 0x10) & !7) | sw);
    while r(RCC + 0x10) >> 3 & 7 != sw {}
}

// 400 MHz, AHB 200, every APB 100: Scale 1's limits exactly (DS12117 Rev 10 p210), LATENCY 2 first (Table 17 p160, VOS1).
unsafe fn to400() {
    w(ACR, 0x32);
    while r(ACR) & 0xF != 2 {}
    pll(50, 0x48, 0x440, 0x40);
    switch(3);
}

#[cfg(path = "ap")]
// ArduPilot Copter-4.7.2's RCC (offset, value) pairs to its switch to PLL1, with its FLASH_ACR write in place (0xFFF), as
// firmware/flash.rs's `ap` (TVI-WO-1300.rm0433.md §4; TVI-WO-1304.measure.md §2.1).
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
// hse-crystal-16's RCC and FLASH_ACR writes after its PWR sequence (firmware/flash.rs's `gen`, without its PWR_D3CR write).
static SEQ: [(u32, u32); 8] = [
    (0x000, 0x00010001), (0x000, 0x00090001), (0x028, 0x00000052), (0x02C, 0x00030004),
    (0x030, 0x0103064F), (0x000, 0x01090001), (0xFFF, 0x00000011), (0x010, 0x00000003),
];

#[no_mangle]
pub unsafe extern "C" fn reset() -> ! {
    #[cfg(path = "second")]
    {
        supply(0x02);
        w(CR3, 0x06);
    }
    #[cfg(path = "same")]
    {
        supply(0x02);
        w(CR3, 0x0100_0002);
        write_volatile(MARK_READ, r(CR3));
    }
    #[cfg(path = "ap")]
    {
        // measure.md §3.1: CR3 = 0x2, ACTVOSRDY, CR1 = 0xF000C000, CR2 = 0x1 (DBP 0), CR3 = 0x1000002, CPUCR = 0, VOS 11, VOSRDY,
        // CR1 read and rewritten with DBP
        supply(0x02);
        w(CR1, 0xF000_C000);
        w(CR2, 0x1);
        w(CR3, 0x0100_0002);
        w(CPUCR, 0);
        vos(3);
        w(CR1, r(CR1) | 1 << 8);
        write_volatile(MARK_READ, r(CR2));
        write_volatile(MARK_READ2, r(D3CR));
    }
    #[cfg(path = "gen")]
    {
        write_volatile(MARK_READ, r(CR3));
        w(CR3, 0x42);
        write_volatile(MARK_READ2, r(CR3));
        while r(CSR1) & 1 << 13 == 0 {}
        vos(2);
    }
    #[cfg(any(path = "ap", path = "gen"))]
    {
        for &(off, v) in SEQ.iter() {
            if off == 0xFFF {
                w(ACR, v);
                while r(ACR) & 0xF != v & 0xF {}
                continue;
            }
            w(RCC + off as usize, v);
            if off == 0x000 {
                while (r(RCC) >> 1) & v & 0x1500_0000 != v & 0x1500_0000 {}
            }
            if off == 0x010 && v & 7 != 0 {
                while r(RCC + 0x10) >> 3 & 7 != v & 7 {}
            }
        }
    }
    #[cfg(path = "ap")]
    {
        w(BDCR, r(BDCR) | 1 << 15);
    }
    #[cfg(path = "order")]
    {
        supply(0x02);
        vos(3);
        write_volatile(MARK_READ, r(D3CR));
        to400();
        switch(0);
        vos(1);
        write_volatile(MARK_READ2, r(CSR1));
    }
    #[cfg(path = "clockfirst")]
    {
        supply(0x02);
        to400();
    }
    #[cfg(path = "invalid")]
    {
        w(CR3, 0x03);
    }
    #[cfg(path = "vosearly")]
    {
        w(D3CR, 3 << 14);
    }
    #[cfg(path = "voslow")]
    {
        supply(0x02);
        vos(3);
        to400();
        w(D3CR, 1 << 14);
    }
    #[cfg(path = "cr2dbp")]
    {
        w(CR2, 1 << 4);
    }
    #[cfg(path = "bdcr")]
    {
        w(BDCR, 1 << 15);
    }
    #[cfg(path = "bdcrok")]
    {
        w(CR1, r(CR1) | 1 << 8);
        w(BDCR, 1 << 15);
        write_volatile(MARK_READ, r(BDCR));
    }
    #[cfg(path = "flashvos3")]
    {
        supply(0x02);
        w(D3CR, 0);
        w(ACR, 0x01);
        while r(ACR) & 0xF != 1 {}
        pll(25, 0x48, 0x440, 0x40);   // 200 MHz; AHB 100; APB 50
        switch(3);
    }
    #[cfg(path = "apbvos3")]
    {
        supply(0x02);
        w(RCC + 0x20, 0x40);   // D3CFGR: D3PPRE /2 (p397)
        w(APB1LENR, 1 << 18);
    }
    #[cfg(path = "vos0")]
    {
        supply(0x02);
        vos(3);
        w(ACR, 0x34);
        while r(ACR) & 0xF != 4 {}
        pll(60, 0x48, 0x440, 0x40);   // 480 MHz; AHB 240; every APB 120
        switch(3);
    }
    #[cfg(path = "revy200")]
    {
        supply(0x02);
        w(ACR, 0x34);
        while r(ACR) & 0xF != 4 {}
        pll(25, 0, 0, 0);   // 200 MHz; AHB 200; every APB 200
        switch(3);
        write_volatile(MARK_READ, r(RCC + 0x10));
    }
    #[cfg(path = "flashlow")]
    {
        supply(0x02);
        vos(3);
        w(ACR, 0x00);
        while r(ACR) & 0xF != 0 {}
        w(D3CR, 2 << 14);
    }
    #[cfg(path = "clockearly")]
    {
        pll(20, 0x48, 0x440, 0x40);   // 160 MHz; AHB 80; every APB 40
        switch(3);
    }
    #[cfg(path = "vos0byp")]
    {
        supply(0x01);
        vos(3);
        w(ACR, 0x34);
        while r(ACR) & 0xF != 4 {}
        pll(60, 0x48, 0x440, 0x40);   // 480 MHz; AHB 240; every APB 120
        switch(3);
    }
    #[cfg(path = "bdcrbyte")]
    {
        write_volatile((BDCR + 1) as *mut u8, 0x80);
    }
    #[cfg(path = "noclock")]
    {
        supply(0x02);
        vos(3);
        w(ACR, 0x34);
        while r(ACR) & 0xF != 4 {}
        pllp(25, 2, 0x48, 0x440, 0x40);   // a 400 MHz VCO, DIVP1 = 2 (/3): no frequency rule
        switch(3);
        w(D3CR, 1 << 14);
    }
    #[cfg(path = "nopwr")]
    {
        w(ACR, 0x01);
        while r(ACR) & 0xF != 1 {}
        pll(50, 0x08, 0x440, 0x40);   // 400 MHz; AXI 200
        switch(3);
    }
    #[cfg(path = "hse400")]
    {
        supply(0x02);
        vos(3);
        w(ACR, 0x32);
        while r(ACR) & 0xF != 2 {}
        hsepll(7, 350, 0x48, 0x440, 0x40);
        switch(3);
        write_volatile(MARK_READ, r(RCC + 0x10));
    }
    #[cfg(path = "hse400v3")]
    {
        supply(0x02);
        w(ACR, 0x32);
        while r(ACR) & 0xF != 2 {}
        hsepll(7, 350, 0x848, 0x440, 0x40);
        switch(3);
        write_volatile(MARK_READ, r(RCC + 0x10));
    }
    #[cfg(path = "hse25")]
    {
        supply(0x02);
        vos(3);
        w(ACR, 0x32);
        while r(ACR) & 0xF != 2 {}
        hsepll(11, 352, 0x48, 0x440, 0x40);
        switch(3);
        write_volatile(MARK_READ, r(RCC + 0x10));
    }
    #[cfg(path = "bothrec")]
    {
        supply(0x02);
        w(APB1LENR, r(APB1LENR) | 1 << 18);   // USART3EN (p463)
        let _ = r(APB1LENR);
        w(0x4000_4800 + 0x0C, 0x22C);          // USART3 BRR
        w(0x4000_4800, 0b1001);                // USART3 CR1: TE | UE
        w(APB1LENR, r(APB1LENR) & !(1 << 18));
    }
    #[cfg(path = "notjudged")]
    {
        w(ACR, 0x02);
        while r(ACR) & 0xF != 2 {}
        pll(50, 0x08, 0, 0);   // 400 MHz; AHB 200; every APB 200
        switch(3);
        write_volatile(MARK_READ, r(RCC + 0x10));
        write_volatile(MARK_READ2, r(RCC + 0x18));
    }
    write_volatile(MARK_AFTER, 0xAF7E_0002);
    loop {}
}

#[panic_handler]
fn panic(_: &core::panic::PanicInfo) -> ! {
    loop {}
}
