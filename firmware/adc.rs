//! Bare-metal probes for the ADC guard (TVI-WO-1307). No dependencies; built with rustc alone, as i2c.rs. Variant chosen at
//! build time with `--cfg path="..."`. Registers: RM0433 Rev 9 §25.6-25.7 (pp. 998-1046); pages and rulings in tests/1307/adc.sh.
//! The board is main's Nucleo with ADC1 channel 10 declared at 1.2 V (`levels: "10:1.2"`, a test value; tests/1307/adc.sh).
//! Unless a path says otherwise ADC1 (0x4002_2000) runs on CKMODE = 11 (adc_sclk/4: the 64 MHz HSI /4, and rev V's /2: 8 MHz,
//! so BOOST = 01, p1009), out of deep-power-down, its regulator started (a spin longer than t_ADCVREG_STUP), enabled with
//! ADRDY polled, and converts channel 10 (PCSEL, SMP10 = 110: 387.5 + 8.5 = 396 cycles, 49.5 us at 16 bits).
//!   path="start"       - the enable with a calibration (ADCALLIN, ADCAL polled), one conversion: passes. MARK_READ = DR
//!                        (0x5D17: 1.2 V of 3.3 V at 16 bits, round(0.3636 x 65535) = 23831), MARK_READ2 = ISR (EOS, EOSMP, ADRDY,
//!                        LDORDY: 0x100B; EOC cleared by the DR read).
//!   path="res12"       - the same at RES = 110 (12 bits, rev V): passes. MARK_READ = DR (0x5D1: round(0.3636 x 4095) = 1489).
//!   path="kernel"      - CKMODE = 00 on ADCSEL's reset source, pll2_p_ck, PLL2 started from its reset values (HSI / 32 x 129 / 2 =
//!                        129 MHz; PRESC /4 and rev V's /2: 16.125 MHz, BOOST = 10), enabled; PLL2 stopped; ADSTART: TVI-ADC-KERNEL,
//!                        predicted-fault (p920, p419). (A mux leaves pll2_p_ck only while both inputs run, p371: so PLL2.)
//!   path="clocklost"   - the same clock, continuous conversions started; PLL2 stopped under them: TVI-RCC-CLOCK at the RCC write,
//!                        naming ADSTART (the RCC guard watches the ADC, granted Q3).
//!   path="ckmode"      - enabled, then CCR.CKMODE changed with ADEN = 1: TVI-ADC-STATE, unmodeled (p1039, p934).
//!   path="deeppwd"     - ADVREGEN set with DEEPPWD = 1 (written 1 in the same word): TVI-ADC-STATE (p926, p933).
//!   path="vreg"        - ADVREGEN, then ADEN at once (no start-up time): TVI-ADC-STATE (p933; t_ADCVREG_STUP, DS12117).
//!   path="calen"       - enabled, then ADCAL with ADEN = 1: TVI-ADC-STATE (p1003).
//!   path="cfgr"        - continuous conversions started, then CFGR.OVRMOD written: TVI-ADC-STATE (p934).
//!   path="undeclared"  - one conversion of channel 11 (no level declared), DR read: TVI-ADC-LEVEL, unmodeled (ruled Q10).
//!   path="early"       - ADSTART, then DR read before EOC: passes; MARK_READ = that DR (0, its reset value: no conversion yet),
//!                        MARK_READ2 = DR after EOC (0x5D17).
//!   path="res"         - RES = 100 (rev V reserves it, p1016), ADSTART: TVI-ADC-RATE, unmodeled.
//!   path="boost"       - BOOST = 00 at 8 MHz (p1009 asks 01), ADSTART: TVI-ADC-RATE, unmodeled.
//!   path="boostack"    - the kernel clock above (16.125 MHz, where p1009 asks BOOST = 10) with ArduPilot's 01: passes, acknowledged
//!                        by name (ruled Q7). MARK_READ = DR (0x5D17).
//!   path="pcsel"       - channel 10 sequenced with PCSEL = 0, ADSTART: TVI-ADC-PCSEL, unmodeled (p1021).
//!   path="field"       - CFGR.EXTEN = 01 (a hardware trigger): TVI-ADC-FIELD, unmodeled (the allow-list, ruled Q12).
//!   path="width"       - a byte write to CR: TVI-ADC-WIDTH, unmodeled (p926).
//!   path="half"        - a half-word read of DR with DMNGT = 00 (not ArduPilot's DMA): TVI-ADC-WIDTH, unmodeled (ruled Q14).
//!   path="read"        - a read at 0x100 (ADC2's window): TVI-ADC-READ, unmodeled.
//!   path="dfsdm"       - DMNGT = 10, ADSTART: TVI-ADC-DMNGT, unmodeled (no DFSDM model).
//!   path="oneshotnone" - DMNGT = 01 with no DMA stream selecting request 9, ADSTART: TVI-ADC-DMNGT, unmodeled (ruled Q8).
//!   path="oneshot"     - DMNGT = 01, continuous, DMA1 stream 0 P2M on request 9 by words, NDT = 4, CIRC = 0: the stream takes 4 conversions and
//!                        ends (EN cleared, 1303a Q4); the ADC raises no more requests and, its DR frozen, never overruns (p964-965):
//!                        passes. MARK_READ = ISR after 2 ms (OVR, bit 4, clear), MARK_READ2 = S0NDTR (0).
//!   path="ccrack"      - ADC3 enabled, then its CCR's VREFEN, TSEN, VBATEN written one by one with ADEN = 1 (ArduPilot's order):
//!                        passes, acknowledged by name (ruled Q6). MARK_READ = ADC3 CCR (0x01C30000).
//!   path="ccrother"    - ADC3 enabled, then its CCR.PRESC changed with ADEN = 1: TVI-ADC-STATE, unmodeled (p1039).
#![no_std]
#![no_main]
#![allow(dead_code)]

