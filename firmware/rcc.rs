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
//!   path="divq"      - DIVQ1EN cleared before PLL1 starts (allowed while PLL1 is off, p401), then PLL1 and SPI1EN:
//!                      pll1_q_ck's output is disabled, so SPI1 starts with no kernel clock; predicted-fault.
//!   path="pllref"    - PLLSRC set to 11 ("no clock", p399), then PLL1ON: refused by TVI-RCC-PLL-REF, unmodeled.
//!   path="divm0"     - DIVM1 = 0 ("prescaler disabled", p398-399), then PLL1ON: refused by TVI-RCC-PLL-REF, unmodeled.
//!   path="sw"        - CFGR.SW set to PLL1 with PLL1 off: refused by TVI-RCC-SW, unmodeled (p350).
//!   path="width"     - SPI1EN set by a 16-bit store: refused by TVI-RCC-WIDTH, unmodeled.
//!   path="read"      - a read of RCC offset 0x014, where RM0433 has no register: refused by TVI-RCC-READ, unmodeled.
//!   path="usart2"    - USART234578SEL written 001 (pll2_q_ck) with PLL2 off, USART2EN, then USART2 CR1.UE: the test asks
//!                      for USART2's kernel clock there; predicted-fault (p417, p371).
//!   path="ardupilot", "gen-s03", "gen-hc16" - the RCC write sequences of ArduPilot and of two generated boards, as
//!                      measured (SEQ below), with no SPI start: each passes, enforcing (WO §2).
//! These end at their own access, with no SPI start.
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
const RCC_CFGR: *mut u32 = (RCC + 0x010) as *mut u32;
const RCC_PLLCKSELR: *mut u32 = (RCC + 0x028) as *mut u32;
const RCC_D2CCIP2R: *mut u32 = (RCC + 0x054) as *mut u32;
const RCC_APB1LENR: *mut u32 = (RCC + 0x0E8) as *mut u32;
const USART2_CR1: *mut u32 = 0x4000_4400 as *mut u32;
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


// RCC write sequences measured in TVI-WO-1300 §1.1, replayed by the paths of the same name. (offset, value).
#[cfg(path = "ardupilot")]
// ArduPilot Copter-4.7.2 (a799dfb6...) on sentinel-02-own: its 26,012 RCC writes' 71 distinct (offset, value) pairs in first-occurrence order (TVI-WO-1300.rm0433.md §4, §5)
static SEQ: [(u32, u32); 71] = [
    (0x088, 0x000007FF), (0x088, 0x00000000), (0x0E0, 0x000007FF), (0x108, 0x312807FF),
    (0x0F4, 0x00010002), (0x11C, 0x0421DEAA), (0x000, 0x00000025), (0x010, 0x00000000),
    (0x000, 0x00000001), (0x004, 0x40000000), (0x074, 0x00000000), (0x02C, 0x01FF0000),
    (0x010, 0x08108800), (0x000, 0x00001025), (0x028, 0x00408040), (0x034, 0x00000000),
    (0x030, 0x01090231), (0x03C, 0x00000000), (0x038, 0x0704022C), (0x044, 0x00000000),
    (0x040, 0x07040E0E), (0x02C, 0x01BF0FFD), (0x000, 0x15003025), (0x018, 0x00000048),
    (0x01C, 0x00000440), (0x020, 0x00000040), (0x04C, 0x00000020), (0x050, 0x10010000),
    (0x054, 0x00E01009), (0x058, 0x10010100), (0x010, 0x00008003), (0x0DC, 0x20000000),
    (0x104, 0xE0000271), (0x0DC, 0x40000000), (0x0DC, 0x80000000), (0x080, 0x7FFFFFFF),
    (0x080, 0x00000000), (0x084, 0xFFFFFFFF), (0x084, 0x00000000), (0x07C, 0x7FFFEFFF),
    (0x07C, 0x00000000), (0x088, 0xFFFFF800), (0x090, 0xFFFFFFFF), (0x090, 0x00000000),
    (0x094, 0xFFFFFFFF), (0x094, 0x00000000), (0x098, 0xFFFFFFFF), (0x098, 0x00000000),
    (0x08C, 0xFFFFFFFF), (0x08C, 0x00000000), (0x09C, 0xFFFFFFFD), (0x09C, 0x00000000),
    (0x0E8, 0x00000008), (0x110, 0xE8FFCBFF), (0x0DC, 0x00000040), (0x104, 0xE0000231),
    (0x0D0, 0x00010000), (0x0D8, 0x00000001), (0x100, 0x1E03C023), (0x0F0, 0x00000010),
    (0x118, 0x31D73033), (0x0D8, 0x00000003), (0x098, 0x00001000), (0x0F0, 0x00001010),
    (0x100, 0x1E03C021), (0x118, 0x31D72033), (0x090, 0x00200000), (0x0E8, 0x00200008),
    (0x090, 0x00004000), (0x0E8, 0x00204008), (0x110, 0xE8FF8BFF),
];
#[cfg(path = "gen-s03")]
// the generated firmware for sentinel-03 (Aethelgard 75da8053): its RCC writes in order
static SEQ: [(u32, u32); 13] = [
    (0x050, 0x00004000), (0x0E0, 0x00000001), (0x0E0, 0x00000003), (0x0E0, 0x00000007),
    (0x0E0, 0x00000017), (0x0E8, 0x00200000), (0x0E8, 0x00600000), (0x0F0, 0x00001000),
    (0x0E8, 0x00604000), (0x0F0, 0x00001001), (0x0F0, 0x00001011), (0x0E8, 0x00624000),
    (0x0E8, 0x00664000),
];
#[cfg(path = "gen-hc16")]
// the generated firmware for hse-crystal-16 (Aethelgard 75da8053): its RCC writes in order
static SEQ: [(u32, u32); 16] = [
    (0x000, 0x00010001), (0x000, 0x00090001), (0x028, 0x00000052), (0x02C, 0x00030004),
    (0x030, 0x0103064F), (0x000, 0x01090001), (0x010, 0x00000003), (0x0E0, 0x00000001),
    (0x0E0, 0x00000003), (0x0E8, 0x00200000), (0x0F0, 0x00001000), (0x0E8, 0x00204000),
    (0x0F0, 0x00001001), (0x0E8, 0x00204001), (0x0E8, 0x00224001), (0x0E8, 0x00264001),
];

