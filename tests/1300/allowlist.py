#!/usr/bin/env python3
"""TVI-WO-1300 / 1300a (tests/1300; host Python, run as `python3 -I tests/1300/allowlist.py check|write` from the repo root).

The RCC allow-list's default classification. Every RM0433 Rev 9 field (data/rm/fields.tsv) gets a class and an allowed set
from its RM access type and the stock model's kind (data/stock/fields.tsv), with the guard's own fields overriding.
From that it generates the two committed outputs: the `Fields` rows of model/GuardedSTM32H7_RCC.cs (between the exact
marker lines FIELDS_OPEN and FIELDS_CLOSE) and tests/1300/fields.resc.

  check   regenerate both in memory and compare byte for byte with the committed files: exit 0 if identical, else 1 + a diff
  write   rewrite both in place (the model file only between the markers)
"""
import collections, difflib, os, re, sys
# TVI-WO-1206 readings BEGIN (spike/1206/apply.py writes this block; spike/1206/readings.py derives it)
READ_1206 = {
    ('RCC_AHB1ENR', 'ADC12EN'): ('acknowledged by name', "the guard's copy; the stock bus enable (computed-read): the bus lock only. With ADCx_CCR.CKMODE = 00 the ADC's kernel clock is the one ADCSEL selects (p920, p419), answered by the query; the ADC guard asks it at ADSTART and registers as watched (TVI-WO-1307 Q3) (TVI-WO-1300b, ruled 2026-11-25T03:03Z Q1); read for 0x0,0x1: a 0 gates the peripheral's clock and its registers then ignore writes and read invalid data (p373), a 1 clocks it; the stock RCC sets the peripheral's bus enable from this bit, so a 0 drops its accesses and reads 0, and the bit reads back from the guard's copy (TVI-WO-1206 A1; p373, p455; STM32H7_RCC.cs L736; STM32H7_RCC.cs L737)"),
    ('RCC_AHB1ENR', 'DMA1EN'): ('acknowledged by name', "the guard's copy; the stock bus enable (computed-read); read for 0x0,0x1: a 0 gates the peripheral's clock and its registers then ignore writes and read invalid data (p373), a 1 clocks it; the stock RCC sets the peripheral's bus enable from this bit, so a 0 drops its accesses and reads 0, and the bit reads back from the guard's copy (TVI-WO-1206 A1; p373, p455; STM32H7_RCC.cs L729; STM32H7_RCC.cs L730)"),
    ('RCC_AHB1ENR', 'DMA2EN'): ('acknowledged by name', "the guard's copy; the stock bus enable (computed-read); read for 0x0,0x1: a 0 gates the peripheral's clock and its registers then ignore writes and read invalid data (p373), a 1 clocks it; the stock RCC sets the peripheral's bus enable from this bit, so a 0 drops its accesses and reads 0, and the bit reads back from the guard's copy (TVI-WO-1206 A1; p373, p455; STM32H7_RCC.cs L732; STM32H7_RCC.cs L733)"),
    ('RCC_AHB1ENR', 'ETH1MACEN'): ('acknowledged by name', "the guard's copy; the stock bus enable (computed-read); read for 0x0: a 0 gates the peripheral's clock and its registers then ignore writes and read invalid data (p373), a 1 clocks it; the stock RCC sets the peripheral's bus enable from this bit, so a 0 drops its accesses and reads 0, and the bit reads back from the guard's copy (TVI-WO-1206 A1; p373, p455; STM32H7_RCC.cs L740; STM32H7_RCC.cs L741)"),
    ('RCC_AHB1ENR', 'ETH1RXEN'): ('acknowledged by name', "the guard's copy; the stock bus enable (computed-read); read for 0x0: written 0 only: the bit is stored and reads back from the guard's copy as silicon's does, and the stock RCC registers no peripheral at it, so it gates nothing; no firmware accesses a peripheral behind it (ethernet, the one present, has no access in any run: spike/1206/data/main-8e15435/instance-accesses.tsv: the armed run, the 14 generated boots and the fixture) (TVI-WO-1206 A2; p373, p455; STM32H7_RCC.cs L746; STM32H7_RCC.cs L747)"),
    ('RCC_AHB1ENR', 'ETH1TXEN'): ('acknowledged by name', "the guard's copy; the stock bus enable (computed-read); read for 0x0: written 0 only: the bit is stored and reads back from the guard's copy as silicon's does, and the stock RCC registers no peripheral at it, so it gates nothing; no firmware accesses a peripheral behind it (ethernet, the one present, has no access in any run: spike/1206/data/main-8e15435/instance-accesses.tsv: the armed run, the 14 generated boots and the fixture) (TVI-WO-1206 A2; p373, p455; STM32H7_RCC.cs L743; STM32H7_RCC.cs L744)"),
    ('RCC_AHB1ENR', 'USB1OTGHSEN'): ("stock, wrong (a 0 leaves the twin's OTG_HS core reachable, where silicon ignores its writes and reads invalid data (p373); unaffected: no firmware accesses usb1: no read or write in any run (spike/1206/data/main-8e15435/instance-accesses.tsv: the armed run, the 14 generated boots and the fixture))", 'disabled: a kernel-clocked peripheral the query does not cover (TVI-WO-1300 (4)); read for 0x0 (TVI-WO-1206; p373, p454; STM32H7_RCC.cs L750; STM32H7_RCC.cs L751)'),
    ('RCC_AHB1ENR', 'USB1OTGHSULPIEN'): ('acknowledged by name', "the guard's copy; the stock bus enable (computed-read); read for 0x0: written 0 only: the bit is stored and reads back from the guard's copy as silicon's does, and the stock RCC registers no peripheral at it, so it gates nothing; no firmware accesses a peripheral behind it (ethernet, the one present, has no access in any run: spike/1206/data/main-8e15435/instance-accesses.tsv: the armed run, the 14 generated boots and the fixture) (TVI-WO-1206 A2; p373, p454; STM32H7_RCC.cs L753; STM32H7_RCC.cs L754)"),
    ('RCC_AHB1ENR', 'USB2OTGHSEN'): ("stock, wrong (a 0 leaves the twin's OTG core reachable, where silicon ignores its writes and reads invalid data (p373); unaffected: no firmware accesses usb2: no read or write in any run (spike/1206/data/main-8e15435/instance-accesses.tsv: the armed run, the 14 generated boots and the fixture))", 'disabled: a kernel-clocked peripheral the query does not cover (TVI-WO-1300 (4)); read for 0x0 (TVI-WO-1206; p373, p454; STM32H7_RCC.cs L756; STM32H7_RCC.cs L757)'),
    ('RCC_AHB1ENR', 'USB2OTGHSULPIEN'): ('acknowledged by name', "the guard's copy; the stock bus enable (computed-read); read for 0x0: written 0 only: the bit is stored and reads back from the guard's copy as silicon's does, and the stock RCC registers no peripheral at it, so it gates nothing; no firmware accesses a peripheral behind it (ethernet, the one present, has no access in any run: spike/1206/data/main-8e15435/instance-accesses.tsv: the armed run, the 14 generated boots and the fixture) (TVI-WO-1206 A2; p373, p454; STM32H7_RCC.cs L759; STM32H7_RCC.cs L760)"),
    ('RCC_AHB2ENR', 'CRYPTEN'): ("stock, wrong (a 0 leaves the twin's CRYP core reachable, where silicon ignores its writes and reads invalid data (p373); unaffected: no firmware accesses crypto: no read or write in any run (spike/1206/data/main-8e15435/instance-accesses.tsv: the armed run, the 14 generated boots and the fixture))", "the guard's copy; the stock bus enable (computed-read); read for 0x0 (TVI-WO-1206; p373, p457; STM32H7_RCC.cs L768; STM32H7_RCC.cs L769)"),
    ('RCC_AHB2ENR', 'DCMIEN'): ('acknowledged by name', "the guard's copy; the stock bus enable (computed-read); read for 0x0: written 0 only: the bit is stored and reads back from the guard's copy as silicon's does, and the stock RCC registers no peripheral at it, so it gates nothing; no firmware accesses a peripheral behind it (ethernet, the one present, has no access in any run: spike/1206/data/main-8e15435/instance-accesses.tsv: the armed run, the 14 generated boots and the fixture) (TVI-WO-1206 A2; p373, p457; STM32H7_RCC.cs L764; STM32H7_RCC.cs L765)"),
    ('RCC_AHB2ENR', 'HASHEN'): ('acknowledged by name', "the guard's copy; the stock bus enable (computed-read); read for 0x0: written 0 only: the bit is stored and reads back from the guard's copy as silicon's does, and the stock RCC registers no peripheral at it, so it gates nothing; no firmware accesses a peripheral behind it (ethernet, the one present, has no access in any run: spike/1206/data/main-8e15435/instance-accesses.tsv: the armed run, the 14 generated boots and the fixture) (TVI-WO-1206 A2; p373, p457; STM32H7_RCC.cs L771; STM32H7_RCC.cs L772)"),
    ('RCC_AHB2ENR', 'SDMMC2EN'): ('acknowledged by name', "disabled: a kernel-clocked peripheral the query does not cover (TVI-WO-1300 (4)); read for 0x0: written 0 only: the bit is stored and reads back from the guard's copy as silicon's does, and the stock RCC registers no peripheral at it, so it gates nothing; no firmware accesses a peripheral behind it (ethernet, the one present, has no access in any run: spike/1206/data/main-8e15435/instance-accesses.tsv: the armed run, the 14 generated boots and the fixture) (TVI-WO-1206 A2; p373, p456; STM32H7_RCC.cs L778; STM32H7_RCC.cs L779)"),
    ('RCC_AHB2ENR', 'SRAM1EN'): ("stock, wrong (the twin never gates SRAM1, where silicon needs the bit set before the CPU uses it (p369, p456); unaffected: AHB2ENR's first write (0x20000000, at 4.06 us) sets it, and every later one only sets bits, so it is 0 only from reset to its enable; the access log covers peripherals, not memory, so that window is not measured (a row))", "the guard's copy; the stock bus enable (computed-read); read for 0x1 (TVI-WO-1206; p373, p456; STM32H7_RCC.cs L782; STM32H7_RCC.cs L783)"),
    ('RCC_AHB2ENR', 'SRAM2EN'): ('stock, wrong (the twin never gates SRAM2, where silicon needs the bit set before the CPU uses it (p369, p456); unaffected: every AHB2ENR write only sets bits (0x20000000, 0x60000000, 0xE0000000, 0xE0000040, the first at 4.06 us), so the bit is 0 only from reset to its enable; the access log covers peripherals, not memory, so that window is not measured (a row))', "the guard's copy; the stock bus enable (computed-read); read for 0x0,0x1 (TVI-WO-1206; p373, p456; STM32H7_RCC.cs L785; STM32H7_RCC.cs L786)"),
    ('RCC_AHB2ENR', 'SRAM3EN'): ('stock, wrong (the twin never gates SRAM3, where silicon needs the bit set before the CPU uses it (p369, p456); unaffected: as SRAM2EN: 0 only from reset to its enable; that window is not measured (a row))', "the guard's copy; the stock bus enable (computed-read); read for 0x0,0x1 (TVI-WO-1206; p373, p456; STM32H7_RCC.cs L788; STM32H7_RCC.cs L789)"),
    ('RCC_AHB4ENR', 'ADC3EN'): ('acknowledged by name', "the guard's copy; the stock bus enable (computed-read): the bus lock only. With ADCx_CCR.CKMODE = 00 the ADC's kernel clock is the one ADCSEL selects (p920, p419), answered by the query; the ADC guard asks it at ADSTART and registers as watched (TVI-WO-1307 Q3) (TVI-WO-1300b, ruled 2026-11-25T03:03Z Q1); read for 0x0,0x1: a 0 gates the peripheral's clock and its registers then ignore writes and read invalid data (p373), a 1 clocks it; the stock RCC sets the peripheral's bus enable from this bit, so a 0 drops its accesses and reads 0, and the bit reads back from the guard's copy (TVI-WO-1206 A1; p373, p458; STM32H7_RCC.cs L834; STM32H7_RCC.cs L835)"),
    ('RCC_AHB4ENR', 'GPIOCEN'): ('acknowledged by name', "the guard's copy; the stock bus enable (computed-read); read for 0x0,0x1: a 0 gates the peripheral's clock and its registers then ignore writes and read invalid data (p373), a 1 clocks it; the stock RCC sets the peripheral's bus enable from this bit, so a 0 drops its accesses and reads 0, and the bit reads back from the guard's copy (TVI-WO-1206 A1; p373, p460; STM32H7_RCC.cs L798; STM32H7_RCC.cs L799)"),
    ('RCC_AHB4ENR', 'GPIODEN'): ('acknowledged by name', "the guard's copy; the stock bus enable (computed-read); read for 0x0,0x1: a 0 gates the peripheral's clock and its registers then ignore writes and read invalid data (p373), a 1 clocks it; the stock RCC sets the peripheral's bus enable from this bit, so a 0 drops its accesses and reads 0, and the bit reads back from the guard's copy (TVI-WO-1206 A1; p373, p459; STM32H7_RCC.cs L801; STM32H7_RCC.cs L802)"),
    ('RCC_AHB4ENR', 'GPIOEEN'): ('acknowledged by name', "the guard's copy; the stock bus enable (computed-read); read for 0x0,0x1: a 0 gates the peripheral's clock and its registers then ignore writes and read invalid data (p373), a 1 clocks it; the stock RCC sets the peripheral's bus enable from this bit, so a 0 drops its accesses and reads 0, and the bit reads back from the guard's copy (TVI-WO-1206 A1; p373, p459; STM32H7_RCC.cs L804; STM32H7_RCC.cs L805)"),
    ('RCC_AHB4ENR', 'GPIOFEN'): ('acknowledged by name', "the guard's copy; the stock bus enable (computed-read); read for 0x0,0x1: a 0 gates the peripheral's clock and its registers then ignore writes and read invalid data (p373), a 1 clocks it; the stock RCC sets the peripheral's bus enable from this bit, so a 0 drops its accesses and reads 0, and the bit reads back from the guard's copy (TVI-WO-1206 A1; p373, p459; STM32H7_RCC.cs L807; STM32H7_RCC.cs L808)"),
    ('RCC_APB1LENR', 'TIM2EN'): ('acknowledged by name', "the guard's copy; the stock bus enable (computed-read); read for 0x0,0x1: a 0 gates the peripheral's clock and its registers then ignore writes and read invalid data (p373), a 1 clocks it; the stock RCC sets the peripheral's bus enable from this bit, so a 0 drops its accesses and reads 0, and the bit reads back from the guard's copy (TVI-WO-1206 A1; p373, p465; STM32H7_RCC.cs L856; STM32H7_RCC.cs L857)"),
    ('RCC_APB2ENR', 'DFSDM1EN'): ('acknowledged by name', "disabled: a kernel-clocked peripheral the query does not cover (TVI-WO-1300 (4)); read for 0x0: written 0 only: the bit is stored and reads back from the guard's copy as silicon's does, and the stock RCC registers no peripheral at it, so it gates nothing; no firmware accesses a peripheral behind it (ethernet, the one present, has no access in any run: spike/1206/data/main-8e15435/instance-accesses.tsv: the armed run, the 14 generated boots and the fixture) (TVI-WO-1206 A2; p373, p468; STM32H7_RCC.cs L997; STM32H7_RCC.cs L998)"),
    ('RCC_APB2ENR', 'HRTIMEN'): ('acknowledged by name', "the guard's copy; the stock bus enable (computed-read); read for 0x0: written 0 only: the bit is stored and reads back from the guard's copy as silicon's does, and the stock RCC registers no peripheral at it, so it gates nothing; no firmware accesses a peripheral behind it (ethernet, the one present, has no access in any run: spike/1206/data/main-8e15435/instance-accesses.tsv: the armed run, the 14 generated boots and the fixture) (TVI-WO-1206 A2; p373, p468; STM32H7_RCC.cs L1000; STM32H7_RCC.cs L1001)"),
    ('RCC_APB2ENR', 'SAI1EN'): ('acknowledged by name', "disabled: a kernel-clocked peripheral the query does not cover (TVI-WO-1300 (4)); read for 0x0: written 0 only: the bit is stored and reads back from the guard's copy as silicon's does, and the stock RCC registers no peripheral at it, so it gates nothing; no firmware accesses a peripheral behind it (ethernet, the one present, has no access in any run: spike/1206/data/main-8e15435/instance-accesses.tsv: the armed run, the 14 generated boots and the fixture) (TVI-WO-1206 A2; p373, p469; STM32H7_RCC.cs L987; STM32H7_RCC.cs L988)"),
    ('RCC_APB2ENR', 'SAI2EN'): ('acknowledged by name', "disabled: a kernel-clocked peripheral the query does not cover (TVI-WO-1300 (4)); read for 0x0: written 0 only: the bit is stored and reads back from the guard's copy as silicon's does, and the stock RCC registers no peripheral at it, so it gates nothing; no firmware accesses a peripheral behind it (ethernet, the one present, has no access in any run: spike/1206/data/main-8e15435/instance-accesses.tsv: the armed run, the 14 generated boots and the fixture) (TVI-WO-1206 A2; p373, p468; STM32H7_RCC.cs L990; STM32H7_RCC.cs L991)"),
    ('RCC_APB2ENR', 'SAI3EN'): ('acknowledged by name', "disabled: a kernel-clocked peripheral the query does not cover (TVI-WO-1300 (4)); read for 0x0: written 0 only: the bit is stored and reads back from the guard's copy as silicon's does, and the stock RCC registers no peripheral at it, so it gates nothing; no firmware accesses a peripheral behind it (ethernet, the one present, has no access in any run: spike/1206/data/main-8e15435/instance-accesses.tsv: the armed run, the 14 generated boots and the fixture) (TVI-WO-1206 A2; p373, p468; STM32H7_RCC.cs L993; STM32H7_RCC.cs L994)"),
    ('RCC_APB2ENR', 'SPI4EN'): ('acknowledged by name', "disabled: a kernel-clocked peripheral the query does not cover (TVI-WO-1300 (4)); read for 0x0: a 0 gates the peripheral's clock and its registers then ignore writes and read invalid data (p373), a 1 clocks it; the stock RCC sets the peripheral's bus enable from this bit, so a 0 drops its accesses and reads 0, and the bit reads back from the guard's copy (TVI-WO-1206 A1; p373, p469; STM32H7_RCC.cs L969; STM32H7_RCC.cs L970)"),
    ('RCC_APB2ENR', 'SPI5EN'): ('acknowledged by name', "disabled: a kernel-clocked peripheral the query does not cover (TVI-WO-1300 (4)); read for 0x0: written 0 only: the bit is stored and reads back from the guard's copy as silicon's does, and the stock RCC registers no peripheral at it, so it gates nothing; no firmware accesses a peripheral behind it (ethernet, the one present, has no access in any run: spike/1206/data/main-8e15435/instance-accesses.tsv: the armed run, the 14 generated boots and the fixture) (TVI-WO-1206 A2; p373, p469; STM32H7_RCC.cs L983; STM32H7_RCC.cs L984)"),
    ('RCC_APB2ENR', 'TIM15EN'): ('acknowledged by name', "the guard's copy; the stock bus enable (computed-read); read for 0x0: a 0 gates the peripheral's clock and its registers then ignore writes and read invalid data (p373), a 1 clocks it; the stock RCC sets the peripheral's bus enable from this bit, so a 0 drops its accesses and reads 0, and the bit reads back from the guard's copy (TVI-WO-1206 A1; p373, p469; STM32H7_RCC.cs L973; STM32H7_RCC.cs L974)"),
    ('RCC_APB2ENR', 'TIM16EN'): ('acknowledged by name', "the guard's copy; the stock bus enable (computed-read); read for 0x0: a 0 gates the peripheral's clock and its registers then ignore writes and read invalid data (p373), a 1 clocks it; the stock RCC sets the peripheral's bus enable from this bit, so a 0 drops its accesses and reads 0, and the bit reads back from the guard's copy (TVI-WO-1206 A1; p373, p469; STM32H7_RCC.cs L976; STM32H7_RCC.cs L977)"),
    ('RCC_APB2ENR', 'TIM17EN'): ('acknowledged by name', "the guard's copy; the stock bus enable (computed-read); read for 0x0: a 0 gates the peripheral's clock and its registers then ignore writes and read invalid data (p373), a 1 clocks it; the stock RCC sets the peripheral's bus enable from this bit, so a 0 drops its accesses and reads 0, and the bit reads back from the guard's copy (TVI-WO-1206 A1; p373, p469; STM32H7_RCC.cs L979; STM32H7_RCC.cs L980)"),
    ('RCC_APB2ENR', 'TIM1EN'): ('acknowledged by name', "the guard's copy; the stock bus enable (computed-read); read for 0x0,0x1: a 0 gates the peripheral's clock and its registers then ignore writes and read invalid data (p373), a 1 clocks it; the stock RCC sets the peripheral's bus enable from this bit, so a 0 drops its accesses and reads 0, and the bit reads back from the guard's copy (TVI-WO-1206 A1; p373, p470; STM32H7_RCC.cs L952; STM32H7_RCC.cs L953)"),
    ('RCC_APB2ENR', 'TIM8EN'): ('acknowledged by name', "the guard's copy; the stock bus enable (computed-read); read for 0x0: a 0 gates the peripheral's clock and its registers then ignore writes and read invalid data (p373), a 1 clocks it; the stock RCC sets the peripheral's bus enable from this bit, so a 0 drops its accesses and reads 0, and the bit reads back from the guard's copy (TVI-WO-1206 A1; p373, p470; STM32H7_RCC.cs L955; STM32H7_RCC.cs L956)"),
    ('RCC_APB2ENR', 'USART6EN'): ('acknowledged by name', "disabled: a kernel-clocked peripheral the query does not cover (TVI-WO-1300 (4)); read for 0x0: a 0 gates the peripheral's clock and its registers then ignore writes and read invalid data (p373), a 1 clocks it; the stock RCC sets the peripheral's bus enable from this bit, so a 0 drops its accesses and reads 0, and the bit reads back from the guard's copy (TVI-WO-1206 A1; p373, p469; STM32H7_RCC.cs L962; STM32H7_RCC.cs L963)"),
    ('RCC_APB4ENR', 'COMP12EN'): ('acknowledged by name', "the guard's copy; the stock bus enable (computed-read); read for 0x0: written 0 only: the bit is stored and reads back from the guard's copy as silicon's does, and the stock RCC registers no peripheral at it, so it gates nothing; no firmware accesses a peripheral behind it (ethernet, the one present, has no access in any run: spike/1206/data/main-8e15435/instance-accesses.tsv: the armed run, the 14 generated boots and the fixture) (TVI-WO-1206 A2; p373, p471; STM32H7_RCC.cs L1035; STM32H7_RCC.cs L1036)"),
    ('RCC_APB4ENR', 'I2C4EN'): ('acknowledged by name', "disabled: a kernel-clocked peripheral the query does not cover (TVI-WO-1300 (4)); read for 0x0: a 0 gates the peripheral's clock and its registers then ignore writes and read invalid data (p373), a 1 clocks it; the stock RCC sets the peripheral's bus enable from this bit, so a 0 drops its accesses and reads 0, and the bit reads back from the guard's copy (TVI-WO-1206 A1; p373, p472; STM32H7_RCC.cs L1018; STM32H7_RCC.cs L1019)"),
    ('RCC_APB4ENR', 'LPTIM2EN'): ('acknowledged by name', "disabled: a kernel-clocked peripheral the query does not cover (TVI-WO-1300 (4)); read for 0x0: a 0 gates the peripheral's clock and its registers then ignore writes and read invalid data (p373), a 1 clocks it; the stock RCC sets the peripheral's bus enable from this bit, so a 0 drops its accesses and reads 0, and the bit reads back from the guard's copy (TVI-WO-1206 A1; p373, p472; STM32H7_RCC.cs L1022; STM32H7_RCC.cs L1023)"),
    ('RCC_APB4ENR', 'LPTIM3EN'): ('acknowledged by name', "disabled: a kernel-clocked peripheral the query does not cover (TVI-WO-1300 (4)); read for 0x0: a 0 gates the peripheral's clock and its registers then ignore writes and read invalid data (p373), a 1 clocks it; the stock RCC sets the peripheral's bus enable from this bit, so a 0 drops its accesses and reads 0, and the bit reads back from the guard's copy (TVI-WO-1206 A1; p373, p472; STM32H7_RCC.cs L1025; STM32H7_RCC.cs L1026)"),
    ('RCC_APB4ENR', 'LPTIM4EN'): ('acknowledged by name', "disabled: a kernel-clocked peripheral the query does not cover (TVI-WO-1300 (4)); read for 0x0: a 0 gates the peripheral's clock and its registers then ignore writes and read invalid data (p373), a 1 clocks it; the stock RCC sets the peripheral's bus enable from this bit, so a 0 drops its accesses and reads 0, and the bit reads back from the guard's copy (TVI-WO-1206 A1; p373, p472; STM32H7_RCC.cs L1028; STM32H7_RCC.cs L1029)"),
    ('RCC_APB4ENR', 'LPTIM5EN'): ('acknowledged by name', "disabled: a kernel-clocked peripheral the query does not cover (TVI-WO-1300 (4)); read for 0x0: a 0 gates the peripheral's clock and its registers then ignore writes and read invalid data (p373), a 1 clocks it; the stock RCC sets the peripheral's bus enable from this bit, so a 0 drops its accesses and reads 0, and the bit reads back from the guard's copy (TVI-WO-1206 A1; p373, p472; STM32H7_RCC.cs L1031; STM32H7_RCC.cs L1032)"),
    ('RCC_APB4ENR', 'LPUART1EN'): ('acknowledged by name', "disabled: a kernel-clocked peripheral the query does not cover (TVI-WO-1300 (4)); read for 0x0: a 0 gates the peripheral's clock and its registers then ignore writes and read invalid data (p373), a 1 clocks it; the stock RCC sets the peripheral's bus enable from this bit, so a 0 drops its accesses and reads 0, and the bit reads back from the guard's copy (TVI-WO-1206 A1; p373, p472; STM32H7_RCC.cs L1010-1012; STM32H7_RCC.cs L1355-1364)"),
    ('RCC_APB4ENR', 'SAI4EN'): ('acknowledged by name', "disabled: a kernel-clocked peripheral the query does not cover (TVI-WO-1300 (4)); read for 0x0: written 0 only: the bit is stored and reads back from the guard's copy as silicon's does, and the stock RCC registers no peripheral at it, so it gates nothing; no firmware accesses a peripheral behind it (ethernet, the one present, has no access in any run: spike/1206/data/main-8e15435/instance-accesses.tsv: the armed run, the 14 generated boots and the fixture) (TVI-WO-1206 A2; p373, p471; STM32H7_RCC.cs L1043; STM32H7_RCC.cs L1044)"),
    ('RCC_APB4ENR', 'SPI6EN'): ('acknowledged by name', "disabled: a kernel-clocked peripheral the query does not cover (TVI-WO-1300 (4)); read for 0x0: written 0 only: the bit is stored and reads back from the guard's copy as silicon's does, and the stock RCC registers no peripheral at it, so it gates nothing; no firmware accesses a peripheral behind it (ethernet, the one present, has no access in any run: spike/1206/data/main-8e15435/instance-accesses.tsv: the armed run, the 14 generated boots and the fixture) (TVI-WO-1206 A2; p373, p472; STM32H7_RCC.cs L1014; STM32H7_RCC.cs L1015)"),
    ('RCC_APB4ENR', 'SYSCFGEN'): ('acknowledged by name', "the guard's copy; the stock bus enable (computed-read); read for 0x1: a 0 gates the peripheral's clock and its registers then ignore writes and read invalid data (p373), a 1 clocks it; the stock RCC sets the peripheral's bus enable from this bit, so a 0 drops its accesses and reads 0, and the bit reads back from the guard's copy (TVI-WO-1206 A1; p373, p473; STM32H7_RCC.cs L1006; STM32H7_RCC.cs L1007)"),
    ('RCC_APB4ENR', 'VREFEN'): ('acknowledged by name', "the guard's copy; the stock bus enable (computed-read); read for 0x0: written 0 only: the bit is stored and reads back from the guard's copy as silicon's does, and the stock RCC registers no peripheral at it, so it gates nothing; no firmware accesses a peripheral behind it (ethernet, the one present, has no access in any run: spike/1206/data/main-8e15435/instance-accesses.tsv: the armed run, the 14 generated boots and the fixture) (TVI-WO-1206 A2; p373, p471; STM32H7_RCC.cs L1038; STM32H7_RCC.cs L1039)"),
    ('RCC_BDCR', 'BDRST'): ('acknowledged by name', "its reset value only: the RTC's kernel clock (RTCSEL) is not modeled; RCC_BDCR is write-protected after a system reset until PWR_CR1.DBP = 1 (p427): with a PWR guard on the platform, a write that changes RCC_BDCR while its DBP = 0 refuses TVI-RCC-DBP, unmodeled (TVI-WO-1308, ruled 2026-11-26T09:31Z Q10); with none, the stock pwr stub reads DBP = 1; read for 0x0: its reset value, written 0: stored and read back alike; with RTCSEL = 00 the RTC has no clock on either side (p427) (TVI-WO-1206 A4; p305, p427; STM32H7_RCC.cs L496; STM32H7_RCC.cs L483)"),
    ('RCC_BDCR', 'LSEBYP'): ('acknowledged by name', "its reset value only: the RTC's kernel clock (RTCSEL) is not modeled; RCC_BDCR is write-protected after a system reset until PWR_CR1.DBP = 1 (p427): with a PWR guard on the platform, a write that changes RCC_BDCR while its DBP = 0 refuses TVI-RCC-DBP, unmodeled (TVI-WO-1308, ruled 2026-11-26T09:31Z Q10); with none, the stock pwr stub reads DBP = 1; read for 0x0: its reset value, written 0: stored and read back alike; with RTCSEL = 00 the RTC has no clock on either side (p427) (TVI-WO-1206 A4; p305, p427, p428; STM32H7_RCC.cs L486)"),
    ('RCC_BDCR', 'LSECSSON'): ('acknowledged by name', "its reset value only: the RTC's kernel clock (RTCSEL) is not modeled; RCC_BDCR is write-protected after a system reset until PWR_CR1.DBP = 1 (p427): with a PWR guard on the platform, a write that changes RCC_BDCR while its DBP = 0 refuses TVI-RCC-DBP, unmodeled (TVI-WO-1308, ruled 2026-11-26T09:31Z Q10); with none, the stock pwr stub reads DBP = 1; read for 0x0: its reset value, written 0: stored and read back alike; with RTCSEL = 00 the RTC has no clock on either side (p427) (TVI-WO-1206 A4; p305, p427, p428; STM32H7_RCC.cs L488)"),
    ('RCC_BDCR', 'LSEDRV'): ('acknowledged by name', "its reset value only: the RTC's kernel clock (RTCSEL) is not modeled; RCC_BDCR is write-protected after a system reset until PWR_CR1.DBP = 1 (p427): with a PWR guard on the platform, a write that changes RCC_BDCR while its DBP = 0 refuses TVI-RCC-DBP, unmodeled (TVI-WO-1308, ruled 2026-11-26T09:31Z Q10); with none, the stock pwr stub reads DBP = 1; read for 0x0: its reset value, written 0: stored and read back alike; with RTCSEL = 00 the RTC has no clock on either side (p427) (TVI-WO-1206 A4; p305, p427, p428; STM32H7_RCC.cs L487)"),
    ('RCC_BDCR', 'LSEON'): ('acknowledged by name', "its reset value only: the RTC's kernel clock (RTCSEL) is not modeled; RCC_BDCR is write-protected after a system reset until PWR_CR1.DBP = 1 (p427): with a PWR guard on the platform, a write that changes RCC_BDCR while its DBP = 0 refuses TVI-RCC-DBP, unmodeled (TVI-WO-1308, ruled 2026-11-26T09:31Z Q10); with none, the stock pwr stub reads DBP = 1; read for 0x0: its reset value, written 0: stored and read back alike; with RTCSEL = 00 the RTC has no clock on either side (p427) (TVI-WO-1206 A4; p305, p427, p428; STM32H7_RCC.cs L484-485)"),
    ('RCC_BDCR', 'RTCEN'): ('acknowledged by name', 'the guard\'s copy, served on read: 0 from construction (a backup domain reset, p427, p334), kept across a machine reset (p427); it gates rtc_ck only (p366), which has no source while RTCSEL = 00 ("No clock", p427), and the RTC\'s registers follow RCC_APB4ENR.RTCAPBEN (p471), so `rtc` is never locked by it; RCC_BDCR\'s write protection (PWR_CR1.DBP, p427): with a PWR guard on the platform, a write that changes RCC_BDCR while its DBP = 0 refuses TVI-RCC-DBP, unmodeled (TVI-WO-1308, ruled 2026-11-26T09:31Z Q10); with none, the stock pwr stub reads DBP = 1 and the write is taken (acknowledged by name) (TVI-WO-1300b, ruled 2026-11-25T03:03Z Q4); read for 0x0,0x1: written 0, then 1 at 4.93 s (BDCR 0x8000), and read back 1 every 100 ms (90 reads): silicon\'s register holds it and the guard serves its copy (the guard keeps rtc on the bus itself); with RTCSEL = 00 rtc_ck has no source on either side (p427), and the firmware uses only the RTC\'s backup registers, which follow RCC_APB4ENR.RTCAPBEN (p366): no time register is read (TVI-WO-1206 A3; p305, p366, p427, p471; STM32H7_RCC.cs L493-495; STM32H7_RCC.cs L1364)'),
    ('RCC_BDCR', 'RTCSEL'): ('acknowledged by name', "its reset value only: the RTC's kernel clock (RTCSEL) is not modeled; RCC_BDCR is write-protected after a system reset until PWR_CR1.DBP = 1 (p427): with a PWR guard on the platform, a write that changes RCC_BDCR while its DBP = 0 refuses TVI-RCC-DBP, unmodeled (TVI-WO-1308, ruled 2026-11-26T09:31Z Q10); with none, the stock pwr stub reads DBP = 1; read for 0x0: its reset value, written 0: stored and read back alike; with RTCSEL = 00 the RTC has no clock on either side (p427) (TVI-WO-1206 A4; p305, p427; STM32H7_RCC.cs L491; STM32F4_RTC.cs L32)"),
    ('RCC_CSR', 'LSION'): ("stock, wrong (the twin's LSIRDY follows LSION alone, where silicon also runs the LSI for a started IWDG (p344, p429; the armed run starts it at 4.77 s); unaffected: no firmware reads RCC_CSR: no read in the armed run, the generated boots or the fixture (spike/1206/data: the armed run (main-8e15435/reads.tsv), the 14 generated boots and the fixture (gen-reads.tsv)))", 'the stock model (stored): stored (local var); read for 0x0 (TVI-WO-1206; p344, p429; STM32H7_RCC.cs L498-501)'),
    ('RCC_D1CFGR', 'D1CPRE'): ('acknowledged by name', "the stock model (stored+callback): stored; rcc_hclk = sys_ck / D1CPRE / HPRE (pp. 394-395) in the guard's KernelHz and BusHz (the overlay's one frequency rule; TVI-WO-1300c, ruled 2026-11-26T04:03Z); read for 0x0: for the value written the twin's bus frequency equals silicon's (the divider's table, pp394-396); the guard's KernelHz and BusHz decode every value as the manual does (TVI-WO-1206 A5; p351, p394; STM32H7_RCC.cs L245; STM32H7_RCC.cs L247-250)"),
    ('RCC_D1CFGR', 'D1PPRE'): ('acknowledged by name', "the stock model (stored+callback): stored; changeCallback derives d1PpreDivider (APB3 divider); read for 0x4: for the value written the twin's bus frequency equals silicon's (the divider's table, pp394-396); the guard's KernelHz and BusHz decode every value as the manual does (TVI-WO-1206 A5; p395; STM32H7_RCC.cs L243; STM32H7_RCC.cs L247-250)"),
    ('RCC_D1CFGR', 'HPRE'): ('acknowledged by name', "the stock model (stored+callback): stored; rcc_hclk = sys_ck / D1CPRE / HPRE (pp. 394-395) in the guard's KernelHz and BusHz (the overlay's one frequency rule; TVI-WO-1300c, ruled 2026-11-26T04:03Z); read for 0x8: for the value written the twin's bus frequency equals silicon's (the divider's table, pp394-396); the guard's KernelHz and BusHz decode every value as the manual does (TVI-WO-1206 A5; p395; STM32H7_RCC.cs L242; STM32H7_RCC.cs L247-250)"),
    ('RCC_D2CFGR', 'D2PPRE1'): ('acknowledged by name', "the stock model (stored+callback): stored; rcc_pclk1 = rcc_hclk / D2PPRE1 (p396) in the guard's KernelHz and BusHz (the overlay's one frequency rule; TVI-WO-1300c, ruled 2026-11-26T04:03Z); read for 0x4: for the value written the twin's bus frequency equals silicon's (the divider's table, pp394-396); the guard's KernelHz and BusHz decode every value as the manual does (TVI-WO-1206 A5; p351, p352, p396; STM32H7_RCC.cs L254; STM32H7_RCC.cs L258-261)"),
    ('RCC_D2CFGR', 'D2PPRE2'): ('acknowledged by name', "the stock model (stored+callback): stored; rcc_pclk2 = rcc_hclk / D2PPRE2 (p396) in the guard's KernelHz and BusHz (the overlay's one frequency rule; TVI-WO-1300c, ruled 2026-11-26T04:03Z); read for 0x4: for the value written the twin's bus frequency equals silicon's (the divider's table, pp394-396); the guard's KernelHz and BusHz decode every value as the manual does (TVI-WO-1206 A5; p351, p352, p396; STM32H7_RCC.cs L256; STM32H7_RCC.cs L258-261)"),
    ('RCC_D3CFGR', 'D3PPRE'): ("stock, wrong (the stock model applies a new APB4 divider to its APB4 peripherals only at the next CR, CFGR, D1CFGR or D2CFGR write (no update on D3CFGR); unaffected: the armed run's CFGR write 3.98 us later applies it; no APB4 timer runs (lptimer2-5: no access in any run, spike/1206/data/main-8e15435/instance-accesses.tsv: the armed run, the 14 generated boots and the fixture); the guard's own APB4 frequency is computed at the write)", 'the stock model (stored+callback): stored; derives d3PpreDivider (APB4); NO register-level PropagateClock here; read for 0x4 (TVI-WO-1206; p397; STM32H7_RCC.cs L262-265; STM32H7_RCC.cs L1438-1441)'),
    ('RCC_PLL1DIVR', 'DIVN1'): ('acknowledged by name', "the stock model (computed-read); the guard's copy of the write it passed, silicon's value, feeds the guard's KernelHz and BusHz (the overlay's one frequency rule; TVI-WO-1300c, ruled 2026-11-26T04:03Z) (pp. 403-404): none after a write the stock model did not keep, or one made while PLL1 ran (Q2); read for 0x31,0x4f,0x9f: for the value written the twin's PLL1 frequency equals the manual's formula (p347: VCO = ref x (DIVN + 1), out = VCO / (DIVy + 1)), inside p346's input ranges: the armed run's VCOH on 16 MHz (HSI / 4), the generated boards' VCOH on 3.2 MHz (hse-crystal-16: HSE 16 / 5) and VCOL on 1.6 MHz (hse-external-8: 8 / 5), each VCO in its range (p404) (TVI-WO-1206 A6; p347, p404; STM32H7_RCC.cs L312-319; STM32H7_RCC.cs L320-328)"),
    ('RCC_PLL1DIVR', 'DIVP1'): ('acknowledged by name', "the stock model (computed-read); the guard's copy of the write it passed, silicon's value, feeds the guard's KernelHz and BusHz (the overlay's one frequency rule; TVI-WO-1300c, ruled 2026-11-26T04:03Z) (pp. 403-404): none after a write the stock model did not keep, or one made while PLL1 ran (Q2); DIVP1 = 0 or an odd divisor gives none (p404; Figure 47 p346; Q7); read for 0x1,0x3: for the value written the twin's PLL1 frequency equals the manual's formula (p347: VCO = ref x (DIVN + 1), out = VCO / (DIVy + 1)), inside p346's input ranges: the armed run's VCOH on 16 MHz (HSI / 4), the generated boards' VCOH on 3.2 MHz (hse-crystal-16: HSE 16 / 5) and VCOL on 1.6 MHz (hse-external-8: 8 / 5), each VCO in its range (p404) (TVI-WO-1206 A6; p347, p404; STM32H7_RCC.cs L329-334; STM32H7_RCC.cs L335-343)"),
    ('RCC_PLL1DIVR', 'DIVQ1'): ('acknowledged by name', "the stock model (stored+callback); the guard's copy of the write it passed, silicon's value, feeds the guard's KernelHz and BusHz (the overlay's one frequency rule; TVI-WO-1300c, ruled 2026-11-26T04:03Z) (pp. 403-404): none after a write the stock model did not keep, or one made while PLL1 ran (Q2); read for 0x3,0x9: for the value written the twin's PLL1 frequency equals the manual's formula (p347: VCO = ref x (DIVN + 1), out = VCO / (DIVy + 1)), inside p346's input ranges: the armed run's VCOH on 16 MHz (HSI / 4), the generated boards' VCOH on 3.2 MHz (hse-crystal-16: HSE 16 / 5) and VCOL on 1.6 MHz (hse-external-8: 8 / 5), each VCO in its range (p404) (TVI-WO-1206 A6; p347, p404; STM32H7_RCC.cs L350-357)"),
    ('RCC_PLL1DIVR', 'DIVR1'): ('acknowledged by name', "the stock model (stored+callback); the guard's copy of the write it passed, silicon's value, feeds the guard's KernelHz and BusHz (the overlay's one frequency rule; TVI-WO-1300c, ruled 2026-11-26T04:03Z) (pp. 403-404): none after a write the stock model did not keep, or one made while PLL1 ran (Q2); read for 0x1: for the value written the twin's PLL1 frequency equals the manual's formula (p347: VCO = ref x (DIVN + 1), out = VCO / (DIVy + 1)), inside p346's input ranges: the armed run's VCOH on 16 MHz (HSI / 4), the generated boards' VCOH on 3.2 MHz (hse-crystal-16: HSE 16 / 5) and VCOL on 1.6 MHz (hse-external-8: 8 / 5), each VCO in its range (p404) (TVI-WO-1206 A6; p347, p403; STM32H7_RCC.cs L359-366)"),
    ('RCC_PLL2DIVR', 'DIVN2'): ('acknowledged by name', "the stock model (stored, DIVN and DIVQ narrowed to 8 and 6 bits where silicon has 9 and 7, a row); the guard's copy of the write it passed, silicon's value, feeds the guard's KernelHz and BusHz (the overlay's one frequency rule; TVI-WO-1300c, ruled 2026-11-26T04:03Z) (pp. 406-407): none after a write the stock model did not keep, or one made while PLL2 ran (Q2); read for 0x2c: by TVI-WO-1300c's ruling Q1 (2026-11-26T04:03Z), on field evidence: PLL2 runs VCOL (PLLCFGR 0x1BF0FFD, PLL2VCOSEL = 1) on an 8 MHz reference (PLLCKSELR 0x408040: HSI 64 MHz / DIVM2 8), where p346 gives VCOL 1 to 2 MHz and p347 says VCOL must be chosen only at or below 2 MHz; RM0433 then guarantees no frequency. The twin serves the formula's (pll2_q_ck 72 MHz, every USART's kernel clock), as the PLL2VCOSEL row acknowledges by name (as ChibiOS chooses it; bench row R11). PLL2's stock fields are narrower than silicon's, and every value written fits them (TVI-WO-1206 A13; p346, p347, p407; STM32H7_RCC.cs L372-378; PeripheralRegister.cs L595-602)"),
    ('RCC_PLL2DIVR', 'DIVP2'): ('acknowledged by name', "the stock model (stored, DIVN and DIVQ narrowed to 8 and 6 bits where silicon has 9 and 7, a row); the guard's copy of the write it passed, silicon's value, feeds the guard's KernelHz and BusHz (the overlay's one frequency rule; TVI-WO-1300c, ruled 2026-11-26T04:03Z) (pp. 406-407): none after a write the stock model did not keep, or one made while PLL2 ran (Q2); read for 0x1: by TVI-WO-1300c's ruling Q1 (2026-11-26T04:03Z), on field evidence: PLL2 runs VCOL (PLLCFGR 0x1BF0FFD, PLL2VCOSEL = 1) on an 8 MHz reference (PLLCKSELR 0x408040: HSI 64 MHz / DIVM2 8), where p346 gives VCOL 1 to 2 MHz and p347 says VCOL must be chosen only at or below 2 MHz; RM0433 then guarantees no frequency. The twin serves the formula's (pll2_q_ck 72 MHz, every USART's kernel clock), as the PLL2VCOSEL row acknowledges by name (as ChibiOS chooses it; bench row R11). PLL2's stock fields are narrower than silicon's, and every value written fits them (TVI-WO-1206 A13; p346, p347, p407; STM32H7_RCC.cs L372-378; PeripheralRegister.cs L595-602)"),
    ('RCC_PLL2DIVR', 'DIVQ2'): ('acknowledged by name', "the stock model (stored, DIVN and DIVQ narrowed to 8 and 6 bits where silicon has 9 and 7, a row); the guard's copy of the write it passed, silicon's value, feeds the guard's KernelHz and BusHz (the overlay's one frequency rule; TVI-WO-1300c, ruled 2026-11-26T04:03Z) (pp. 406-407): none after a write the stock model did not keep, or one made while PLL2 ran (Q2); read for 0x4: by TVI-WO-1300c's ruling Q1 (2026-11-26T04:03Z), on field evidence: PLL2 runs VCOL (PLLCFGR 0x1BF0FFD, PLL2VCOSEL = 1) on an 8 MHz reference (PLLCKSELR 0x408040: HSI 64 MHz / DIVM2 8), where p346 gives VCOL 1 to 2 MHz and p347 says VCOL must be chosen only at or below 2 MHz; RM0433 then guarantees no frequency. The twin serves the formula's (pll2_q_ck 72 MHz, every USART's kernel clock), as the PLL2VCOSEL row acknowledges by name (as ChibiOS chooses it; bench row R11). PLL2's stock fields are narrower than silicon's, and every value written fits them (TVI-WO-1206 A13; p346, p347, p407; STM32H7_RCC.cs L372-378; PeripheralRegister.cs L595-602)"),
    ('RCC_PLL2DIVR', 'DIVR2'): ('acknowledged by name', "the stock model (stored, DIVN and DIVQ narrowed to 8 and 6 bits where silicon has 9 and 7, a row); the guard's copy of the write it passed, silicon's value, feeds the guard's KernelHz and BusHz (the overlay's one frequency rule; TVI-WO-1300c, ruled 2026-11-26T04:03Z) (pp. 406-407): none after a write the stock model did not keep, or one made while PLL2 ran (Q2); read for 0x7: by TVI-WO-1300c's ruling Q1 (2026-11-26T04:03Z), on field evidence: PLL2 runs VCOL (PLLCFGR 0x1BF0FFD, PLL2VCOSEL = 1) on an 8 MHz reference (PLLCKSELR 0x408040: HSI 64 MHz / DIVM2 8), where p346 gives VCOL 1 to 2 MHz and p347 says VCOL must be chosen only at or below 2 MHz; RM0433 then guarantees no frequency. The twin serves the formula's (pll2_q_ck 72 MHz, every USART's kernel clock), as the PLL2VCOSEL row acknowledges by name (as ChibiOS chooses it; bench row R11). PLL2's stock fields are narrower than silicon's, and every value written fits them (TVI-WO-1206 A13; p346, p347, p406; STM32H7_RCC.cs L372-378; PeripheralRegister.cs L595-602)"),
    ('RCC_PLL3DIVR', 'DIVN3'): ('acknowledged by name', 'the stock model (stored, DIVN and DIVQ narrowed to 8 and 6 bits where silicon has 9 and 7, a row); the guard\'s copy of the write it passed, silicon\'s value, feeds the guard\'s KernelHz and BusHz (the overlay\'s one frequency rule; TVI-WO-1300c, ruled 2026-11-26T04:03Z) (pp. 409-410): none after a write the stock model did not keep, or one made while PLL3 ran (Q2); read for 0xe: by the architect\'s ruling, 2026-11-27T00:10Z, on field evidence: p347 says "To insure an optimal behavior of the PLL when one of the post-divider (DIVP, DIVQ or DIVR) is not used, the application shall set the enable bit (DIVyEN) as well as the corresponding post-divider bits (DIVP, DIVQ or DIVR) to \'0\'. If the above rules are not respected, the PLL output frequency is not guaranteed." The armed run writes PLLCFGR 0x1FF0000, PLL3DIVR 0x7040E0E, PLLCFGR 0x1BF0FFD (DIVP3EN = 0, DIVP3 = 7), then CR 0x15003025 (PLL1-3 on). PLL3 also runs VCOL on a 16 MHz reference (PLL3VCOSEL = 1, DIVM3 4), outside p346\'s 1 to 2 MHz (the PLL3VCOSEL row, Q1, R11). RM0433 guarantees no frequency for PLL3\'s outputs in this configuration. The twin serves the formula\'s 30 MHz on pll3_r_ck to I2C1/2 and the ADC. ArduPilot flies this configuration on silicon, which is field evidence, not the manual. Bench row BR1 (TVI-WO-1206 FEEDBACK §5): measure pll3_r_ck on the Nucleo with DIVP3EN = 0 and DIVP3 = 7, by I2C SCL timing (TVI-WO-1206 A11; p346, p347, p410; STM32H7_RCC.cs L383-389; PeripheralRegister.cs L595-602)'),
    ('RCC_PLL3DIVR', 'DIVP3'): ('acknowledged by name', 'the stock model (stored, DIVN and DIVQ narrowed to 8 and 6 bits where silicon has 9 and 7, a row); the guard\'s copy of the write it passed, silicon\'s value, feeds the guard\'s KernelHz and BusHz (the overlay\'s one frequency rule; TVI-WO-1300c, ruled 2026-11-26T04:03Z) (pp. 409-410): none after a write the stock model did not keep, or one made while PLL3 ran (Q2); read for 0x7: by the architect\'s ruling, 2026-11-27T00:10Z, on field evidence: p347 says "To insure an optimal behavior of the PLL when one of the post-divider (DIVP, DIVQ or DIVR) is not used, the application shall set the enable bit (DIVyEN) as well as the corresponding post-divider bits (DIVP, DIVQ or DIVR) to \'0\'. If the above rules are not respected, the PLL output frequency is not guaranteed." The armed run writes PLLCFGR 0x1FF0000, PLL3DIVR 0x7040E0E, PLLCFGR 0x1BF0FFD (DIVP3EN = 0, DIVP3 = 7), then CR 0x15003025 (PLL1-3 on). PLL3 also runs VCOL on a 16 MHz reference (PLL3VCOSEL = 1, DIVM3 4), outside p346\'s 1 to 2 MHz (the PLL3VCOSEL row, Q1, R11). RM0433 guarantees no frequency for PLL3\'s outputs in this configuration. The twin serves the formula\'s 30 MHz on pll3_r_ck to I2C1/2 and the ADC. ArduPilot flies this configuration on silicon, which is field evidence, not the manual. Bench row BR1 (TVI-WO-1206 FEEDBACK §5): measure pll3_r_ck on the Nucleo with DIVP3EN = 0 and DIVP3 = 7, by I2C SCL timing (TVI-WO-1206 A11; p346, p347, p410; STM32H7_RCC.cs L383-389; PeripheralRegister.cs L595-602)'),
    ('RCC_PLL3DIVR', 'DIVQ3'): ('acknowledged by name', 'the stock model (stored, DIVN and DIVQ narrowed to 8 and 6 bits where silicon has 9 and 7, a row); the guard\'s copy of the write it passed, silicon\'s value, feeds the guard\'s KernelHz and BusHz (the overlay\'s one frequency rule; TVI-WO-1300c, ruled 2026-11-26T04:03Z) (pp. 409-410): none after a write the stock model did not keep, or one made while PLL3 ran (Q2); read for 0x4: by the architect\'s ruling, 2026-11-27T00:10Z, on field evidence: p347 says "To insure an optimal behavior of the PLL when one of the post-divider (DIVP, DIVQ or DIVR) is not used, the application shall set the enable bit (DIVyEN) as well as the corresponding post-divider bits (DIVP, DIVQ or DIVR) to \'0\'. If the above rules are not respected, the PLL output frequency is not guaranteed." The armed run writes PLLCFGR 0x1FF0000, PLL3DIVR 0x7040E0E, PLLCFGR 0x1BF0FFD (DIVP3EN = 0, DIVP3 = 7), then CR 0x15003025 (PLL1-3 on). PLL3 also runs VCOL on a 16 MHz reference (PLL3VCOSEL = 1, DIVM3 4), outside p346\'s 1 to 2 MHz (the PLL3VCOSEL row, Q1, R11). RM0433 guarantees no frequency for PLL3\'s outputs in this configuration. The twin serves the formula\'s 30 MHz on pll3_r_ck to I2C1/2 and the ADC. ArduPilot flies this configuration on silicon, which is field evidence, not the manual. Bench row BR1 (TVI-WO-1206 FEEDBACK §5): measure pll3_r_ck on the Nucleo with DIVP3EN = 0 and DIVP3 = 7, by I2C SCL timing (TVI-WO-1206 A11; p346, p347, p410; STM32H7_RCC.cs L383-389; PeripheralRegister.cs L595-602)'),
    ('RCC_PLL3DIVR', 'DIVR3'): ('acknowledged by name', 'the stock model (stored, DIVN and DIVQ narrowed to 8 and 6 bits where silicon has 9 and 7, a row); the guard\'s copy of the write it passed, silicon\'s value, feeds the guard\'s KernelHz and BusHz (the overlay\'s one frequency rule; TVI-WO-1300c, ruled 2026-11-26T04:03Z) (pp. 409-410): none after a write the stock model did not keep, or one made while PLL3 ran (Q2); read for 0x7: by the architect\'s ruling, 2026-11-27T00:10Z, on field evidence: p347 says "To insure an optimal behavior of the PLL when one of the post-divider (DIVP, DIVQ or DIVR) is not used, the application shall set the enable bit (DIVyEN) as well as the corresponding post-divider bits (DIVP, DIVQ or DIVR) to \'0\'. If the above rules are not respected, the PLL output frequency is not guaranteed." The armed run writes PLLCFGR 0x1FF0000, PLL3DIVR 0x7040E0E, PLLCFGR 0x1BF0FFD (DIVP3EN = 0, DIVP3 = 7), then CR 0x15003025 (PLL1-3 on). PLL3 also runs VCOL on a 16 MHz reference (PLL3VCOSEL = 1, DIVM3 4), outside p346\'s 1 to 2 MHz (the PLL3VCOSEL row, Q1, R11). RM0433 guarantees no frequency for PLL3\'s outputs in this configuration. The twin serves the formula\'s 30 MHz on pll3_r_ck to I2C1/2 and the ADC. ArduPilot flies this configuration on silicon, which is field evidence, not the manual. Bench row BR1 (TVI-WO-1206 FEEDBACK §5): measure pll3_r_ck on the Nucleo with DIVP3EN = 0 and DIVP3 = 7, by I2C SCL timing (TVI-WO-1206 A11; p346, p347, p409; STM32H7_RCC.cs L383-389; PeripheralRegister.cs L595-602)'),
    ('RCC_RSR', 'RMVF'): ("stock, wrong (RMVF reads back 0 (write-one-to-clear) where silicon's is set and reset by software (p451); the flags it clears read 0 after on both sides; unaffected: RCC_RSR is read once, before the write, by the armed run and by the fixture, and never after (spike/1206/data: the armed run (main-8e15435/reads.tsv), the 14 generated boots and the fixture (gen-reads.tsv)))", 'the stock model (write-only-action): FieldMode.WriteOneToClear: always reads 0; write 1 clears C1RSTF,C2RSTF,D1RSTF,D2RSTF,BORRSTF,PINRSTF,PORRSTF (not the t; read for 0x1 (TVI-WO-1206; p333, p450, p451; STM32H7_RCC.cs L672-679; STM32H7_RCC.cs L680-684)'),
}
# TVI-WO-1206 readings END