use core::ptr::{read_volatile, write_volatile};

const RCC_CR: usize = 0x5802_4400;
const RCC_AHB1ENR: usize = 0x5802_44D8;
const RCC_AHB4ENR: usize = 0x5802_44E0;
const RCC_D1CCIPR: usize = 0x5802_444C;
const RCC_D3CCIPR: usize = 0x5802_4458;
const ADC1: usize = 0x4002_2000;
const ADC3: usize = 0x5802_6000;
const DMA1: usize = 0x4002_0000;
const DMAMUX1: usize = 0x4002_0800;
const SRAM4: usize = 0x3800_1900;
const ISR: usize = 0x00;
const CR: usize = 0x08;
const CFGR: usize = 0x0C;
const SMPR2: usize = 0x18;
const PCSEL: usize = 0x1C;
const SQR1: usize = 0x30;
const DR: usize = 0x40;
const CCR: usize = 0x308;
const MARK_AFTER: *mut u32 = 0x2000_0104 as *mut u32;
const MARK_READ: *mut u32 = 0x2000_0108 as *mut u32;
const MARK_READ2: *mut u32 = 0x2000_010C as *mut u32;
const ADVREGEN: u32 = 1 << 28;
const ADEN: u32 = 1 << 0;
const ADSTART: u32 = 1 << 2;
const ADCAL: u32 = 1 << 31;
const ADCALLIN: u32 = 1 << 16;
const EOC: u32 = 1 << 2;
const CONT: u32 = 1 << 13;
const CKMODE_SCLK4: u32 = 3 << 16;
const BOOST01: u32 = 1 << 8;
const BOOST10: u32 = 2 << 8;
const PRESC4: u32 = 2 << 18;

unsafe fn w(a: usize, v: u32) {
    write_volatile(a as *mut u32, v);
}

unsafe fn r(a: usize) -> u32 {
    read_volatile(a as *const u32)
}

/// Lets virtual time pass: longer than t_ADCVREG_STUP (10 us, DS12117).
unsafe fn spin() {
    let mut i = 0u32;
    while i < 3000 {
        write_volatile(MARK_READ2, read_volatile(MARK_READ2));
        i += 1;
    }
}

/// Out of deep-power-down, the regulator and its start-up time, an optional calibration, ADEN and ADRDY (p926-933).
unsafe fn enable(adc: usize, ccr: u32, boost: u32, calibrate: bool) {
    w(adc + CCR, ccr);
    w(adc + CR, 0);
    w(adc + CR, ADVREGEN | boost);
    spin();
    if calibrate {
        w(adc + CR, ADVREGEN | boost | ADCALLIN);
        w(adc + CR, ADVREGEN | boost | ADCALLIN | ADCAL);
        while r(adc + CR) & ADCAL != 0 {}
    }
    w(adc + ISR, 1);
    w(adc + CR, r(adc + CR) | ADEN);
    while r(adc + ISR) & 1 == 0 {}
}

