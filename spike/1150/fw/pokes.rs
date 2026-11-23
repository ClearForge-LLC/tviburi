//! TVI-WO-1150 spike firmware: CPU writes to TIM1 and RCC, for (i). Built with rustc alone against the
//! repository's firmware/link.x. Addresses: RM0433 Rev 9 Table 8 (TIM1 0x4001_0000, RCC 0x5802_4400);
//! offsets: TIM1 CR1 0x00, DIER 0x0C, PSC 0x28, ARR 0x2C (§38.4); RCC D2CCIP1R 0x50, APB2ENR 0xF0 (§8.7).
#![no_std]
#![no_main]
use core::ptr::write_volatile;

#[no_mangle]
pub extern "C" fn reset() -> ! {
    unsafe {
        write_volatile(0x5802_44F0 as *mut u32, 1 << 0); // RCC_APB2ENR.TIM1EN
        write_volatile(0x5802_4450 as *mut u32, 1 << 12); // RCC_D2CCIP1R.SPI123SEL = 001 (pll2_p_ck)
        write_volatile(0x4001_0028 as *mut u32, 0); // TIM1_PSC
        write_volatile(0x4001_002C as *mut u32, 99); // TIM1_ARR
        write_volatile(0x4001_000C as *mut u32, 1); // TIM1_DIER.UIE
        write_volatile(0x4001_0000 as *mut u32, 1); // TIM1_CR1.CEN
        write_volatile(0x2000_0104 as *mut u32, 0xAF7E_1150);
    }
    loop {
        core::hint::spin_loop();
    }
}

#[panic_handler]
fn panic(_: &core::panic::PanicInfo) -> ! {
    loop {}
}
