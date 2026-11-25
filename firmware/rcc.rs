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
//!   path="ardupilot" - ArduPilot's RCC write sequence, as measured (SEQ below), with no SPI start: it passes, enforcing.
//!   path="gen-s03", "gen-hc16" - the RCC write sequences of two generated boards (SEQ below), then SPI1's start, as the
//!                      generated firmware starts it (TVI-WO-1300a): sentinel-03 starts with no kernel clock (the per_ck
//!                      scar, refused at SPI1, TVI-SPI-KERNEL); hse-crystal-16 starts on pll1_q_ck and passes.
//! These end at their own access, with no SPI start.
//! TVI-WO-1300a. Each path below starts SPI1 on its kernel clock first ("clocked", or per_ck), unless it says otherwise.
//!   path="unenabled" - PLL1 started, then SPI1 configured with SPI1EN never set: TVI-SPI-ENABLE, predicted-fault (p373).
//!   path="lost-pll"  - then PLL1ON cleared (PLL1 is not the system clock): TVI-RCC-CLOCK, predicted-fault (p383, p371).
//!   path="lost-sel"  - then SPI123SEL written 001 (pll2_p_ck, PLL2 off): the mux waits, TVI-RCC-CLOCK (p414, p371).
//!   path="lost-en"   - then SPI1EN cleared: TVI-RCC-CLOCK (Table 62, p376).
//!   path="lost-osc"  - per_ck on hse_ck (HSE on, CKPERSEL = 10), then HSEON cleared (HSE neither the system clock nor
//!                      PLL1's source): TVI-RCC-CLOCK (p384, p412, p371).
//!   path="lost-ckper" - per_ck on hsi_ker_ck, then CKPERSEL written 01 (csi_ker_ck, CSI off): TVI-RCC-CLOCK (p412, p371).
//!   path="stopped"   - then SPE cleared, then SPI123SEL written 001: nothing runs, a valid order; passes.
//!   path="ckper-live" - per_ck on hsi_ker_ck with HSE on, then CKPERSEL written 10 (hse_ck): both inputs run, the mux
//!                      switches; passes.
//!   path="blocked"   - per_ck on hsi_ker_ck, then HSION cleared while HSI is the system clock: silicon blocks it
//!                      ("cannot be cleared", p386) and so does the model; passes.
//!   path="cfg2same"  - then CFG2 written with the value it holds, SPE = 1: nothing changes (TVI-WO-1300a (4), p2221); passes.
//!   path="sel5"      - SPI123SEL written 101 (reserved, "the kernel clock is disabled", p414): TVI-RCC-FIELD, unmodeled.
//!   path="divr"      - PLL1 started, then RCC_PLL1DIVR changed (DIVQ1 3 -> 4): silicon ignores it (p404); TVI-RCC-PLL.
//! TVI-WO-1300b (ruled 2026-11-25T03:03Z). These end at their own access, with no SPI start.
//!   path="usart-lost"  - USART1 started on rcc_pclk2 (USART16SEL 000: USART1EN, BRR, CR1 = UE|TE), then USART16SEL written
//!                        100 (csi_ker_ck, CSI off): the mux waits, so the running USART1 loses its kernel clock: TVI-RCC-CLOCK,
//!                        predicted-fault (p417, p371, p2019).
//!   path="usart-stopped" - the same with CR1 written 0 (UE = 0) before the selector: nothing runs, a valid order; passes.
//!   path="usart-lost-en" - USART3 started (USART234578SEL 000: rcc_pclk1), then USART3EN cleared: TVI-RCC-CLOCK (p376).
//!   path="usart-othersel" - USART1 started, then USART234578SEL (the other mux: USART2/3/UART4/5/7/8) written 100: USART1's
//!                        source is unchanged and nothing on that mux runs; passes (WO §5: the wrong source).
//!   path="hse-on"      - HSEON set and HSERDY polled: on a platform with no hseFrequency, TVI-RCC-HSE, unmodeled (p384);
//!                        with one declared, it passes.
//!   path="adc"         - the armed Sentinel-03 run's RCC writes that 1300 refused, in its order and values: ADC12EN and
//!                        ADC3EN set (p455, p458), then ADC12LPEN, ADC3LPEN, USART2LPEN and TIM1LPEN cleared (p477, p480,
//!                        p485, p492); passes.
//!   path="rtcen"       - RCC_BDCR read (0 at construction, p427), then ArduPilot's set_rtc_backup: BDCR written 0 (its
//!                        `|= STM32_RTCSEL`), an RTC backup register written and read back (rtc unlocked: RTCAPBEN = 1,
//!                        p471), BDCR written 0x8000 (RTCEN), BDCR read; the reads go to DTCM 0x20000108-0x20000110; passes.
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
const USART1: usize = 0x4001_1000;
const USART3: usize = 0x4000_4800;
const RCC_BDCR: *mut u32 = (RCC + 0x070) as *mut u32;
const RTC_BKP1R: *mut u32 = 0x5800_4054 as *mut u32;
/// TVI-WO-1300b's read-backs, read by the test.
const READ0: *mut u32 = 0x2000_0108 as *mut u32;
const READ1: *mut u32 = 0x2000_010C as *mut u32;
const READ2: *mut u32 = 0x2000_0110 as *mut u32;
const SPI1_CR1: *mut u32 = 0x4001_3000 as *mut u32;
const SPI1_CFG2: *mut u32 = 0x4001_300C as *mut u32;
/// Marker in DTCM, read back by the test: the firmware got past its last guarded write.
const MARK_AFTER: *mut u32 = 0x2000_0104 as *mut u32;