/// Channel 10 (or `ch`) preselected, sampled 387.5 cycles, alone in the sequence; CFGR = `cfgr`.
unsafe fn setup(adc: usize, ch: u32, cfgr: u32) {
    w(adc + PCSEL, 1 << ch);
    w(adc + SMPR2, 6 << (3 * (ch - 10)));
    w(adc + SQR1, ch << 6);
    w(adc + CFGR, cfgr);
}

unsafe fn start(adc: usize) {
    w(adc + CR, r(adc + CR) | ADSTART);
}

unsafe fn convert(adc: usize) -> u32 {
    start(adc);
    while r(adc + ISR) & EOC == 0 {}
    r(adc + DR)
}

/// PLL2 from its reset values (RCC_CR.PLL2ON bit 26, PLL2RDY bit 27, p383): pll2_p_ck, ADCSEL's reset source (p419), runs.
unsafe fn pll2() {
    w(RCC_CR, r(RCC_CR) | 1 << 26);
    while r(RCC_CR) & 1 << 27 == 0 {}
}

unsafe fn pll2_off() {
    w(RCC_CR, r(RCC_CR) & !(1 << 26));
}

#[no_mangle]
pub unsafe extern "C" fn reset() -> ! {
    w(RCC_AHB1ENR, 0x23); // DMA1EN, DMA2EN, ADC12EN (p455)
    w(RCC_AHB4ENR, 1 << 24); // ADC3EN (p458)
    #[cfg(path = "start")]
    {
        enable(ADC1, CKMODE_SCLK4, BOOST01, true);
        setup(ADC1, 10, 0);
        write_volatile(MARK_READ, convert(ADC1));
        write_volatile(MARK_READ2, r(ADC1 + ISR));
    }
    #[cfg(path = "res12")]
    {
        enable(ADC1, CKMODE_SCLK4, BOOST01, false);
        setup(ADC1, 10, 6 << 2);
        write_volatile(MARK_READ, convert(ADC1));
    }
    #[cfg(path = "kernel")]
    {
        pll2();
        enable(ADC1, PRESC4, BOOST10, false);
        setup(ADC1, 10, 0);
        pll2_off();
        start(ADC1);
    }
    #[cfg(path = "clocklost")]
    {
        pll2();
        enable(ADC1, PRESC4, BOOST10, false);
        setup(ADC1, 10, CONT);
        start(ADC1);
        pll2_off();
    }
    #[cfg(path = "ckmode")]
    {
        enable(ADC1, CKMODE_SCLK4, BOOST01, false);
        w(ADC1 + CCR, 2 << 16);
    }
    #[cfg(path = "deeppwd")]
    {
        w(ADC1 + CCR, CKMODE_SCLK4);
        w(ADC1 + CR, (1 << 29) | ADVREGEN | BOOST01);
    }
    #[cfg(path = "vreg")]
    {
        w(ADC1 + CCR, CKMODE_SCLK4);
        w(ADC1 + CR, 0);
        w(ADC1 + CR, ADVREGEN | BOOST01);
        w(ADC1 + CR, ADVREGEN | BOOST01 | ADEN);
    }
    #[cfg(path = "calen")]
    {
        enable(ADC1, CKMODE_SCLK4, BOOST01, false);
        w(ADC1 + CR, ADVREGEN | BOOST01 | ADEN | ADCAL);
    }
    #[cfg(path = "cfgr")]
    {
        enable(ADC1, CKMODE_SCLK4, BOOST01, false);
        setup(ADC1, 10, CONT);
        start(ADC1);
        w(ADC1 + CFGR, CONT | (1 << 12));
    }
    #[cfg(path = "undeclared")]
    {
        enable(ADC1, CKMODE_SCLK4, BOOST01, false);
        setup(ADC1, 11, 0);
        write_volatile(MARK_READ, convert(ADC1));
    }
    #[cfg(path = "early")]
    {
        enable(ADC1, CKMODE_SCLK4, BOOST01, false);
        setup(ADC1, 10, 0);
        start(ADC1);
        write_volatile(MARK_READ, r(ADC1 + DR));
        while r(ADC1 + ISR) & EOC == 0 {}
        write_volatile(MARK_READ2, r(ADC1 + DR));
    }
    #[cfg(path = "res")]
    {
        enable(ADC1, CKMODE_SCLK4, BOOST01, false);
        setup(ADC1, 10, 4 << 2);
        start(ADC1);
    }
    #[cfg(path = "boost")]
    {
        enable(ADC1, CKMODE_SCLK4, 0, false);
        setup(ADC1, 10, 0);
        start(ADC1);
    }
    #[cfg(path = "boostack")]
    {
        pll2();
        enable(ADC1, PRESC4, BOOST01, false);
        setup(ADC1, 10, 0);
        write_volatile(MARK_READ, convert(ADC1));
    }
    #[cfg(path = "pcsel")]
    {
        enable(ADC1, CKMODE_SCLK4, BOOST01, false);
        setup(ADC1, 10, 0);
        w(ADC1 + PCSEL, 0);
        start(ADC1);
    }
    #[cfg(path = "field")]
    {
        enable(ADC1, CKMODE_SCLK4, BOOST01, false);
        w(ADC1 + CFGR, 1 << 10);
    }
    #[cfg(path = "width")]
    {
        write_volatile((ADC1 + CR) as *mut u8, 0);
    }
    #[cfg(path = "half")]
    {
        enable(ADC1, CKMODE_SCLK4, BOOST01, false);
        let _ = read_volatile((ADC1 + DR) as *const u16);
    }
    #[cfg(path = "read")]
    {
        let _ = r(ADC1 + 0x100);
    }
    #[cfg(path = "dfsdm")]
    {
        enable(ADC1, CKMODE_SCLK4, BOOST01, false);
        setup(ADC1, 10, 2);
        start(ADC1);
    }
    #[cfg(path = "oneshotnone")]
    {
        enable(ADC1, CKMODE_SCLK4, BOOST01, false);
        setup(ADC1, 10, CONT | 1);
        start(ADC1);
    }
    #[cfg(path = "oneshot")]
    {
        enable(ADC1, CKMODE_SCLK4, BOOST01, false);
        setup(ADC1, 10, CONT | 1);
        w(DMAMUX1, 9); // channel 0 (DMA1 stream 0): adc1_dma (p697)
        w(DMA1 + 0x10 + 0x14, 0x21); // S0FCR
        w(DMA1 + 0x10 + 0x08, (ADC1 + DR) as u32); // S0PAR
        w(DMA1 + 0x10 + 0x0C, SRAM4 as u32); // S0M0AR
        w(DMA1 + 0x10 + 0x04, 4); // S0NDTR
        w(DMA1 + 0x10, 0x5400); // S0CR: P2M, MINC, 32-bit (words: the half-word read is ArduPilot's DMNGT = 11 only, Q14)
        w(DMA1 + 0x10, 0x5401); // EN
        start(ADC1);
        let mut i = 0;
        while i < 20 {
            spin();
            i += 1;
        }
        write_volatile(MARK_READ, r(ADC1 + ISR));
        write_volatile(MARK_READ2, r(DMA1 + 0x10 + 0x04));
    }
    #[cfg(path = "ccrack")]
    {
        enable(ADC3, CKMODE_SCLK4, BOOST01, false);
        w(ADC3 + CCR, r(ADC3 + CCR) | 1 << 22);
        w(ADC3 + CCR, r(ADC3 + CCR) | 1 << 23);
        w(ADC3 + CCR, r(ADC3 + CCR) | 1 << 24);
        write_volatile(MARK_READ, r(ADC3 + CCR));
    }
    #[cfg(path = "ccrother")]
    {
        enable(ADC3, CKMODE_SCLK4, BOOST01, false);
        w(ADC3 + CCR, CKMODE_SCLK4 | 1 << 18);
    }
    write_volatile(MARK_AFTER, 0xAF7E_0002);
    loop {}
}

#[panic_handler]
fn panic(_: &core::panic::PanicInfo) -> ! {
    loop {}
}
