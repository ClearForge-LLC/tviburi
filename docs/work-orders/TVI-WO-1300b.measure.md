# TVI-WO-1300b — measurement (WO §1 step 1)

**Builder:** Claude (Opus 5.5), Furnace. **Branch:** `wo/tvi-1300b-rcc-rows`, from `main` `87c4164`.
**Held references:** RM0433 Rev 9 (`~/.cache/aet-held/rm0433-rev9.{pdf,txt}`, sha256 `672a9433…` / text `8e02c0c0…`); pages by `tests/1300/rmpage.sh`.
**Stock source read:** `STM32H7_RCC.cs` at `~/.cache/aet-4202/renode-src/inf`, sha256 `7f10d491…`, the file `1300` pinned.
**Inputs, read-only:** `1305`'s armed run (`~/.cache/tvi-1305/armed`: capture `cap/s03`, injection `inj/inject.resc`, console `run2`, ELF `dbf598d8…`); `1305`'s captures of the 14 generated boots (`cap-gen`, Aethelgard `fa12abf4`) and ArduPilot's fixture boot (`cap-ap/fx`).

## 0. Predictions (committed before any run, 2026-11-25T02:06Z)

Written after reading the WO, `1300a` and its FEEDBACK, `1305`'s FEEDBACK, the RCC guard, the stock `STM32H7_RCC.cs`, RM0433 p427 and p471, ChibiOS's `init_bkp_domain` (ArduPilot `fb773567`, `modules/ChibiOS/os/hal/ports/STM32/STM32H7xx/hal_lld.c`, read in Aethelgard's pinned `aethelgard-ardupilot` image), and the armed run's existing records (offline only: no run yet).

**P1. The armed run's RCC records** are the six `1305` listed, and no others: `AHB1ENR.ADC12EN` = 1, `AHB4ENR.ADC3EN` = 1, `AHB1LPENR.ADC12LPEN` = 0, `AHB4LPENR.ADC3LPEN` = 0, `APB1LLPENR.USART2LPEN` = 0, `APB2LPENR.TIM1LPEN` = 0. Each record names only the first field a write fails (`RefusedField` stops there), so I predict that no refused write hides a second failing field.
*Already checked offline (not a run):* the nine distinct refused (offset, value) pairs in `run2`'s 77,108 records, evaluated against every allow-list row, each fail exactly one field (`h/allfields.py`).

**P2. The 14 generated boots and the fixture** make the RCC records `tests/1300/boots.tsv` lists and no others: `d1-drv8876`'s `ADC3EN`, and the nine `per_ck` boards' `TVI-SPI-KERNEL` (SPI, not RCC). The fixture makes none. No generated boot writes an `*LPEN` bit.

**P3. hseFrequency.**
- **Tviburi's committed platforms:** `model/tviburi-nucleo-h753zi.repl` declares 8 MHz (the stock Nucleo's `rcc: hseFrequency: 8_000_000`, restated), `tests/1300/hse16*.repl` 16 MHz, `tests/1300/unregistered.repl` 8 MHz. Every other Tviburi test platform includes the Nucleo board and inherits its 8 MHz.
- **Aethelgard's generated platforms** (`platform.rs` `emit_hse` at Aethelgard `main` `fd12b2f8`): `hseFrequency` only for a board whose specification has an HSE. Of the captured 14, `hse-crystal-16` (16 MHz), `hse-external-8` and `sentinel-02-bench-hse8` (8 MHz) declare one; **the other 11, ArduPilot's fixture and the armed run declare none**, and take the guard's 8 MHz default today.
- **Whether any relies on it** (WO §7's stop): I predict **none**. No platform that declares nothing sets `HSEON`, selects `PLLSRC` = 10, `SW` = 010 or `CKPERSEL` = 10, or sets `HSEBYP`. ArduPilot on Sentinel-03 runs from the HSI (the armed run's first record's state: `CR=0x3F003025`, `HSEON` = 0; `PLLCKSELR=0x00408040`, `PLLSRC` = 00).

**P4. RTCEN.**
- **RM0433:** `RCC_BDCR` "Reset value: 0x0000 0000, reset by Backup domain reset"; `RTCEN` "0: rtc_ck clock is disabled (default after backup domain reset)"; its bits "are only reset after a backup domain reset … Any other internal or external reset will not have any effect on these bits" (§8.7.26 p427; §8.4.6 p334). So silicon's `RTCEN` is 0 at power-on and survives a system reset.
- **The stock model** reads `RTCEN` as `IsPeripheralEnabled(RTC)`: 1 from reset whenever `rtc` is registered (stock L493-495), and writing it locks or unlocks `rtc` on the bus.
- **What `RTCEN` gates on silicon is `rtc_ck`, not the RTC's registers.** "The rtc_ck clock is enabled through RTCEN … The RTC bus interface clock (APB clock) is enabled through RTCAPBEN and RTCAPBLPEN" (p366); `RCC_APB4ENR.RTCAPBEN` "1: The register clock interface of the RTC (APB) is enabled (default after reset)" (p471). **I predict this changes (5)'s premise:** silicon does not lock the RTC's registers at reset (RTCAPBEN = 1). With `RTCEN` = 0 the RTC's calendar does not count, which the stock RTC model does not show.
- **No committed boot reads `RCC_BDCR.RTCEN` and acts on it.** ArduPilot reads `BDCR` in `init_bkp_domain` and compares only `RTCSEL` (`STM32_RTCSEL_NOCLK`, so no backup-domain reset); `HAL_USE_RTC` is `FALSE` in ArduPilot's `halconf.h`, so the `RTCEN` branch is not compiled. No firmware accesses `rtc`.

**P5. (2), a running USART's kernel clock lost:** no committed boot makes an RCC write, after any USART's `UE` = 1, that changes `D2CCIP2R`, the oscillator or PLL2/PLL3 the USART's mux selects, or its `USARTxEN`. So the new watch changes no record.

## 1. The runs

(to follow)