HERE = os.path.dirname(os.path.abspath(__file__))
H = HERE + '/data/'
ROOT = os.path.dirname(os.path.dirname(HERE))
MODEL = ROOT + '/model/GuardedSTM32H7_RCC.cs'
RESC = HERE + '/fields.resc'
FIELDS_OPEN = '        private static readonly Row[] Fields =\n        {\n'
FIELDS_CLOSE = '        };\n'

SIX = {'SPI1EN', 'SPI2EN', 'I2C1EN', 'I2C2EN', 'USART1EN', 'RNGEN', 'USART2EN', 'USART3EN'}  # F7 (14:35Z): USART2/3 joined
# Kernel-clocked enables: the RM bit text names a kernel clock (39), plus Table 60 (p355-356) muxes whose enable text
# names only a "delay clock" or the pixel clock.
EXTRA_KERNEL = {'QSPIEN', 'SDMMC1EN', 'SDMMC2EN', 'LTDCEN'}
# Selector values the guard models, per mux (the source each names is modeled: the guard's MuxDef tables).
MUX_ALLOWED = {
    ('RCC_D1CCIPR', 'CKPERSEL[1:0]'): [0, 1, 2, 3],
    ('RCC_D2CCIP1R', 'SPI123SEL[2:0]'): [0, 1, 2, 4],               # 011 I2S_CKIN: a pin, not modeled; 101-111 reserved
                                                                    # (p414; refused, ruled 2026-11-23 17:16Z, TVI-WO-1300a)
    ('RCC_D2CCIP2R', 'I2C123SEL[1:0]'): [0, 1, 2, 3],
    ('RCC_D2CCIP2R', 'USART16SEL[2:0]'): [0, 1, 2, 3, 4, 6, 7],     # 101 lse_ck: not modeled
    ('RCC_D2CCIP2R', 'RNGSEL[1:0]'): [0, 1],                        # 10 lse_ck, 11 lsi_ck: not modeled
    ('RCC_D2CCIP2R', 'USART234578SEL[2:0]'): [0, 1, 2, 3, 4, 6, 7], # F7; 101 lse_ck: not modeled
}
# The architect's acknowledgements (TVI-WO-1300.rm0433.md §6.1, ruled 14:35Z): field -> (allowed, A-number, behaves as)
ACKED = {}
for f in ('MCO1PRE[3:0]', 'MCO2PRE[3:0]', 'RTCPRE[5:0]'):
    ACKED[('RCC_CFGR', f)] = (None, 2, 'no MCO pin and no RTC clock are modeled; with MCOx at reset no pin is driven')
