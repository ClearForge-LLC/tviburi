# TVI-WO-1300c — §1 measurement

**Builder:** Claude (Opus 5.5), Furnace, 2026-11-26. **Branch:** `wo/tvi-1300c-kernel-hz`, from `main` `189214b`.
**State:** predictions (§0), committed before any run of this work order.

## 0. Predictions, committed before any run

**Read before writing them,** so these are not blind on those points:
- **The three copies, at `189214b`** (the guards are unchanged since `ed70138`): the USART guard's `KernelHz`/`Vco`/`Div` (`model/GuardedSTM32F7_USART.cs` L560–630) and its `Written` subscription (L106–119); the ADC guard's `AdcHz`/`SysHz`/`Vco`/`Div` (`model/GuardedSTM32H7_ADC.cs` L853–952) and its subscription (L974–987); the RCC guard's `KernelHz`/`SourceHz`/`PllHz`/`Prescaler` (`model/GuardedSTM32H7_RCC.cs` L222–306), its `divr` (L1120–1122, set in `Pass`), `Copy`/`CopyMask` and the `Clocked` table (L1075–1087).
- **The callers:** the USART guard's receiver start (L288, which in recorded mode runs after `RefusedKernel`) and `FrameTime` (each byte); the ADC guard's `ADCAL`, `ADEN`, `ADSTART`, the rate check after an RCC write (`rateStale`) and `State()`, which every ADC record's `state` carries; the I2C guard's `SclPeriod` (each NACK), which already calls the RCC guard's `KernelHz`.
- **RM0433 Rev 9** (`~/.cache/aet-held/rm0433-rev9.txt`, pages by `tests/1300/rmpage.sh`; not yet rendered): p346 (VCOH "2 to 16 MHz", VCOL "1 to 2 MHz"), p347 ("if the frequency of the reference clock is lower or equal to 2 MHz, then VCOL must be selected, otherwise VCOH must be chosen"; `PLLxRGE` "must be set according to the reference input frequency"; "If the above rules are not respected, the PLL output frequency is not guaranteed"), pp. 401–402 (`PLLxRGE` 00 1–2, 01 2–4, 10 4–8, 11 8–16 MHz; `PLLxVCOSEL`), p403 (`DIVR1` = 0 "Not allowed"), p404 (`DIVP1`: "odd division factors are not allowed", 0 → `vco1_ck`, 2 "Not allowed"; `DIVN1` below 3 "wrong configurations"), pp. 406–407 and 409–410 (no "not allowed" value for PLL2 or PLL3).
- **Earlier measurements:** `1305a` §3 (the HSE boards' `PLLCKSELR`, `PLL1DIVR` and `PLLCFGR`; `PLL1RGE`/`PLL1VCOSEL` read back 0), `1305a` FEEDBACK (the `pllUntrusted` rule and why), `1307` §2 (ArduPilot's ADC1 on `pll3_r_ck`, 30 MHz), `1306a` §2 (the NACK's uses); the memory note that ADC cases run PLL2 at its reset values (129 MHz).

Nothing has been run in this work order.

### P1. The three copies differ in these inputs (a reading; §2.1 states it as the table)

| Input | USART copy | ADC copy | RCC `KernelHz` |
|---|---|---|---|
| **Unit** | `ulong` Hz; `vco = input × (DIVN+1) / DIVM`, then each divider by integer division | `double` | `double` |
| **Gate** | none: computes from the selector register whenever called | none: from `ADCSEL`/`CKPERSEL` as written | answers only where the query says enabled, the mux settled and the source running |
| **Ports** | any USART name (`usart1`–`uart8`) | `adcM1S2`, `adc3` | the `Clocked` table: `usart1`–`3`, `i2c1`–`2`, `spi1`–`2`, `rng`, `adc12`, `adc3` |
| **`RCC_PLLnDIVR`** | the stock read-back, "no rule" once a write did not read back as written (`pllUntrusted`, PLL1 and PLL2) | the same, PLL1–3 | the guard's `divr`, every write it passed (in recorded mode, refused writes too), never marked |
| **`VCOSEL`** | the last `PLLCFGR` the RCC guard passed | the same | the guard's copy of `PLLCFGR` (the same value) |
| **PLL reference** | HSI or HSE; CSI "no rule" | HSI or HSE; CSI "no rule" | HSI, CSI or HSE |
| **`sys_ck`** | HSI or PLL1 | HSI or PLL1 | HSI, CSI, HSE or PLL1 |
| **Sources** | `rcc_pclk1`/`2`, `pll2_q_ck`, `hsi_ker_ck` | `pll2_p_ck`, `pll3_r_ck`, `per_ck` (from `CKPERSEL` as written), `sys_ck`, `rcc_hclk` (`CKMODE` ≠ 00) | every `pllN_x_ck`, `rcc_pclk1`/`2`, `hsi_ker_ck`, `csi_ker_ck`, `hse_ck`, `per_ck` (from its settled mux); no `sys_ck` or `rcc_hclk` query |
| **Ranges** | ref 1–16 MHz, VCO by `VCOSEL` | the same | the same, and `DIVN` ≥ 3 (dead: ref ≤ 16 MHz × 3 < 150 MHz) |

### P2. The committed cases (`1300b`, `1305`, `1305a`, `1306`, `1306a`, `1307`)
- **Every case passes on the instrumented tree** (the shadow only logs).
- **Where the copies compute a number, it is the same number in every case** but those that plant a disagreement on purpose. Those I expect in `1305a`'s `idlenone` (a wide `DIVN2` or an ignored `DIVP1`, where the USART copy says "no rule" and the RCC guard's `divr` gives a number) and possibly `1307`'s `rccchange`/`rate2` (an RCC write under running conversions). Low confidence on which of `1307`'s.
- **The gate:** at least one ADC case's record `state` carries an `f_ADC` the ADC copy computed while the RCC guard's query says the clock does not run (`kernel`, `clocklost` or `unheld`). With the RCC guard's answer it would read `f_ADC none`: a changed record text, so a §7 item. Moderate.
- **The integer chain changes no consumed time in any case:** every frequency the cases reach is a whole number of Hz at each step (64 MHz HSI, 8 and 16 MHz HSE, PLL outputs from 2–16 MHz references with integer `DIVN`).