const CR_PLL1ON: u32 = 1 << 24;
const CR_PLL1RDY: u32 = 1 << 25;
const SPI1EN: u32 = 1 << 12;
const SPI123SEL_SHIFT: u32 = 12;
const RCC_D1CCIPR: *mut u32 = (RCC + 0x04C) as *mut u32;
const CR_HSION: u32 = 1 << 0;
const CR_HSEON: u32 = 1 << 16;
const CR_HSERDY: u32 = 1 << 17;
const CKPERSEL_SHIFT: u32 = 28;

unsafe fn ckpersel(v: u32) {
    write_volatile(RCC_D1CCIPR, (read_volatile(RCC_D1CCIPR) & !(3 << CKPERSEL_SHIFT)) | (v << CKPERSEL_SHIFT));
}

unsafe fn hse_on() {
    write_volatile(RCC_CR, read_volatile(RCC_CR) | CR_HSEON);
    while read_volatile(RCC_CR) & CR_HSERDY == 0 {}
}

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


// A USART started on its reset kernel source (USARTxSEL 000: rcc_pclk2 for USART1, rcc_pclk1 for USART3; p417): its bus
// enable, BRR at or above 16 (p2035), then CR1 = UE | TE, the start (TVI-USART-KERNEL asks the query; it runs).
unsafe fn usart_start(base: usize, enr: *mut u32, bit: u32) {
    write_volatile(enr, read_volatile(enr) | (1 << bit));
    let _ = read_volatile(enr);
    write_volatile((base + 0x0C) as *mut u32, 0x22C); // BRR
    write_volatile(base as *mut u32, 0b1001); // CR1: TE | UE
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
    #[cfg(any(path = "usart-lost", path = "usart-stopped", path = "usart-othersel"))]
    usart_start(USART1, RCC_APB2ENR, 4);
    #[cfg(path = "usart-stopped")]
    write_volatile(USART1 as *mut u32, 0); // UE = 0 first
    #[cfg(any(path = "usart-lost", path = "usart-stopped"))]
    write_volatile(RCC_D2CCIP2R, (read_volatile(RCC_D2CCIP2R) & !(0b111 << 3)) | (0b100 << 3)); // USART16SEL = csi_ker_ck
    #[cfg(path = "usart-othersel")]
    write_volatile(RCC_D2CCIP2R, (read_volatile(RCC_D2CCIP2R) & !0b111) | 0b100); // USART234578SEL = csi_ker_ck
    #[cfg(path = "usart-lost-en")]
    {
        usart_start(USART3, RCC_APB1LENR, 18);
        write_volatile(RCC_APB1LENR, read_volatile(RCC_APB1LENR) & !(1 << 18)); // USART3EN = 0 under UE = 1
    }
    #[cfg(path = "hse-on")]
    hse_on();
    #[cfg(path = "adc")]
    for &(off, v) in [(0x0D8u32, 0x23u32), (0x0E0, 0x0120_07FF), (0x100, 0x1E03_C003), (0x108, 0x3028_07FF), (0x110, 0xE8FD_CBFF),
        (0x118, 0x31D7_3032)].iter()
    {
        write_volatile((RCC + off as usize) as *mut u32, v);
    }
    #[cfg(path = "rtcen")]
    {
        write_volatile(READ0, read_volatile(RCC_BDCR));
        write_volatile(RCC_BDCR, read_volatile(RCC_BDCR)); // set_rtc_backup's `RCC->BDCR |= STM32_RTCSEL` (NOCLK: 0)
        write_volatile(RTC_BKP1R, 0xA5A5_0001);
        write_volatile(READ1, read_volatile(RTC_BKP1R));
        write_volatile(RCC_BDCR, read_volatile(RCC_BDCR) | (1 << 15)); // RTCEN
        write_volatile(READ2, read_volatile(RCC_BDCR));
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
    #[cfg(path = "alias")]
    write_volatile(RCC_C1_APB2ENR, read_volatile(RCC_C1_APB2ENR) | SPI1EN);
    #[cfg(any(path = "unenabled", path = "lost-pll", path = "lost-sel", path = "lost-en", path = "stopped", path = "cfg2same", path = "divr",
        path = "lost-osc", path = "lost-ckper", path = "ckper-live", path = "blocked"))]
    pll1_on();
    #[cfg(any(path = "lost-osc", path = "ckper-live"))]
    hse_on();
    #[cfg(path = "lost-osc")]
    ckpersel(0b10);
    #[cfg(any(path = "lost-osc", path = "lost-ckper", path = "ckper-live", path = "blocked"))]
    spi123sel(0b100); // per_ck: both inputs run, so the mux switches
    #[cfg(path = "sel5")]
    spi123sel(0b101);
    #[cfg(path = "divr")]
    write_volatile(RCC_PLL1DIVR, (1 << 24) | (4 << 16) | (1 << 9) | 399);
    #[cfg(path = "unenabled")]
    spi1_start(); // SPI1EN never set: the first SPI1 access refuses
    // Every path but those that end at their own access enables SPI1 and starts it.
    #[cfg(not(any(path = "ardupilot", path = "pllref", path = "divm0", path = "sw", path = "width", path = "read", path = "usart2",
        path = "unenabled", path = "sel5", path = "divr", path = "usart-lost", path = "usart-stopped", path = "usart-lost-en",
        path = "usart-othersel", path = "hse-on", path = "adc", path = "rtcen")))]
    {
        #[cfg(not(path = "alias"))]
        write_volatile(RCC_APB2ENR, read_volatile(RCC_APB2ENR) | SPI1EN);
        let _ = read_volatile(RCC_APB2ENR); // RM0433 p372 b): read back the enable before using the peripheral
        spi1_start();
    }
    // After the start, with SPI1 running (SPE = 1).
    #[cfg(path = "lost-pll")]
    write_volatile(RCC_CR, read_volatile(RCC_CR) & !CR_PLL1ON);
    #[cfg(path = "lost-sel")]
    spi123sel(0b001);
    #[cfg(path = "lost-en")]
    write_volatile(RCC_APB2ENR, read_volatile(RCC_APB2ENR) & !SPI1EN);
    #[cfg(path = "lost-osc")]
    write_volatile(RCC_CR, read_volatile(RCC_CR) & !CR_HSEON);
    #[cfg(path = "lost-ckper")]
    ckpersel(0b01);
    #[cfg(path = "stopped")]
    {
        write_volatile(SPI1_CR1, read_volatile(SPI1_CR1) & !1); // SPE = 0
        spi123sel(0b001);
    }
    #[cfg(path = "ckper-live")]
    ckpersel(0b10);
    #[cfg(path = "blocked")]
    write_volatile(RCC_CR, read_volatile(RCC_CR) & !CR_HSION);
    #[cfg(path = "cfg2same")]
    write_volatile(SPI1_CFG2, read_volatile(SPI1_CFG2));
    write_volatile(MARK_AFTER, 0xAF7E_0002);
    loop {}
}

#[panic_handler]
fn panic(_: &core::panic::PanicInfo) -> ! {
    loop {}
}