ACKED[('RCC_CR', 'HSEBYP')] = (None, 3, 'the twin has no oscillator: bypass and crystal behave alike')
ACKED_RSTR = (None, 4, 'reserved: no flop on either side; written 1 by all-ones resets')
ACKED[('RCC_RSR', 'Reserved', 18)] = ([0, 1], 5, 'reserved, reset 1 by the register\'s 0x00FE0000: written 0 with RMVF, ignored by both')
for reg, f in (('RCC_AHB1LPENR', 'DMA2LPEN'), ('RCC_AHB2LPENR', 'RNGLPEN'), ('RCC_APB1LLPENR', 'SPI2LPEN'), ('RCC_APB2LPENR', 'SPI1LPEN')):
    ACKED[(reg, f)] = ([0, 1], 6, 'the twin does not model CSleep: the clock runs while the CPU sleeps, whatever this bit says (backlog)')
# TVI-WO-1300b (3), ruled 2026-11-25T03:03Z Q1: the armed Sentinel-03 run's four LPEN clears, acknowledged by name, those four only,
# with A6's reason (the release resolves an acknowledgement by its A-number: tools/tviburi-release `acknowledgement`)
ACKED_1300B = {(reg, f): ([0, 1], 'the twin does not model CSleep: the clock runs while the CPU sleeps, whatever this bit says (backlog) '
                                  '(TVI-WO-1300 A6; extended to this bit by TVI-WO-1300b, ruled 2026-11-25T03:03Z Q1)')
               for reg, f in (('RCC_AHB1LPENR', 'ADC12LPEN'), ('RCC_AHB4LPENR', 'ADC3LPEN'), ('RCC_APB1LLPENR', 'USART2LPEN'), ('RCC_APB2LPENR', 'TIM1LPEN'))}