### P3. The 14 generated boots, ArduPilot's fixture (5 s) and the armed run (13 s)
- **Generated:** USART kernel clocks `rcc_pclk1`/`2` from the HSI (64 MHz), or from PLL1 on the HSE on `hse-crystal-16` and `hse-external-8` (64 MHz); all three copies agree, to the Hz and the nanosecond. `d1-drv8876`'s ADC3 polling gives the same `f_ADC` in the ADC copy and the RCC guard. The ten TMP117 NACKs on `i2c1` (`rcc_pclk1`) agree with the USART copy; the ADC copy has no APB prescaler, so "not that copy's source".
- **ArduPilot:** `usart1`–`usart3` on `pll2_q_ck` (72 MHz, `PLL2DIVR` read back as written), ADC1 on `pll3_r_ck` (30 MHz), the I2C NACKs on `i2c1`/`i2c2`: **no disagreement**, and no port outside `usart1`–`3` reaches a frequency.
- **No firmware writes `PLLnDIVR` with a value the stock model does not keep,** so `pllUntrusted` is never set on a replay.

### P4. The (3) rules, shadow-evaluated
- **Both firmwares obey `RGE`, `VCOSEL` and `DIVP1`** on every PLL they turn on: the HSE boards as `1305a` §3 read them (`hse-crystal-16` ref 3.2 MHz, `RGE` 01, VCOH; `hse-external-8` ref 1.6 MHz, `RGE` 00, VCOL), ArduPilot's PLLs at 8 and 16 MHz with `RGE` 10 or 11 and VCOH. Moderate: ArduPilot's 8 MHz sits on the 4–8/8–16 edge.
- **The boundaries are the risk, not the firmware.** At reset `DIVMn` = 32 on a 64 MHz HSI, so `refn_ck` is exactly 2 MHz with `RGE` = 00 and `VCOSEL` = 0: p346 allows it (VCOH from 2 MHz), p347 forbids it (≤ 2 MHz must be VCOL). The committed ADC cases that run PLL2 from its reset values would lose their clock under a strict p347 reading: a committed case's computed time would change (a §7 stop). I expect to put the edges (2, 4 and 8 MHz) to the architect.
- **No committed firmware sets `DIVP1` to a value p404 forbids** (`DIVP1` = 0, "odd division factors are not allowed" against the table's "0000000: pll1_p_ck = vco1_ck", is a contradiction I also expect to put).

### P5. Memory
The build deletes two copies and their subscriptions, and adds one public `sys_ck`/`rcc_hclk` answer to the RCC guard: net fewer lines compiled, so within ±5 MiB of `main` at the median, eleven boots a side (`1302`'s finding: the cost follows the compiled code).

### P6. The shard projection (`1202a`'s method, at 1.53)
- `main` has ten shards (`1307`), the longest 16.9 min in `1306a`'s CI.
- This WO adds about 6–10 cases (one planted firmware per disagreeing source and per (3) rule) and 8–12 mutants, about 150–300 s at the reference. It also retires the `1305a`/`1307` mutant rows whose text lies in the deleted copies, which a ruling must allow (the fence says "your own rows").
- The slowest shard stays under 20 minutes at 1.53 with ten. Moderate.
