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

## 1. How it was measured (after §0 was committed at `94520ea`)

**Image:** `aethelgard-tools@sha256:1e0ab105…`, the digest Aethelgard `main` `589030ca` pins (`container/IMAGE`, fetched this session), as `tests/spike.sh` pins. One container of mine at a time, under `~/bin/capped-run`, `nice -n 10`, `--network none`.
**Evidence:** `~/.cache/tvi-1300c/` (`h/` the scripts, `cases/` the four suites' logs, `rep/main` and `rep/meas` the replays, `rm/` the rendered pages), kept until the architect says.

| Run | What | How |
|---|---|---|
| **M1** | The shadow: at each use of a kernel-clock frequency, one log line under no peripheral's name carrying four answers, and the work order's (2) and (3) verdicts on each PLL the source reaches. The four answers: the copy the guard uses today; the other two copies' arithmetic applied to the same source (marked "applied" where that copy has no such source); the RCC guard's `KernelHz` (gated, what the build would use); and its arithmetic ungated. A line is printed only when its payload changes, with the count of the last. | `h/Meas1300c.cs` and `h/instrument.py`, applied to a scratch export of `main` `189214b` (`src-meas`), never to the branch. The USART and ADC copies are ported verbatim and checked against the guard's own answer at every use (`PORT-MISMATCH`: never seen). The I2C use is wrapped at its one `KernelHz` call. |
| **C** | Every case of `tests/1300/rcc.sh` (68), `tests/1305/usart.sh` (38), `tests/1306/i2c.sh` (47) and `tests/1307/adc.sh` (46) | each suite's `SRC` argument on `src-meas` (`h/suites.sh`); `h/analyse.py` |
| **R** | The 14 generated boots, ArduPilot's fixture (5 s) and the armed run (13 s), on `main` and on `src-meas`, every guard recorded | `h/loop.sh`, `h/replay-m.sh` (the committed `tests/1301/replay-1301.sh` plus `1305a`'s `LOGP`/`RUNFOR`/`EXTRA` knobs) over `1305`'s captures (read-only). The armed run: `1305`'s capture with its injection (`EXTRA='include @/inj/inject.resc;'`), `ACCEPT_USART_DMA="usart1:41"` (p604) and `ACCEPT_EXTERNAL="PB6:up PB7:up"`, as `1305`/`1306a` ran it. `h/ident.py` compares the two trees' consoles with the shadow's lines removed; `h/mav.py` (`1305`'s, also reading DMA-written `TDR` bytes) reads the arming. |
| **RM** | RM0433 Rev 9 pp. 346–347, 401–404, 406–407 and 409–410; DS12117 Rev 10 Tables 39, 40, 136 and 137 | `tests/1300/rmpage.sh` on the held text; p346–347 rendered (`gs` in the image, `rm/p001.png`) for Figure 47's footnote, which the text layer garbles |
| **S** | The shard projection | `tools/tviburi-route plan` built from this tree, on `main`'s `times.tsv`, cases and mutants; then with this WO's projected items added at their family's median weight (`h/proj.sh`) |

**The shadow changes nothing:** all 16 replays (and the armed run without the receive DMA) are line-for-line identical on `main` and `src-meas` once the shadow's lines are removed. The armed run arms at 9.332 s on both, as `1306a` measured. 197 of the 199 cases passed on `src-meas`. The other two, `rcc-adc3` and `adc-overwrite`, lost their end records to the teardown race at load 12; each passed twice more on each tree.

## 2. Results

### 2.1 The three copies (the table)

| | USART guard's (`GuardedSTM32F7_USART.cs` L571–635) | ADC guard's (`GuardedSTM32H7_ADC.cs` L853–952) | RCC guard's `KernelHz` (`GuardedSTM32H7_RCC.cs` L228–306) |
|---|---|---|---|
| **Unit** | `ulong` Hz, each step truncated: `vco = in × (DIVN+1) / DIVM`, then `/ (DIVx+1)`, `/ D1CPRE / HPRE / D2PPREx` | `double` | `double` |
| **Gate** | none: from the selector register as written; called after the query says running (the start, `Clocked()` for a byte), **but also** at the receiver start in recorded mode after `TVI-USART-KERNEL` fired (L288) | none: `ADCSEL`/`CKPERSEL` as written; `State()` computes f_ADC for **every** ADC record, clock running or not | the query: enabled, mux settled, source running; else none with the query's condition |
| **Ports** | any USART name | `adcM1S2` (`adc12`), `adc3` | the `Clocked` table: `usart1`–`3`, `i2c1`–`2`, `spi1`–`2`, `rng`, `adc12`, `adc3` |
| **HSIDIV** | `RCC_CR` read through the RCC guard (the stock register) | the same | `Cr()` (the same register) |
| **`sys_ck`** | HSI (`SWS` 000) or PLL1's P (011); else none | the same | HSI, CSI, HSE or PLL1's P |
| **`D1CPRE`/`HPRE`/`D2PPREx`** | the stock registers, through the RCC guard | `D1CPRE`/`HPRE` only (for `rcc_hclk`; `adc_sclk` on rev V is `sys_ck` or `sys_ck`/2 by `HPRE`) | the stock registers |
| **PLL reference** | `PLLSRC` from the guard's copy: HSI, or HSE at `HseFrequency`; CSI none | the same | HSI, CSI or HSE |
| **`DIVMn`** | the guard's copy of `PLLCKSELR` | the same | the same |
| **`DIVNn`, `DIVPn`/`DIVQn`/`DIVRn`** | the **stock read-back** of `RCC_PLLnDIVR`; "no rule" once any write to it did not read back as written (`pllUntrusted`, PLL1–2), until a machine reset | the same, PLL1–3 | **`divr`**: the last write the guard passed (in recorded mode, a refused write too), never marked |
| **`VCOSEL`** | the last `PLLCFGR` the RCC guard passed (`Written`) | the same | the guard's copy of `PLLCFGR` (the same value) |
| **Sources** | `rcc_pclk1`/`2`, `pll2_q_ck`, `hsi_ker_ck` | `pll2_p_ck`, `pll3_r_ck`, `per_ck` (`CKPERSEL` as written), `sys_ck`/`rcc_hclk` (`CKMODE` ≠ 00) | every `pllN_x_ck`, `rcc_pclk1`/`2`, `hsi_ker_ck`, `csi_ker_ck`, `hse_ck`, `per_ck` (its settled mux); **no `sys_ck` or `rcc_hclk` answer** |
| **Ranges** | ref 1–16 MHz (integer `in / DIVM`), VCO by `VCOSEL` | ref 1–16 MHz, VCO by `VCOSEL` | the same, and `DIVN` ≥ 3, which is dead (ref ≤ 16 MHz × 3 < 150 MHz) |
| **"No rule" reasons** | selector `pll3_q_ck`, `csi_ker_ck`, `lse_ck`; `sys_ck` not HSI/PLL1; reference not HSI/HSE or `DIVM` = 0; `pllUntrusted`; out of range | `ADCSEL` 11; `per_ck` with `CKPERSEL` 11; `PRESC` > 1011; `sys_ck` not HSI/PLL1; reference not HSI/HSE; `pllUntrusted`; out of range | the query (not enabled, mux waiting, source stopped, peripheral not in `Clocked`); `SWS` > 011; HSE with no `hseFrequency`; `lse_ck`, `I2S_CKIN`; no reference or out of range |

**A source one copy refused and another computed** (the union's names): the CSI as a kernel clock, as `sys_ck` and as a PLL reference (RCC only); the HSE as `sys_ck` (RCC only); PLL3 (ADC and RCC); `per_ck` (ADC and RCC, by different inputs); a `PLLnDIVR` write the stock model did not keep (RCC only); the ports `usart6`, `uart4`, `uart5`, `uart7`, `uart8` (USART only); a clock that is not running (USART and ADC only, ungated).

### 2.2 Every disagreement, by case

**Frequencies the replays reach** (14 generated boots, the fixture, the armed run): **no disagreement.** Every use gives the same number from every copy that has the source, and the same frame time to the nanosecond:
- generated: `usart1`–`3` on `rcc_pclk1`/`2` at 64 MHz (from the HSI, or PLL1 on the HSE on `hse-crystal-16`, `hse-external-8`, `sentinel-02-bench-hse8`); the `i2c1` NACKs at 64 MHz; `d1-drv8876`'s ADC3 on `CKMODE` = 11, `adc_sclk`, 8 MHz;
- ArduPilot: `usart1`–`3` on `pll2_q_ck` at 72 MHz; ADC1 and ADC3 on `pll3_r_ck` (30 MHz, f_ADC 15 MHz on rev V); the `i2c1`/`i2c2` NACKs on `pll3_r_ck` at 30 MHz.
No use on a port outside `usart1`–`3`. No `PLLnDIVR` write the stock model did not keep. Every value is a whole number of Hz at every step, so the unit never shows.

**The committed cases:** 48 USART uses, 107 ADC uses, 12 I2C uses. Each disagreement, with what the build's single answer (the RCC guard's) would do:

| Case (WO) | Use | Today | RCC guard's | Changes in the case |
|---|---|---|---|---|
| `usart-idlenorule` (`1305a`) | `usart1` started on `csi_ker_ck` | USART: no rule → `TVI-USART-IDLE` | 4 MHz | the planted refusal disappears: **the case must be re-planted** |
| `usart-idlenone` (`1305a`), path 1 | a byte after the source moved to `csi_ker_ck` | no rule | 4 MHz (frame 500,000 ns) | one of its seven records goes |
| `usart-idlenone`, path 2 | `rcc_pclk2` with `sys_ck` on the CSI | no rule | 4 MHz | one record goes |
| `usart-idlenone`, path 7 | PLL1 after `DIVP1` = 2 (÷3), which the stock model ignored | no rule (`pllUntrusted`) | 86 MHz (258 / 3) | none, if (3)'s `DIVP1` rule lands; the record's reason text changes |
| `usart-idlenone`, path 4 | `pll2_q_ck` after `DIVN2` = 359, kept by the stock model as 103 | no rule (`pllUntrusted`) | 72 MHz, from the write (silicon's 9 bits hold 359) | by Q2 below |
| `usart-idlenone`, paths 3, 5, 6 | a CSI reference (125 kHz); a 6 MHz VCO; a 500 MHz VCO on VCOL | no rule | no rule | **reason text only**, which the case checks word for word |
| `i2c-nackpll` (`1306a`) | `i2c1` on `pll3_r_ck` after `DIVN3` = 811, kept by the stock model as 555 | ADC copy: no rule; **the I2C guard uses the RCC guard's: 30 MHz** | 30 MHz | **under (2) as written ("`pllUntrusted`'s rule, kept once"), this I2C case loses its NACK**: a `TVI-I2C-NACK` refusal where it now NACKs at 2.567 µs per SCL period. The I2C cases are fenced to pass unchanged. Q2. |
| `adc-kernel`, `-fields`, `-read`, `-recorded`, `-width` (`1307`) | `State()` in a record's `state`, with PLL2 off (`ADCSEL` 00, reset values) | f_ADC 64.5 MHz (16.125 MHz in `kernel`, `PRESC` /4) | none: PLL2 is not running | **each record's `state` reads `f_ADC none`.** It is truer (silicon has no clock there). Whether the cases check that text is for the build to see. Q4 |

No other use in any case disagrees. Every `ADCAL`, `ADEN`, `ADSTART` and rate-check use agrees, and so does every USART start and byte timed on a running clock.

### 2.3 What RM0433 says for (3), and who sets it otherwise

- **`PLLxRGE`** (pp. 401–402): 00 1–2 MHz, 01 2–4, 10 4–8, 11 8–16; "must be set according to the reference input frequency" (p347). The edges 2, 4 and 8 MHz sit in two ranges each; p347 says only "if the above rules are not respected, the PLL output frequency is not guaranteed".
- **The VCO choice** (p346, and Figure 47 rendered): VCOH takes "2 to 16 MHz" and gives 192–960 MHz; VCOL takes "1 to 2 MHz" and gives 150–420 MHz. `PLLxVCOSEL` = 1 routes the reference into VCOL's "1 to 2 MHz" input. p347: "if the frequency of the reference clock is lower or equal to 2 MHz, then VCOL must be selected, otherwise VCOH must be chosen". DS12117 Rev 10 agrees on the input: medium VCO `fPLL_IN` 1–2 MHz (Table 40 p131, Table 137 p235), wide 2–16 MHz. It gives the wide VCO's output as 192–836 MHz in Table 39 (p130) and 192–960 MHz in Table 136 (p234), a revision split that no guard models today (a row, not this WO).
- **`DIVP1`** (p404): "odd division factors are not allowed"; `0000010` "Not allowed"; but `0000000`: "pll1_p_ck = vco1_ck". Figure 47's footnote (p346, rendered): "DIVPx = **2**,4,6,…128 when x = 1, and DIVPx = **1**,2,3,…128 for x = 2 or 3". Two statements to one: `DIVP1` = 0 is not allowed.
- **`DIVR1`** = 0 is "Not allowed" (p403). No kernel-clock mux in the query takes `pll1_r_ck`, so it never reaches `KernelHz`: no rule is needed, and none is added.
- **`DIVN`** < 3: "wrong configurations" (p404, p407); already unreachable through the VCO range.

**Who sets them otherwise:**

| Firmware or case | PLL | ref | `RGE` | `VCOSEL` | Against the pages |
|---|---|---|---|---|---|
| **ArduPilot, fixture and armed run** (`PLLCFGR` = `0x01BF0FFD`, written once) | PLL2 (USARTs, 72 MHz) | 8 MHz | 11 | **1 (VCOL)** | **VCOL with an 8 MHz reference: against p346, p347 and DS12117.** The VCO (360 MHz) is inside VCOL's output range, so every copy computes today |
| | PLL3 (ADCs, I2C, 30 MHz) | 16 MHz | 11 | **1 (VCOL)** | the same, VCO 240 MHz |
| | PLL1 | 16 MHz | 11 | 0 | allowed; `DIVP1` = 1 (÷2) |
| Generated, 11 boards (HSI ÷ 5) | PLL1 | 12.8 MHz | 11 | 0 | allowed |
| `hse-crystal-16` | PLL1 | 3.2 MHz | 01 | 0 | allowed |
| `hse-external-8`, `sentinel-02-bench-hse8` | PLL1 | 1.6 MHz | 00 | 1 | allowed |
| `usart-idlekernel` (`1305a`) | PLL2 (ArduPilot's 72 MHz) | 8 MHz | **00 (reset)** | 0 | **`RGE` wrong under any reading**: the case writes `PLLCKSELR` and `PLL2DIVR` but leaves `PLLCFGR` at reset |
| 19 ADC uses (`1307`'s cases), `i2c-nackclock`, `i2c-nackpll`, `usart-idlenone` | PLL2 or PLL3 at reset `DIVM` = 32 | 2 MHz | 00 | 0 | allowed by p346 (VCOH from 2 MHz) and `RGE` 00 (1–2 MHz, inclusive); **forbidden by p347** (≤ 2 MHz must be VCOL) |
| `usart-idlenone` path 7 | PLL1 | 2 MHz | 00 | 0 | `DIVP1` = 2, not allowed (already no rule today) |

**`PLLCFGR` read-modify-write:** none. Every firmware writes `PLLCFGR` once. Its read-back loses `RGE`, `PLL1VCOSEL`, `PLL3VCOSEL` and `FRACEN` (stock tags; `PLL2VCOSEL` is stored), and no firmware writes that back.

### 2.4 Memory

Not measured: the build does not exist yet. Prediction P5 stands: the build compiles fewer lines (two copies, two subscriptions and their constants go; a `sys_ck`/`rcc_hclk` answer and the (3) checks come into the RCC guard), so it should land within ±5 MiB of `main` at the median. It will be measured on the branch with `tests/1301/ci-memory.sh`, eleven boots a side, before READY, and judged in CI.

### 2.5 The shard projection

`main`'s ten shards, by `tools/tviburi-route plan` on `main`'s `times.tsv`: 620 s each at the reference, so **15.8 min at 1.53** for the longest. With this WO's projected items (10 cases and 14 mutants at their families' medians, 14 USART rows retired), each shard holds 625 s: **15.9 min at 1.53**. No eleventh shard.

### 2.6 Against the predictions

| Prediction | Result |
|---|---|
| P1, the table | right in every row; three rows I did not predict: the USART's ungated recorded-mode start (L288), the `CKPERSEL` the ADC reads as written, and the ports |
| P2, the cases | right that the disagreements are planted (`idlenone`, `idlenorule`) and that the gate shows in ADC record `state` (`kernel`, `fields`, `read`, `recorded`, `width`). **Wrong** that `rccchange`/`rate2` disagree; **missed** `i2c-nackpll`, whose answer (2) would change |
| P3, the replays | right: no disagreement and no unkept `PLLnDIVR` write |
| P4, (3) | **wrong on ArduPilot:** I predicted it obeys `VCOSEL`; it sets VCOL on 8 and 16 MHz references. Right that the 2 MHz reset edge and an `RGE` left at reset (`idlekernel`) are the committed cases' exposure |
| P5, memory | not yet measured |
| P6, shards | right: 15.9 min at 1.53 with ten shards |

## 3. The stop: questions for the ruling

Each is a §7 condition or a choice the work order leaves open. My recommendation comes first.

- **Q1, ArduPilot's VCO choice (§7: "a committed firmware sets one").** ArduPilot puts PLL2 and PLL3 on VCOL (`VCOSEL` = 1) with 8 and 16 MHz references. A (3) rule on the VCO choice would make its USARTs, ADCs and I2C NACKs "no rule": `TVI-USART-IDLE`, `TVI-ADC-RATE` and `TVI-I2C-NACK` records on the fixture and the armed run, and in enforcing mode a refusal at the first byte.
  - **(a), recommended:** the VCO choice is checked by the VCO's output against `VCOSEL`'s range (today's rule, kept). The reference-input half (p346, p347, DS12117 Tables 40 and 137) becomes `acknowledged by name` with a bench row: ArduPilot's `PLLCFGR` on a NUCLEO-H753ZI, PLL2's Q output measured on MCO2.
  - (b): the input half is enforced as written. The armed run stops being a pass on every guard that times from PLL2 or PLL3.
  - A further point, from memory and not read: ChibiOS chooses `VCOSEL` by the VCO's output frequency, not its input.
- **Q2, the divider rule against a fenced I2C case (§7: "a committed case's computed time would change").** `i2c-nackpll` writes `DIVN3` = 811 once; the stock model keeps 555. Silicon holds 811, and the RCC guard computes from it (30 MHz). `usart-idlenone` path 4 is the same input on PLL2, and the USART copy answers "no rule" there.
  - **(B′), recommended:** the guard's `divr` (the write it passed) is silicon's value. "No rule" applies from the **next** `PLLnDIVR` write after one the stock model did not keep, until a machine reset, because that next write may carry the stock's narrowed bits back. That is `pllUntrusted`'s reason (`1305a` F2), applied where it bites. It also applies to a write passed in recorded mode after `TVI-RCC-PLL` fired, which silicon ignores. Changes: `idlenone` path 4 (no rule → 72 MHz; one record fewer); `nackpll` unchanged.
  - (A), (2) as written: "no rule" from the unkept write itself. Changes `i2c-nackpll`, outside the fence.
  - (C): B′, and the RCC guard serves `PLLnDIVR` reads from `divr`, so a read-modify-write is right. This is the roadmap's other half of the `1300a` row; it changes `rcc-divr2rmw` and is not in this work order.
- **Q3, the reset edge and `RGE`.**
  - **Recommended:** the edges are inclusive. A reference on an edge is allowed in either range it touches (2 MHz with `RGE` 00 or 01; 8 MHz with 10 or 11). `VCOSEL` = 0 at exactly 2 MHz is allowed by p346; p347's "≤ 2 MHz must be VCOL" is not enforced at its edge. Otherwise every committed case that runs a PLL from reset values (the ADC cases, `i2c-nackclock`, `i2c-nackpll`) would lose its clock: the reset state would be illegal.
  - **`usart-idlekernel` leaves `RGE` at 00 with an 8 MHz reference,** which no reading allows. I ask leave to add ArduPilot's own `PLLCFGR` (`0x01BF0FFD`) to that case's setup, before PLL2 starts. That is ArduPilot's value, so the case's computed time is unchanged. But under Q1 (b) it would not be.
- **Q4, the gate (the ADC `state` text).** With one rule, an ADC record's `state` says `f_ADC none` when the clock does not run.
  - **Recommended:** accept it: five `1307` cases' record text changes, and the build adapts any check that reads it.
  - The other option is an ungated answer for `State()` only, which would be a second rule.
- **Q5, `CKMODE` ≠ 00** (`d1-drv8876`, generated, reaches it).
  - **Recommended:** the RCC guard gains one public read-only answer for a named bus clock, `BusHz("sys_ck" | "rcc_hclk")`, beside `KernelHz`, from the same `SourceHz`. That is a second public method on the RCC guard, so it needs the ruling.
- **Q6, the union's two refusals:**
  - **`usart-idlenorule`** plants `csi_ker_ck` as "no rule"; the union computes it (4 MHz). Recommended: re-plant it on a source that stays "no rule": a VCO outside its range from planted firmware. Its record and page are unchanged.
  - **The ports outside the query** (`usart6`, `uart4/5/7/8`): their enables already refuse at the RCC (`Zero` only), so only a recorded run reaches them. No firmware or case does. Recommended: they answer "no rule" (the RCC guard's), and the query is not widened.
- **Q7, `DIVP1` = 0.** p404's table gives it a frequency; its note and Figure 47 forbid it. Recommended: not allowed, "no rule", citing both. No firmware sets it.
- **Q8, the fence:**
  - **(i)** The 14 mutant rows `US52`–`US59` and `US63`–`US68` test the USART copy, which this WO deletes. Recommended: retire them, with their rules' rows re-made on the RCC guard.
  - **(ii)** The four `LPEN` rows' ledger `detail` resolves to `TVI-WO-1300.rm0433.md` §8 A6, which does not list them. Making it true needs one line in that document, or a second reference form in `tools/tviburi-release`. Both are outside the fence. Recommended: one line appended to §8 A6 naming the four bits and `1300b`.
  - **(iii)** "In `fields.py`'s words" for the RCC rows: the RCC's generator is `tests/1300/allowlist.py`, and I read the WO as meaning it.