GUARD_ANY = {('RCC_PLLCKSELR', f) for f in ('PLLSRC[1:0]', 'DIVM1[5:0]', 'DIVM2[5:0]', 'DIVM3[5:0]')} | \
    {('RCC_PLLCFGR', f'DIV{o}{n}EN') for o in 'PQR' for n in '123'}


# TVI-WO-1300c (4), ruled 2026-11-26T04:03Z: the rows that feed the guard's KernelHz and BusHz (the overlay's one frequency rule),
# restated with what the field does to the frequency and its page. Applied after the classification above; a row's class is kept,
# but for RCC_PLLCFGR's RGE, VCOSEL and FRACEN, now the guard's copy served on read (they were tags reading 0, acknowledged as A1
# "no PLL frequency is modeled beyond PLL1's integer one", no longer true).
RULED_C = 'TVI-WO-1300c, ruled 2026-11-26T04:03Z'
ONE_RULE = "the guard's KernelHz and BusHz (the overlay's one frequency rule; " + RULED_C + ')'
FREQ = {
    ('RCC_CR', 'HSIDIV[1:0]'): (None, 'the stock model (stored+callback): stored; a change reverted while PLLSRC = HSI with a PLL on; hsi_ck = 64 MHz / HSIDIV '
        '(p385), for sys_ck, hsi_ker_ck and the PLL reference in ' + ONE_RULE),
    ('RCC_CFGR', 'SWS[2:0]'): (None, 'ignored (read-only; stock computed-read); it selects sys_ck (the HSI, CSI, HSE or pll1_p_ck, p393) for ' + ONE_RULE),
    ('RCC_D1CFGR', 'D1CPRE[3:0]'): (None, 'the stock model (stored+callback): stored; rcc_hclk = sys_ck / D1CPRE / HPRE (pp. 394-395) in ' + ONE_RULE),
    ('RCC_D1CFGR', 'HPRE[3:0]'): (None, 'the stock model (stored+callback): stored; rcc_hclk = sys_ck / D1CPRE / HPRE (pp. 394-395) in ' + ONE_RULE),
    ('RCC_D2CFGR', 'D2PPRE1[2:0]'): (None, 'the stock model (stored+callback): stored; rcc_pclk1 = rcc_hclk / D2PPRE1 (p396) in ' + ONE_RULE),
    ('RCC_D2CFGR', 'D2PPRE2[2:0]'): (None, 'the stock model (stored+callback): stored; rcc_pclk2 = rcc_hclk / D2PPRE2 (p396) in ' + ONE_RULE),
}
for n in '123':
    FREQ[('RCC_PLLCKSELR', f'DIVM{n}[5:0]')] = (None, "the guard's copy (the kernel clock query); ref" + n + '_ck = the PLLSRC source / DIVM' + n
        + ', 0 "prescaler disabled" (pp. 398-399), in ' + ONE_RULE)
    FREQ[('RCC_PLLCFGR', f'PLL{n}RGE[1:0]')] = ('guard', "the guard's copy, served on read (the stock model drops it): ref" + n + '_ck must lie in the range it '
        'names, 1-2, 2-4, 4-8 or 8-16 MHz, edges inclusive, else ' + ONE_RULE + ' has no frequency ("must be set according to the reference input '
        'frequency", p347; pp. 401-402; Q3)')
    FREQ[('RCC_PLLCFGR', f'PLL{n}VCOSEL')] = ('guard', "the guard's copy, served on read: the VCO's output range, 192-960 MHz or 150-420 MHz (p404, "
        'p407, p410), and its input: VCOH takes 2-16 MHz, VCOL 1-2 MHz (p346, p347), else ' + ONE_RULE + ' has no frequency; VCOL above 2 MHz with '
        "its VCO in VCOL's range acknowledged by name, as ChibiOS chooses it (hal_lld_type1.h L1834-1837; bench row R11; Q1)")
    FREQ[('RCC_PLLCFGR', f'PLL{n}FRACEN')] = ('guard', "the guard's copy, served on read; FRACN" + n + ' is 0 (its allow-list), so the PLL runs in '
        'integer mode whatever this bit says (p349, p405) in ' + ONE_RULE)
    page = {'1': 'pp. 403-404', '2': 'pp. 406-407', '3': 'pp. 409-410'}[n]
    for o in 'NPQR':
        # p410 prints DIVN3[7:0] over bits 8:0, and data/rm/fields.tsv keeps its spelling
        FREQ[('RCC_PLL%sDIVR' % n, f'DIV{o}{n}[{(7 if n == "3" else 8) if o == "N" else 6}:0]')] = (None, '%s; the guard\'s copy of the write it passed, silicon\'s value, feeds '
            % ('the stock model (%s)' % ('computed-read' if n == '1' and o in 'NP' else 'stored+callback' if n == '1' else 'stored, DIVN and DIVQ narrowed to 8 and 6 bits where silicon has 9 and 7, a row'))
            + ONE_RULE + ' (' + page + '): none after a write the stock model did not keep, or one made while PLL' + n + ' ran (Q2)'
            + ('; DIVP1 = 0 or an odd divisor gives none (p404; Figure 47 p346; Q7)' if (n, o) == ('1', 'P') else ''))
