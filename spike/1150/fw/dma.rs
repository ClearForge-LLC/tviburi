//! TVI-WO-1150 (vi) spike firmware: a DMA write to a guarded register. The CPU sets SPI1 CR1.IOLOCK (the guard
//! latches it), then DMA1 stream 0 copies one word, memory to memory, from DTCM into SPI1 CFG2, which the latch
//! refuses (TVI-SPI-IOLOCK). The record's `pc` then shows who the guard thinks wrote.
//! RM0433 Rev 9: DMA1 at 0x4002_0000 (Table 8); DMA_SxCR 0x010 (+0x18x), DIR[7:6] = 10 memory-to-memory,
//! PSIZE[12:11] = MSIZE[14:13] = 10 word, EN bit 0 (§15.5.5 p662-664); SxNDTR 0x014, SxPAR 0x018 (the source in
//! memory-to-memory), SxM0AR 0x01C (the destination) (§15.5.6-15.5.8 p665). RCC AHB1ENR.DMA1EN (0x5802_44D8 bit 0).
#![no_std]
#![no_main]
use core::ptr::write_volatile;

#[no_mangle]
pub extern "C" fn reset() -> ! {
    unsafe {
        write_volatile(0x5802_44D8 as *mut u32, 1 << 0); // RCC_AHB1ENR.DMA1EN
        write_volatile(0x5802_44F0 as *mut u32, 1 << 12); // RCC_APB2ENR.SPI1EN
        write_volatile(0x4001_3000 as *mut u32, 1 << 16); // SPI1_CR1.IOLOCK: the guard latches
        write_volatile(0x2000_0200 as *mut u32, (1 << 22) | (1 << 26)); // the word to copy: CFG2 MASTER | SSM
        write_volatile(0x4002_0010 as *mut u32, 0); // DMA1_S0CR: disabled
        write_volatile(0x4002_0018 as *mut u32, 0x2000_0200); // DMA1_S0PAR: source
        write_volatile(0x4002_001C as *mut u32, 0x4001_300C); // DMA1_S0M0AR: destination, SPI1_CFG2
        write_volatile(0x4002_0014 as *mut u32, 1); // DMA1_S0NDTR: one item
        write_volatile(0x4002_0010 as *mut u32, (2 << 6) | (2 << 11) | (2 << 13) | 1); // DMA1_S0CR: M2M, words, EN
        write_volatile(0x2000_0104 as *mut u32, 0xAF7E_D3A0);
    }
    loop {
        core::hint::spin_loop();
    }
}

#[panic_handler]
fn panic(_: &core::panic::PanicInfo) -> ! {
    loop {}
}
