# TVI-WO-1206: the unaudited fields above the cut, read against RM0433 and the stock source

**Builder:** Claude (Opus 5.5), Furnace, 2026-11-27. **Branch:** `wo/tvi-1206-unaudited-read`, from `main` `8e15435`.
**What this document is:** the home of the readings' acknowledgements, **§3, A1–A10**, cited by each acknowledged ledger row as `(TVI-WO-1206 A<n>; …)` (the release resolves it here: `tools/tviburi-release` `acknowledgement()`, granted 2026-11-26T23:36Z).

## 1. Sources

| Source | What | How it was used |
|---|---|---|
| RM0433 Rev 9 | `~/.cache/aet-held/rm0433-rev9.txt`; pages printed by `spike/1206/page.sh` (the convention of `tests/1300/rmpage.sh`) | every `rm` quote checked on its printed page |
| The stock source | renode-infrastructure `066a7f13`, the pinned build's tree (`TVI-WO-0100b.rm0433` §1): `STM32H7_RCC.cs` `7f10d491…` and `STM32H7_SPI.cs` `bc3f1348…` (the hashes 1300 and 0100b pinned), `STM32F7_USART.cs` `d62666e1…`, `STM32_Timer.cs` `7e99f48c…`, `STM32F7_I2C.cs` `4c8e30b7…`, `STM32_GPIOPort.cs` `16ffaed5…` | every `stock` quote checked at its cited lines |
| The guards | `model/` at `8e15435` | every `guard` quote checked at its cited lines |
| The values written | `spike/1206/data/items.tsv`: the armed run's values (1205's join) and the generated boards' (1100's queue, column 10) | each reading covers them |
| What firmware reads | `spike/1206/data/main-8e15435/reads.tsv` (the armed run, every access logged) and `gen-reads.tsv` (the 14 generated boots and ArduPilot's fixture, every access logged by `spike/1206/harness/gen-log.sh`) | every "never read" and "never accessed" |
| Write order | the armed run's log (run B on `main` `8e15435`) and `spike/1100/data/writes.tsv` | every claim about when a field is written |

## 2. How each field was read

- **The unit:** the 129 items above the cut (105 rows, 24 registers) hold **121 distinct ledger fields**. One reading per field covers every instance bound to that guard type. The readers checked per instance; the stock code differs between instances only in constructor frequencies, which none of these readings uses.
- **Predictions first:** `spike/1206/predictions.md`, committed before any reading (`fe69aa5`).
- **The readers:** five workers, one per family slice, each returning verbatim quotes with page and `file Ln`, and what each side does per value (`~/.cache/tvi-1206/reads/`). They assigned no state.
- **The checks:** `spike/1206/check-reads.py`: 121 rows, 0 quotes off their page or lines. The builder then checked each claim against the runs above and assigned every state (`spike/1206/readings.py`, which writes the rows and this document's §3).

| Family | Fields | `acknowledged` | `stock-wrong` | Held (§4) |
|---|---|---|---|---|
| RCC enables | 47 | 42 | 5 | 0 |
| RCC clocks | 27 | 20 | 3 | 4 |
| TIM1 | 17 | 9 | 8 | 0 |
| I2C, GPIO, SPI | 9 | 3 | 5 | 1 |
| USART | 21 | 15 | 6 | 0 |
| **All** | **121** | **89** | **27** | **5** |

## 3. The acknowledgements

| A | Fields | Reading (both sides, for the values written) | Each row cites |
|---|---|---|---|
| A1 | `RCC_AHB1ENR.ADC12EN`, `RCC_AHB1ENR.DMA1EN`, `RCC_AHB1ENR.DMA2EN`, `RCC_AHB1ENR.ETH1MACEN`, `RCC_AHB4ENR.ADC3EN`, `RCC_AHB4ENR.GPIOCEN`, `RCC_AHB4ENR.GPIODEN`, `RCC_AHB4ENR.GPIOEEN`, `RCC_AHB4ENR.GPIOFEN`, `RCC_APB1LENR.TIM2EN`, `RCC_APB2ENR.TIM1EN`, `RCC_APB2ENR.TIM8EN`, `RCC_APB2ENR.TIM15EN`, `RCC_APB2ENR.TIM16EN`, `RCC_APB2ENR.TIM17EN`, `RCC_APB2ENR.SPI4EN`, `RCC_APB2ENR.USART6EN`, `RCC_APB4ENR.I2C4EN`, `RCC_APB4ENR.LPTIM2EN`, `RCC_APB4ENR.LPTIM3EN`, `RCC_APB4ENR.LPTIM4EN`, `RCC_APB4ENR.LPTIM5EN`, `RCC_APB4ENR.LPUART1EN`, `RCC_APB4ENR.SYSCFGEN` | a 0 gates the peripheral's clock and its registers then ignore writes and read invalid data (p373), a 1 clocks it; the stock RCC sets the peripheral's bus enable from this bit, so a 0 drops its accesses and reads 0, and the bit reads back from the guard's copy | its own page(s) and stock line(s) |
| A2 | `RCC_AHB1ENR.ETH1RXEN`, `RCC_AHB1ENR.ETH1TXEN`, `RCC_AHB1ENR.USB1OTGHSULPIEN`, `RCC_AHB1ENR.USB2OTGHSULPIEN`, `RCC_AHB2ENR.DCMIEN`, `RCC_AHB2ENR.HASHEN`, `RCC_AHB2ENR.SDMMC2EN`, `RCC_APB2ENR.HRTIMEN`, `RCC_APB2ENR.DFSDM1EN`, `RCC_APB2ENR.SAI1EN`, `RCC_APB2ENR.SAI2EN`, `RCC_APB2ENR.SAI3EN`, `RCC_APB2ENR.SPI5EN`, `RCC_APB4ENR.SAI4EN`, `RCC_APB4ENR.SPI6EN`, `RCC_APB4ENR.COMP12EN`, `RCC_APB4ENR.VREFEN` | written 0 only, for a peripheral the armed platform does not have (its peripheral listing): the bit is stored and reads back from the guard's copy as silicon's does, and nothing sits behind it to gate | its own page(s) and stock line(s) |
| A3 | `RCC_AHB2ENR.SRAM1EN` | written 1 only: SRAM1 is clocked on silicon (p456, p369) and the twin's sram1 is always mapped | its own page(s) and stock line(s) |
| A4 | `RCC_BDCR.BDRST`, `RCC_BDCR.LSEBYP`, `RCC_BDCR.LSECSSON`, `RCC_BDCR.LSEDRV`, `RCC_BDCR.LSEON`, `RCC_BDCR.RTCSEL`, `RCC_BDCR.RTCEN` | its reset value (RTCEN 0 or 1): stored and read back alike; with RTCSEL = 00 the RTC has no clock on either side (p427), and the RTC's registers follow RCC_APB4ENR.RTCAPBEN (p366) | its own page(s) and stock line(s) |
| A5 | `RCC_D1CFGR.D1CPRE`, `RCC_D1CFGR.D1PPRE`, `RCC_D1CFGR.HPRE`, `RCC_D2CFGR.D2PPRE1`, `RCC_D2CFGR.D2PPRE2` | for the value written the twin's bus frequency equals silicon's (the divider's table, pp394-396); the guard's KernelHz and BusHz decode every value as the manual does | its own page(s) and stock line(s) |
| A6 | `RCC_PLL1DIVR.DIVN1`, `RCC_PLL1DIVR.DIVP1`, `RCC_PLL1DIVR.DIVQ1`, `RCC_PLL1DIVR.DIVR1`, `RCC_PLL2DIVR.DIVN2`, `RCC_PLL2DIVR.DIVP2`, `RCC_PLL2DIVR.DIVQ2`, `RCC_PLL2DIVR.DIVR2` | for the value written the twin's PLL frequency equals the manual's formula (p347: VCO = ref x (DIVN + 1), out = VCO / (DIVy + 1)); PLL2's stock fields are narrower than silicon's, and every value written fits them | its own page(s) and stock line(s) |
| A7 | `TIMx_CCMR1.CC1S[1:0]`, `TIMx_CCMR1.CC2S[1:0]`, `TIMx_CCMR2.CC3S[1:0]`, `TIMx_CCMR2.CC4S[1:0]`, `TIMx_CR1.CMS[1:0]`, `TIMx_CR1.DIR`, `TIMx_CR1.UDIS`, `TIMx_CR2.TI1S`, `TIMx_DIER.TIE` | written 0 only, the mode the timer runs in on both sides: outputs (CCxS = 00), edge-aligned upcounting, updates enabled, no input XOR, no trigger interrupt (the guard holds SMCR.SMS at 0 and refuses EGR.TG) | its own page(s) and stock line(s) |
| A8 | `I2C_CR2.ADD10`, `I2C_CR2.RELOAD`, `I2C_ICR.ADDRCF` | written 0 only: 7-bit addressing, no reload, and a 0 written to ADDRCF clears nothing, alike on both sides | its own page(s) and stock line(s) |
| A9 | `USART_CR1.OVER8`, `USART_CR1.RTOIE`, `USART_CR1.TCIE`, `USART_CR1.TXFNFIE` | written 0 only: oversampling by 16, no receiver-timeout, transfer-complete or TX-FIFO interrupt, alike on both sides | its own page(s) and stock line(s) |
| A10 | `USART_ICR.CMCF`, `USART_ICR.CTSCF`, `USART_ICR.EOBCF`, `USART_ICR.FECF`, `USART_ICR.LBDCF`, `USART_ICR.NECF`, `USART_ICR.ORECF`, `USART_ICR.PECF`, `USART_ICR.TCBGTCF`, `USART_ICR.TXFECF`, `USART_ICR.WUCF` | a 1 clears its ISR flag and a 0 does nothing, on both sides; the twin never sets the flag, which is its own read-only row's matter | its own page(s) and stock line(s) |

## 4. Held for a ruling

- **`RCC_PLL3DIVR` `DIVN3`, `DIVP3`, `DIVQ3`, `DIVR3`:** the armed run leaves DIVP3EN = 0 with DIVP3 = 7, and p347 then guarantees no PLL3 output frequency. Raised to the architect.
- **`I2C_ICR.Res2_1`:** its state is pinned by `tests/1306/i2c.sh` L261. Raised to the architect.