FREQ[('RCC_CR', 'HSEON')] = (None, 'the stock model (stored+callback): stored; write 0 reverted if SW==HSE or PLL1 selected with PLLSRC==HSE; '
    'in recorded mode with no crystal declared, the stock RCC runs on a placeholder 8 MHz so a run can continue past TVI-RCC-HSE; no guard reads it (TVI-WO-1204 (3) P2, ruled 2026-11-26T12:51Z)')
FREQ[('RCC_PLLCKSELR', 'PLLSRC[1:0]')] = (None, "the guard's copy (the kernel clock query); the PLLs' reference, the HSI, CSI or HSE, 11 none (p399), in " + ONE_RULE)


def tsv(path):
    return [l.rstrip('\n').split('\t') for l in open(path, encoding='utf-8') if l.strip() and not l.startswith(('register', '#'))]


def build():
    stock = collections.defaultdict(list)
    for r in tsv(H + 'stock/fields.tsv'):
        try:
            stock[int(r[1], 16)].append((int(r[3]), int(r[4]), r[5], r[2], r[7]))
        except (ValueError, IndexError):
            pass


    def skind(off, msb, lsb):
        ks = {(k, b) for (m, l, k, n, b) in stock.get(off, []) if not (m < lsb or l > msb) and k != 'other'}
        if not ks:
            return ('undefined' if off not in stock else 'gap'), ''
        kinds = sorted({k for k, _ in ks})
        return ','.join(kinds), '; '.join(sorted({b for _, b in ks}))[:120]


    rows = []
    for r in tsv(H + 'rm/fields.tsv'):
        reg, off, field, msb, lsb, acc, reset, page = r[:8]
        off = int(off, 16); msb = int(msb); lsb = int(lsb)
        if reg == 'RCC_ICSCR':
            continue   # one register per offset: 0x004 is RCC_HSICFGR (revision V); RCC_ICSCR is revision Y's
        if reg == 'RCC_CSICFGR' and field == 'Reserved' and (msb, lsb) == (23, 8):
            lsb = 10   # p390 prints Reserved 23:8 over CSICAL[9:0]: a misprint; CSICAL holds 9:8
        try:
            rv = int(reset, 16) if reset.startswith('0x') else int(reset, 2)
        except ValueError:
            rv = None
        sk, sb = skind(off, msb, lsb)
        name = field
        base = re.sub(r'\[.*', '', field)
        if field == 'Reserved':
            cls, allowed, behaves = 'reserved', [rv or 0], 'reserved: dropped, reads its reset value'
        elif (reg, field) in MUX_ALLOWED:
            cls, allowed, behaves = 'guard', MUX_ALLOWED[(reg, field)], "the guard's copy and its mux (p371)"
        elif (reg, field) in GUARD_ANY:
            cls, allowed, behaves = 'guard', None, "the guard's copy (the kernel clock query)"
        elif reg == 'RCC_D3CCIPR' and base == 'ADCSEL':
            # TVI-WO-1307 (granted 2026-11-25T21:17Z Q3): the query answers adc12 and adc3 from this mux.
            cls, allowed, behaves = 'guard-copy', None, "the guard's copy, served on read; the ADCs' kernel clock mux, answered by the query for adc12 and adc3 (TVI-WO-1307 Q3)"
        elif re.match(r'RCC_D[123]CCIP', reg):
            cls, allowed, behaves = 'guard-copy', None, "the guard's copy, served on read; this mux's peripherals are not modeled, and their enable refuses"
        elif reg.endswith('ENR') and not reg.endswith('LPENR') and acc.startswith('rw'):
            if base in SIX:
                cls, allowed, behaves = 'guard', None, "the guard's copy and the stock bus enable; the kernel clock query"
            elif base in ('ADC12EN', 'ADC3EN'):
                # TVI-WO-1300b (3), ruled 2026-11-25T03:03Z Q1 (superseding 1300a's 17:52Z): the bus enable only
                cls, allowed, behaves = 'inherited', None, ("the guard's copy; the stock bus enable (" + sk + "): the bus lock only. With ADCx_CCR.CKMODE = 00 the "
                    "ADC's kernel clock is the one ADCSEL selects (p920, p419), answered by the query; the ADC guard asks it at ADSTART and registers as "
                    'watched (TVI-WO-1307 Q3) (TVI-WO-1300b, ruled 2026-11-25T03:03Z Q1)')
            elif base in EXTRA_KERNEL or 'kernel clock' in (r[8] if len(r) > 8 else ''):
                cls, allowed, behaves = 'kernel-unmodeled', [0], 'disabled: a kernel-clocked peripheral the query does not cover (TVI-WO-1300 (4))'
            elif sk in ('computed-read',):
                cls, allowed, behaves = 'inherited', None, "the guard's copy; the stock bus enable (" + sk + ')'
            else:
                cls, allowed, behaves = 'tag', [rv], 'its reset value (stock ' + sk + ')'
        elif acc == 'r':
            cls, allowed, behaves = 'read-only', None, 'ignored (read-only; stock ' + sk + ')'
        elif sk in ('stored', 'stored+callback', 'computed-read', 'write-only-action', 'read-only-constant'):
            cls, allowed, behaves = 'inherited', None, 'the stock model (' + sk + '): ' + sb
        else:
            cls, allowed, behaves = 'tag', [rv], 'its reset value (stock ' + sk + ')'
        if (reg, field) == ('RCC_CFGR', 'SW[2:0]'):
            cls, allowed, behaves = 'inherited', [0, 1, 2, 3], 'the stock model (stored+callback); 100-111 are reserved (p393), and the stock model keeps the old value'
        elif (reg, field) == ('RCC_BDCR', 'RTCEN'):
            # TVI-WO-1300b (5), ruled 2026-11-25T03:03Z Q4
            cls, allowed, behaves = 'inherited', [0, 1], ('the guard\'s copy, served on read: 0 from construction (a backup domain reset, p427, '
                'p334), kept across a machine reset (p427); it gates rtc_ck only (p366), which has no source while RTCSEL = 00 ("No clock", p427), '
                'and the RTC\'s registers follow RCC_APB4ENR.RTCAPBEN (p471), so `rtc` is never locked by it; RCC_BDCR\'s write protection '
                '(PWR_CR1.DBP, p427): with a PWR guard on the platform, a write that changes RCC_BDCR while its DBP = 0 refuses TVI-RCC-DBP, '
                'unmodeled (TVI-WO-1308, ruled 2026-11-26T09:31Z Q10); with none, the stock pwr stub reads DBP = 1 and the write is taken '
                '(acknowledged by name) (TVI-WO-1300b, ruled 2026-11-25T03:03Z Q4)')
        elif reg == 'RCC_BDCR' and acc not in ('r', '-') and field != 'Reserved':
            cls, allowed, behaves = 'inherited', [rv or 0], 'its reset value only: the RTC\'s kernel clock (RTCSEL) is not modeled; RCC_BDCR is write-protected after a system reset until PWR_CR1.DBP = 1 (p427): with a PWR guard on the platform, a write that changes RCC_BDCR while its DBP = 0 refuses TVI-RCC-DBP, unmodeled (TVI-WO-1308, ruled 2026-11-26T09:31Z Q10); with none, the stock pwr stub reads DBP = 1'
        if (reg, field) in FREQ:
            c, behaves = FREQ[(reg, field)]
            if c:
                cls, allowed = c, None   # any value: the guard's copy, served on read (was a tag acknowledged as A1)
        if (reg, field) in ACKED_1300B:
            allowed, why = ACKED_1300B[(reg, field)]
            cls, behaves = 'acknowledged', why
        ack = ACKED.get((reg, field)) or ACKED.get((reg, field, msb)) or (ACKED_RSTR if reg.endswith('RSTR') and field == 'Reserved' else None)
        if ack:
            allowed, a, why = ack
            cls, behaves = 'acknowledged', why + f' (TVI-WO-1300 A{a})'
        rows.append(dict(reg=reg, off=off, field=field, msb=msb, lsb=lsb, acc=acc, reset=rv, page=page, cls=cls,
                         allowed=allowed, behaves=behaves, stock=sk))
    return rows