#[no_mangle]
pub unsafe extern "C" fn reset() -> ! {
    #[cfg(any(path = "ardupilot", path = "gen-s03", path = "gen-hc16"))]
    {
        for &(off, v) in SEQ.iter() {
            write_volatile((RCC + off as usize) as *mut u32, v);
            if off == 0x000 {
                // as the firmwares do after a CR write: wait for every PLL it turned on (PLLxRDY = PLLxON, bits 25/27/29)
                while (read_volatile(RCC_CR) >> 1) & v & 0x1500_0000 != v & 0x1500_0000 {}
            }
        }
        write_volatile(MARK_AFTER, 0xAF7E_0002);
        loop {}
    }
    #[cfg(path = "pllref")]
    {
        write_volatile(RCC_PLLCKSELR, read_volatile(RCC_PLLCKSELR) | 0b11); // PLLSRC = 11: no clock to the PLLs
        write_volatile(RCC_CR, read_volatile(RCC_CR) | CR_PLL1ON);
    }
    #[cfg(path = "divm0")]
    {
        write_volatile(RCC_PLLCKSELR, read_volatile(RCC_PLLCKSELR) & !(0x3F << 4)); // DIVM1 = 0: prescaler disabled
        write_volatile(RCC_CR, read_volatile(RCC_CR) | CR_PLL1ON);
    }
    #[cfg(path = "sw")]
    write_volatile(RCC_CFGR, (read_volatile(RCC_CFGR) & !0b111) | 0b011); // SW = PLL1, PLL1 off
    #[cfg(path = "width")]
    write_volatile((RCC + 0x0F0) as *mut u16, SPI1EN as u16);
    #[cfg(path = "read")]
    let _ = read_volatile((RCC + 0x014) as *const u32);
    #[cfg(path = "usart2")]
    {
        write_volatile(RCC_D2CCIP2R, (read_volatile(RCC_D2CCIP2R) & !0b111) | 0b001); // USART234578SEL = pll2_q_ck
        write_volatile(RCC_APB1LENR, read_volatile(RCC_APB1LENR) | (1 << 17)); // USART2EN
        let _ = read_volatile(RCC_APB1LENR);
        write_volatile(USART2_CR1, 1); // UE: the start
    }
    #[cfg(any(path = "pllref", path = "divm0", path = "sw", path = "width", path = "read", path = "usart2"))]
    {
        write_volatile(MARK_AFTER, 0xAF7E_0002);
        loop {}
    }
    #[cfg(path = "divq")]
    write_volatile(RCC_PLLCFGR, read_volatile(RCC_PLLCFGR) & !(1 << 17));
    #[cfg(any(path = "clocked", path = "field", path = "pll", path = "switched", path = "alias", path = "divq"))]
    pll1_on();
    #[cfg(path = "switch")]
    spi123sel(0b001);
    #[cfg(any(path = "stuck", path = "switched"))]
    spi123sel(0b100);
    #[cfg(path = "field")]
    spi123sel(0b011);
    #[cfg(path = "pll")]
    write_volatile(RCC_PLLCFGR, read_volatile(RCC_PLLCFGR) & !(1 << 17));
    #[cfg(not(any(path = "alias", path = "pllref", path = "divm0", path = "sw", path = "width", path = "read", path = "usart2")))]
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