# C# rows for the guard's `Fields` table (one per RM field), and the gate-clause-6 cases for tests/1300/fields.resc.
def cs(s):
    return '"' + s.replace('\\', '\\\\').replace('"', '\\"') + '"'

def ledger_class(x):  # the class strings tools/tviburi-release maps to RUL-16's states
    c = x['cls']
    if c in ('guard', 'guard-copy'):
        return 'modeled by the guard'
    if c in ('inherited', 'kernel-unmodeled'):
        return 'inherited, unaudited (acknowledged by name)'
    if c == 'acknowledged':
        return 'acknowledged by name'
    if c == 'tag' and x['stock'] == 'undefined':
        return 'undefined in the stock model'
    return c   # tag, reserved, read-only


def csname(x):  # SPI's row names: a reserved range is ResM_L (ResM for one bit); a field without its [m:n] suffix
    if x['field'] == 'Reserved':
        return f"Res{x['msb']}" + ('' if x['msb'] == x['lsb'] else f"_{x['lsb']}")
    return re.sub(r'\[.*', '', x['field'])


def emit_rows(rows, section):
    out = []
    for x in rows:
        a = 'Any' if x['allowed'] is None else ('Zero' if x['allowed'] == [0] else 'new uint[] { ' + ', '.join(str(v) for v in x['allowed']) + ' }')
        k, b = READ_1206.get((x['reg'], csname(x)), (ledger_class(x), x['behaves']))  # TVI-WO-1206
        out.append(f"            R({cs(x['reg'])}, 0x{x['off']:03X}, {cs(section[x['reg']])}, {cs(csname(x))}, {x['msb']}, {x['lsb']}, {a}, {cs(k)}, {cs(b)}, {x['page']}),\n")
    return ''.join(out)


def emit_resc(rows):
    out = []
    w = out.append
    # Gate clause 6 (RUL-17): tests/1300/fields.resc, one case per row whose allowed set is restricted: the register's
    # reset word with that field given the smallest value outside its set, then `machine Reset`.
    reset_word = collections.defaultdict(int)
    for x in rows:
        if x['reset']:
            reset_word[x['off']] |= x['reset'] << x['lsb']
    w('# TVI-WO-1300 gate clause 6 (RUL-17): every field outside the RCC allow-list, written with a value the model does\n'
      '# not implement, one per case, from reset; each must refuse with TVI-RCC-FIELD, kind unmodeled, naming the field.\n'
      '# GENERATED by tests/1300/allowlist.py from the allow-list rows (model/GuardedSTM32H7_RCC.cs `Fields`); the\n'
      '# `# FIELD` lines are the expected refusals, in order.\n')
    for x in rows:
        if x['allowed'] is None:
            continue
        width = x['msb'] - x['lsb'] + 1
        bad = next((v for v in range(1 << min(width, 4)) if v not in x['allowed']), None)
        if bad is None:
            continue
        mask = ((1 << width) - 1) << x['lsb']
        word = (reset_word[x['off']] & ~mask) | (bad << x['lsb'])
        bits = f"bit {x['lsb']}" if x['msb'] == x['lsb'] else f"{x['msb']}:{x['lsb']}"   # as Row.Bits formats it
        w(f"# FIELD {x['reg']}.{csname(x)} ({bits})\n")
        w(f"sysbus WriteDoubleWord 0x{0x58024400 + x['off']:08X} 0x{word:08X}\n")
        w("machine Reset\n")
    return ''.join(out)


def read(path):
    with open(path, encoding='utf-8', newline='') as f:
        return f.read()


def splice(model, rows_text):
    if model.count(FIELDS_OPEN) != 1:
        sys.exit('allowlist.py: the Fields opening marker is not found exactly once in the model')
    i = model.index(FIELDS_OPEN) + len(FIELDS_OPEN)
    j = model.index(FIELDS_CLOSE, i)
    return model[:i] + rows_text + model[j:]


def main():
    mode = sys.argv[1] if len(sys.argv) == 2 else ''
    if mode not in ('check', 'write'):
        sys.exit('usage: python3 -I tests/1300/allowlist.py check|write')
    rows = build()
    section = {r[0]: '§' + r[2] for r in tsv(H + 'rm/registers.tsv')}
    want = {MODEL: splice(read(MODEL), emit_rows(rows, section)), RESC: emit_resc(rows)}
    bad = 0
    for path, text in want.items():
        have = read(path)
        if have == text:
            continue
        bad = 1
        if mode == 'write':
            with open(path, 'w', encoding='utf-8', newline='') as f:
                f.write(text)
            print('rewrote', os.path.relpath(path, ROOT))
        else:
            print('DIFFERS:', os.path.relpath(path, ROOT))
            d = list(difflib.unified_diff(have.split('\n'), text.split('\n'), 'committed', 'regenerated', n=0, lineterm=''))
            print('\n'.join(l[:200] for l in d[:12]))
    if mode == 'check':
        print(f'{len(rows)} rows;', 'allow-list differs from the committed files' if bad else 'allow-list regenerates byte for byte')
        return bad
    print(len(rows), 'rows')
    return 0


if __name__ == '__main__':
    sys.exit(main())
