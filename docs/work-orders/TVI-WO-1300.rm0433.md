# TVI-WO-1300 §1.1 — the RCC's field table, measured against both firmwares, and the kernel-clock sources by page

**Status:** measured 2026-11-23, at the §1.1 stop. **No model, tool, script or firmware code is changed on this branch yet.** `tests/1300/` holds the measurement harness (granted by the architect, 13:10Z). §6 lists the fields the architect is asked to acknowledge by name, or to rule refused, and the questions the rule raises.
**Branch head measured:** `cde1896` (`main`). Every page cited is ST RM0433 Rev 9 (sha256 `672a9433…`), chapter 8, unless it says otherwise. "Inferred" marks a reading the manual does not state in a sentence.

## 1. Sources and how each was measured

| What | Identity | How |
|---|---|---|
| RM0433 Rev 9 | the held PDF and its text, `~/.cache/aet-held/rm0433-rev9.{pdf,txt}` | Pages come from the text's footers: content after the footer `N/3329` is on page N+1. Each page used in §2 and §3 was checked by hand against that mapping (for example: the `RCC_D2CCIP1R` heading follows footer 412, so it is p413, as the table of contents says). Table 60 (pp355–356) is rotated in the text, so its column headers are lost; the selector tables of §8.7.19–8.7.22 are used instead |
| The stock RCC model | `STM32H7_RCC.cs`, sha256 `7f10d491…`, renode-infrastructure `066a7f13` (the pinned build's, as `1150` (i) and `0100b` §1 established) | Read line by line; its behaviours that the guard depends on were then measured on the pinned binary (§3) |
| The stock platform | `platforms/cpus/stm32h7.repl`, sha256 `b85cb4af…`, the same in the pinned image and in Aethelgard's pinned copy (`crates/aethelgard-twin/tests/renode-1.17.0/`) | The `rcc` entry (L513–514: `Miscellaneous.STM32H7_RCC @ sysbus 0x58024400`, `systick: nvic`) and its 58 registrations |
| Image and Renode | `ghcr.io/clearforge-llc/aethelgard-tools@sha256:1e0ab105…`, Renode `1.17.0+20260907gitf1dd1b4af.aet1605` | Aethelgard `main`'s `container/IMAGE` at `75da8053`. Every container ran under `~/bin/capped-run`, `nice -n 10`, one at a time; every run but the CLI build with `--network none` |
| Aethelgard | `main` at `75da8053`, exported with `git archive` | Its CLI and `aethelgard-bridge` built in the image into the worktree's untracked `target-aet/` |
| ArduPilot | the held ELF, `~/.cache/aet-4240v/ardupilot/arducopter.elf`, sha256 `a799dfb6…dab1ef` | `spike/1150/harness/capture-foreign.sh`, unchanged: `simulate --spec crates/aethelgard-cli/tests/fixtures/sentinel-02-own.json --foreign 5000ms`. Verdict `pass`; the emitted platform's sha256 `3927f28a…`, as in `1150` (v) |
| The generated firmware | 14 specifications | `spike/1100/harness/capture.sh`, unchanged, one specification per container: `1100`'s 13 plus `examples/sentinel-02-bench-hse8.json`, new since `1100` |

## 2. The kernel-clock rule's facts, by page

### 2.1 The selectors the query reads

| Peripheral | Bus-clock enable (page) | Selector (page) | Sources, by value |
|---|---|---|---|
| SPI1 | `RCC_APB2ENR.SPI1EN`, bit 12 (p469) | `RCC_D2CCIP1R.SPI123SEL[2:0]`, bits 14:12 (p414) | `000` `pll1_q_ck` (reset); `001` `pll2_p_ck`; `010` `pll3_p_ck`; `011` `I2S_CKIN` ("an external clock taken from a pin"); `100` `per_ck`; others "reserved, the kernel clock is disabled" |
| SPI2 | `RCC_APB1LENR.SPI2EN`, bit 14 (p464) | the same `SPI123SEL` mux | as SPI1 |
| I2C1, I2C2 | `RCC_APB1LENR.I2C1EN`, `I2C2EN`, bits 21, 22 (p463) | `RCC_D2CCIP2R.I2C123SEL[1:0]`, bits 13:12 (p416) | `00` `rcc_pclk1` (reset); `01` `pll3_r_ck`; `10` `hsi_ker_ck`; `11` `csi_ker_ck` |
| USART1 | `RCC_APB2ENR.USART1EN`, bit 4 (p470) | `RCC_D2CCIP2R.USART16SEL[2:0]`, bits 5:3 (p417) | `000` `rcc_pclk2` (reset); `001` `pll2_q_ck`; `010` `pll3_q_ck`; `011` `hsi_ker_ck`; `100` `csi_ker_ck`; `101` `lse_ck`; others reserved, disabled |
| RNG | `RCC_AHB2ENR.RNGEN`, bit 6 (p457) | `RCC_D2CCIP2R.RNGSEL[1:0]`, bits 9:8 (p417) | `00` printed "hsi8_ck", **a misprint for `hsi48_ck`**: Figure 58 (p364) shows `hsi48_ck` on RNGSEL's input 0; `01` `pll1_q_ck`; `10` `lse_ck`; `11` `lsi_ck` |
| `per_ck` | — | `RCC_D1CCIPR.CKPERSEL[1:0]`, bits 29:28 (p412) | `00` `hsi_ker_ck` (reset); `01` `csi_ker_ck`; `10` `hse_ck`; `11` "reserved, the per_ck clock is disabled" |

Each enable bit's text names its selector ("the kernel clock selected by …", p463, p464, p469, p470, p457; SPI1EN's text says `I2S123SRC`, the old name of `SPI123SEL`). The same `PERxEN` bit gates the bus and the kernel clock: "Kernel and bus interface clocks are provided to the peripheral, because the CPU is in CRun, and PERxEN='1'"; with `PERxEN` = 0, "No clock provided to the peripheral" (Table 62, p376; summary p377).

### 2.2 When each source runs

| Source | Runs when | Pages |
|---|---|---|
| `hsi_ker_ck` | `RCC_CR.HSION` = 1. **Inferred:** the manual has no sentence defining `hsi_ker_ck`'s availability. Figure 45 (p338) draws `hsi_ker_ck` from the HSI beside `hsi_ck`; p353 says the HSI and CSI "provide two clock paths … one path for the peripheral kernel clock (hsi_ker_ck or csi_ker_ck)". `HSIKERON` keeps the HSI on in Stop mode (p386), which the twin does not model | p386 (HSION), p338, p353 |
| `csi_ker_ck` | `RCC_CR.CSION` = 1 (inferred, as `hsi_ker_ck`). `CSIRDY` "is not activated if the CSI is enabled by CSIKERON or by a peripheral request" (p385) | p385, p338, p353 |
| `hse_ck` | `RCC_CR.HSEON` = 1; "At startup, the hse_ck clock is not released until this bit [HSERDY] is set" (p340) | p384, p340 |
| `hsi48_ck` | `RCC_CR.HSI48ON` = 1 | p384, p343 |
| `pllN_x_ck` | `RCC_CR.PLLNON` = 1; `RCC_PLLCFGR.DIVxNEN` = 1 ("0: pllN_x_ck output is disabled", p400–401); the PLL's reference runs: `RCC_PLLCKSELR.PLLSRC` `00` `hsi_ck`, `01` `csi_ck`, `10` `hse_ck`, `11` "No clock send to DIVMx divider and PLLs" (p399), and `DIVMN` ≠ 0 ("000000: prescaler disabled", p398–399). A PLL is "ready (i.e. locked)" at `PLLxRDY` (p347); that its outputs are not valid before it is **inferred** (p348–349, Figure 48) | p383, p398–401, p347–349 |
| `rcc_pclk1`, `rcc_pclk2` | the bus clocks, gated by the peripheral's own `PERxEN` (Table 62 p376) | p376–377 |
| `per_ck` | `CKPERSEL`'s mux has settled (below) on a running source | p412, p356 (Table 60 note 2) |

### 2.3 The mux's current input (ruled part of the rule, architect 13:10Z)

> "**Clock switching state after system reset.** After system reset, PLLs, HSE, CSI and HSI48 are switched OFF. If one of these clocks is used as input clock through the MUX, the switching does not take place. Indeed, switching from the original to the new input can only be performed if a clock is present on both inputs. Otherwise, no clock is provided to the peripheral. To recover from this situation, the user has to provide a valid clock to both inputs. For example if pll2_q_ck is selected as kernel clock to the USART1, after system reset, the user has to enable pll2_q_ck to switch back to rcc_pclk2 as kernel clock to USART1." (§8.5.10, p371–372)

So the guard models each mux's **current input**, beside the selector's value: at power-on the input is the reset value; it changes to the selector's value only when the old and the new input both run; while they differ, no clock reaches the peripheral; and a system reset (`machine Reset`) returns the selector to its reset value without moving the input. The CCIPR footnote agrees (p413: "the user has to make sure that both the previous and the new clock sources are present during the switching").

### 2.4 Writes silicon ignores while a PLL runs

- `PLLSRC`: "These bits can be written only when all PLLs are disabled" (p399).
- `DIVMx`: "The hardware does not allow any modification of this prescaler when PLLx is enabled" (p398–399).
- `DIVPxEN`, `DIVQxEN`, `DIVRxEN`: "This bit can be written only when the PLLx is disabled (PLLxON = '0' and PLLxRDY = '0')" (p400–401).
- §8.5.5 (p347) lists `DIVNx`, `PLLxRGE`, `PLLxVCOSEL`, `DIVPx`, `DIVQx`, `DIVRx` and the `DIVxEN` bits as unchangeable once the PLL is enabled.

The query reads the first three, so a change to one of them while its PLL runs would make the guard's copy wrong. It is proposed as `TVI-RCC-PLL`, `predicted-fault`. The stock model already keeps `DIVN1`, `DIVQ1`, `DIVR1` and `DIVP1` while PLL1 is on (`STM32H7_RCC.cs`, the `PLL1DividersConfiguration` callbacks).

### 2.5 The alias registers (ruled, 13:10Z)

"The control of PERxEN and PERxLPEN bits can be performed at two different address offset: 0x0D0 and 0x130 … RCC_xxxxENR or RCC_C1_xxxxENR to control the PERxEN bits" (§8.7.1, p382; Figure 64 maps both to one physical register). The guard serves 0x130–0x17C as 0x0D0–0x11C.

## 3. The stock model, measured on the pinned binary

The wrapper prototype (`tests/1300/RccProto1300.cs`) on the Nucleo board, under monitor writes and reads (`tests/1300/smoke.resc`):

| What | Measured | Silicon (RM0433 Rev 9) |
|---|---|---|
| The wrapper as `rcc` with the 58 restated | loads; `peripherals` lists `rcc (RccProto1300)`; the owned stock RCC holds **59** registrations (58 plus the board's `spi1`) | — |
| `RCC_CR` at reset | `0x25` (`HSIRDY` and `HSIDIVF` computed) | `0x0000 0001` with `HSIRDY` set by hardware (p383) |
| A tag written | `PLLCFGR` written `0x0` **reads `0x01FF0000`**: tags read their reset bits, not 0. `architecture.md` §2.0a's "Stock tags discard writes and read 0" holds only for tags whose reset is 0 | — |
| `RCC_D2CCIP1R` written `0x4000` | reads `0x0`: every selector is dropped (`architecture.md` §2.0) | reads `0x4000` |
| `PLLSRC` = `11` ("no clock"), then `PLL1ON` | `RCC_CR` reads `0x03000025`: **`PLL1RDY` = 1 with no reference** | `PLL1RDY` = 1 means locked (p347) |
| `*ENR` at reset | `APB2ENR` reads `0x00073033`: each bit reads whether its registered peripheral is enabled on the bus, which is 1 from reset | `0x0000 0000` (§8.7.47 p468) |
| `*ENR` written | `APB2ENR` = 0, then `0x1000`, read back `0x0` then `0x1000`; a write to `spi1` while disabled is "Tried to write a locked peripheral" | — |
| `RCC_C1_APB2ENR` (0x150) written `0x1001` | the base register stays `0x1000` (TIM1 not enabled); the alias reads the base | one register (p382) |
| `RCC_C1_APB4LPENR` (0x17C) read | **stack overflow; Renode aborts (exit 134)**: the stock alias reads itself (`STM32H7_RCC.cs` L1238) | reads `RCC_APB4LPENR` |
| `TIM1RST` through the wrapper | `timer1.PSC` 5, then 0 after `APB2RSTR` 1, 0 (§8.7.35 p442) | resets TIM1 |
| A hook, then access logging, on the guard | both fire, and the write still reaches the wrapper | — |
| Access logging on the guard | `rcc: WriteUInt32 to 0x54 (unknown), value 0x0`: **the register name is lost** (the stock model's log names it) | — |
| A hook on `spi1` | sees `machine` and `self` (the SPI guard), so a test can ask the RCC guard at an SPI write | — |

## 4. The field table

Sources, as §1: **ArduPilot** is the full 5 s boot (26,021 RCC writes, all 32-bit; 39,005 reads, all 32-bit). **Cold writes** are the consumer's monitor writes (`pc: none`), the same nine `*ENR` values for both firmwares. **Generated firmware** is the 14 boards captured at `75da8053`. The 13 that 1100 also ran have RCC writes byte-identical to its rows (Aethelgard `f42a0c88`). Every RCC write and read of the generated firmware is 32-bit. Each boot's boot block, with the trace's own lines removed, is byte-identical to the same boot without the wrapper (15 of 15).

**Class:** `guard`, modeled by the guard (the query, or its copy); `guard-copy`, a selector the guard stores and serves on read, whose peripherals the query does not cover; `inherited`, the stock model's (unaudited, northstar invariant 2); `tag` or `reserved`, dropped by the stock model, which behaves as the field's reset value; `read-only`, ignored by silicon and by the model; `kernel-unmodeled`, the enable of a kernel-clocked peripheral the query does not cover (WO (4)). **Allowed** is the allow-list as ruled (§8): the table the guard holds. **outside** marks a value given that it does not allow: after the rulings, only `ADC3EN` on `d1-drv8876` (F8, ruled `unmodeled`). The classes `guard`, `inherited` and `acknowledged` are written to the ledger as "modeled by the guard", "inherited, unaudited (acknowledged by name)" and "acknowledged by name"; a `kernel-unmodeled` enable as inherited with only 0 allowed.
| Register | Offset | Field | Bits | Page | Class | Allowed | ArduPilot | Cold writes | Generated firmware |
|---|---|---|---|---|---|---|---|---|---|
| `RCC_CR` | `0x000` | `CSIKERON` | 9:9 | 385 | tag | 0x0 | `0x0` | — | `0x0` (hse-crystal-16, hse-external-8, sentinel-02-bench-hse8) |
| `RCC_CR` | `0x000` | `CSION` | 7:7 | 385 | inherited | any | `0x0` | — | `0x0` (hse-crystal-16, hse-external-8, sentinel-02-bench-hse8) |
| `RCC_CR` | `0x000` | `CSIRDY` | 8:8 | 385 | read-only | any | `0x0` | — | `0x0` (hse-crystal-16, hse-external-8, sentinel-02-bench-hse8) |
| `RCC_CR` | `0x000` | `D1CKRDY` | 14:14 | 384 | read-only | any | `0x0` | — | `0x0` (hse-crystal-16, hse-external-8, sentinel-02-bench-hse8) |
| `RCC_CR` | `0x000` | `D2CKRDY` | 15:15 | 384 | read-only | any | `0x0` | — | `0x0` (hse-crystal-16, hse-external-8, sentinel-02-bench-hse8) |
| `RCC_CR` | `0x000` | `HSEBYP` | 18:18 | 384 | acknowledged | any | `0x0` | — | `0x0` (hse-crystal-16); `0x1` (hse-external-8, sentinel-02-bench-hse8) |
| `RCC_CR` | `0x000` | `HSECSSON` | 19:19 | 384 | inherited | any | `0x0` | — | `0x0` (hse-crystal-16, hse-external-8, sentinel-02-bench-hse8); `0x1` (hse-crystal-16, hse-external-8, sentinel-02-bench-hse8) |
| `RCC_CR` | `0x000` | `HSEON` | 16:16 | 384 | inherited | any | `0x0` | — | `0x0` (hse-external-8, sentinel-02-bench-hse8); `0x1` (hse-crystal-16, hse-external-8, sentinel-02-bench-hse8) |
| `RCC_CR` | `0x000` | `HSERDY` | 17:17 | 384 | read-only | any | `0x0` | — | `0x0` (hse-crystal-16, hse-external-8, sentinel-02-bench-hse8) |
| `RCC_CR` | `0x000` | `HSI48ON` | 12:12 | 384 | inherited | any | `0x0`, `0x1` | — | `0x0` (hse-crystal-16, hse-external-8, sentinel-02-bench-hse8) |
| `RCC_CR` | `0x000` | `HSI48RDY` | 13:13 | 384 | read-only | any | `0x0`, `0x1` | — | `0x0` (hse-crystal-16, hse-external-8, sentinel-02-bench-hse8) |
| `RCC_CR` | `0x000` | `HSIDIVF` | 5:5 | 385 | read-only | any | `0x0`, `0x1` | — | `0x0` (hse-crystal-16, hse-external-8, sentinel-02-bench-hse8) |
| `RCC_CR` | `0x000` | `HSIDIV[1:0]` | 4:3 | 385 | inherited | any | `0x0` | — | `0x0` (hse-crystal-16, hse-external-8, sentinel-02-bench-hse8) |
| `RCC_CR` | `0x000` | `HSIKERON` | 1:1 | 386 | tag | 0x0 | `0x0` | — | `0x0` (hse-crystal-16, hse-external-8, sentinel-02-bench-hse8) |
| `RCC_CR` | `0x000` | `HSION` | 0:0 | 386 | inherited | any | `0x1` | — | `0x1` (hse-crystal-16, hse-external-8, sentinel-02-bench-hse8) |
| `RCC_CR` | `0x000` | `HSIRDY` | 2:2 | 386 | read-only | any | `0x0`, `0x1` | — | `0x0` (hse-crystal-16, hse-external-8, sentinel-02-bench-hse8) |
| `RCC_CR` | `0x000` | `PLL1ON` | 24:24 | 383 | inherited | any | `0x0`, `0x1` | — | `0x0` (hse-crystal-16, hse-external-8, sentinel-02-bench-hse8); `0x1` (hse-crystal-16, hse-external-8, sentinel-02-bench-hse8) |
| `RCC_CR` | `0x000` | `PLL1RDY` | 25:25 | 383 | read-only | any | `0x0` | — | `0x0` (hse-crystal-16, hse-external-8, sentinel-02-bench-hse8) |
| `RCC_CR` | `0x000` | `PLL2ON` | 26:26 | 383 | inherited | any | `0x0`, `0x1` | — | `0x0` (hse-crystal-16, hse-external-8, sentinel-02-bench-hse8) |
| `RCC_CR` | `0x000` | `PLL2RDY` | 27:27 | 383 | read-only | any | `0x0` | — | `0x0` (hse-crystal-16, hse-external-8, sentinel-02-bench-hse8) |
| `RCC_CR` | `0x000` | `PLL3ON` | 28:28 | 383 | inherited | any | `0x0`, `0x1` | — | `0x0` (hse-crystal-16, hse-external-8, sentinel-02-bench-hse8) |
| `RCC_CR` | `0x000` | `PLL3RDY` | 29:29 | 383 | read-only | any | `0x0` | — | `0x0` (hse-crystal-16, hse-external-8, sentinel-02-bench-hse8) |
| `RCC_CR` | `0x000` | `Reserved` | 31:30 | 383 | reserved | 0x0 | `0x0` | — | `0x0` (hse-crystal-16, hse-external-8, sentinel-02-bench-hse8) |
| `RCC_CR` | `0x000` | `Reserved` | 23:20 | 383 | reserved | 0x0 | `0x0` | — | `0x0` (hse-crystal-16, hse-external-8, sentinel-02-bench-hse8) |
| `RCC_CR` | `0x000` | `Reserved` | 11:10 | 384 | reserved | 0x0 | `0x0` | — | `0x0` (hse-crystal-16, hse-external-8, sentinel-02-bench-hse8) |
| `RCC_CR` | `0x000` | `Reserved` | 6:6 | 385 | reserved | 0x0 | `0x0` | — | `0x0` (hse-crystal-16, hse-external-8, sentinel-02-bench-hse8) |
| `RCC_HSICFGR` | `0x004` | `HSICAL[11:0]` | 11:0 | 388 | read-only | any | `0x0` | — | — |
| `RCC_HSICFGR` | `0x004` | `HSITRIM[6:0]` | 30:24 | 388 | tag | 0x40 | `0x40` | — | — |
| `RCC_HSICFGR` | `0x004` | `Reserved` | 31:31 | 388 | reserved | 0x0 | `0x0` | — | — |
| `RCC_HSICFGR` | `0x004` | `Reserved` | 23:12 | 388 | reserved | 0x0 | `0x0` | — | — |
| `RCC_CFGR` | `0x010` | `HRTIMSEL` | 14:14 | 392 | tag | 0x0 | `0x0` | — | `0x0` (hse-crystal-16, hse-external-8, sentinel-02-bench-hse8) |
| `RCC_CFGR` | `0x010` | `MCO1PRE[3:0]` | 21:18 | 392 | acknowledged | any | `0x0`, `0x4` | — | `0x0` (hse-crystal-16, hse-external-8, sentinel-02-bench-hse8) |
| `RCC_CFGR` | `0x010` | `MCO1[2:0]` | 24:22 | 391 | tag | 0x0 | `0x0` | — | `0x0` (hse-crystal-16, hse-external-8, sentinel-02-bench-hse8) |
| `RCC_CFGR` | `0x010` | `MCO2PRE[3:0]` | 28:25 | 391 | acknowledged | any | `0x0`, `0x4` | — | `0x0` (hse-crystal-16, hse-external-8, sentinel-02-bench-hse8) |
| `RCC_CFGR` | `0x010` | `MCO2[2:0]` | 31:29 | 391 | tag | 0x0 | `0x0` | — | `0x0` (hse-crystal-16, hse-external-8, sentinel-02-bench-hse8) |
| `RCC_CFGR` | `0x010` | `RTCPRE[5:0]` | 13:8 | 392 | acknowledged | any | `0x0`, `0x8` | — | `0x0` (hse-crystal-16, hse-external-8, sentinel-02-bench-hse8) |
| `RCC_CFGR` | `0x010` | `Reserved` | 17:16 | 392 | reserved | 0x0 | `0x0` | — | `0x0` (hse-crystal-16, hse-external-8, sentinel-02-bench-hse8) |
| `RCC_CFGR` | `0x010` | `STOPKERWUCK` | 7:7 | 392 | tag | 0x0 | `0x0` | — | `0x0` (hse-crystal-16, hse-external-8, sentinel-02-bench-hse8) |
| `RCC_CFGR` | `0x010` | `STOPWUCK` | 6:6 | 393 | tag | 0x0 | `0x0` | — | `0x0` (hse-crystal-16, hse-external-8, sentinel-02-bench-hse8) |
| `RCC_CFGR` | `0x010` | `SWS[2:0]` | 5:3 | 393 | read-only | any | `0x0` | — | `0x0` (hse-crystal-16, hse-external-8, sentinel-02-bench-hse8) |
| `RCC_CFGR` | `0x010` | `SW[2:0]` | 2:0 | 393 | inherited | any | `0x0`, `0x3` | — | `0x3` (hse-crystal-16, hse-external-8, sentinel-02-bench-hse8) |
| `RCC_CFGR` | `0x010` | `TIMPRE` | 15:15 | 392 | inherited | any | `0x0`, `0x1` | — | `0x0` (hse-crystal-16, hse-external-8, sentinel-02-bench-hse8) |
| `RCC_D1CFGR` | `0x018` | `D1CPRE[3:0]` | 11:8 | 394 | inherited | any | `0x0` | — | — |
| `RCC_D1CFGR` | `0x018` | `D1PPRE[2:0]` | 6:4 | 395 | inherited | any | `0x4` | — | — |
| `RCC_D1CFGR` | `0x018` | `HPRE[3:0]` | 3:0 | 395 | inherited | any | `0x8` | — | — |
| `RCC_D1CFGR` | `0x018` | `Reserved` | 31:12 | 394 | reserved | 0x0 | `0x0` | — | — |
| `RCC_D1CFGR` | `0x018` | `Reserved` | 7:7 | 395 | reserved | 0x0 | `0x0` | — | — |
| `RCC_D2CFGR` | `0x01C` | `D2PPRE1[2:0]` | 6:4 | 396 | inherited | any | `0x4` | — | — |
| `RCC_D2CFGR` | `0x01C` | `D2PPRE2[2:0]` | 10:8 | 396 | inherited | any | `0x4` | — | — |
| `RCC_D2CFGR` | `0x01C` | `Reserved` | 31:11 | 396 | reserved | 0x0 | `0x0` | — | — |
| `RCC_D2CFGR` | `0x01C` | `Reserved` | 7:7 | 396 | reserved | 0x0 | `0x0` | — | — |
| `RCC_D2CFGR` | `0x01C` | `Reserved` | 3:0 | 396 | reserved | 0x0 | `0x0` | — | — |
| `RCC_D3CFGR` | `0x020` | `D3PPRE[2:0]` | 6:4 | 397 | inherited | any | `0x4` | — | — |
| `RCC_D3CFGR` | `0x020` | `Reserved` | 31:7 | 397 | reserved | 0x0 | `0x0` | — | — |
| `RCC_D3CFGR` | `0x020` | `Reserved` | 3:0 | 397 | reserved | 0x0 | `0x0` | — | — |
| `RCC_PLLCKSELR` | `0x028` | `DIVM1[5:0]` | 9:4 | 399 | guard | any | `0x4` | — | `0x5` (hse-crystal-16, hse-external-8, sentinel-02-bench-hse8) |
| `RCC_PLLCKSELR` | `0x028` | `DIVM2[5:0]` | 17:12 | 398 | guard | any | `0x8` | — | `0x0` (hse-crystal-16, hse-external-8, sentinel-02-bench-hse8) |
| `RCC_PLLCKSELR` | `0x028` | `DIVM3[5:0]` | 25:20 | 398 | guard | any | `0x4` | — | `0x0` (hse-crystal-16, hse-external-8, sentinel-02-bench-hse8) |
| `RCC_PLLCKSELR` | `0x028` | `PLLSRC[1:0]` | 1:0 | 399 | guard | any | `0x0` | — | `0x2` (hse-crystal-16, hse-external-8, sentinel-02-bench-hse8) |
| `RCC_PLLCKSELR` | `0x028` | `Reserved` | 31:26 | 398 | reserved | 0x0 | `0x0` | — | `0x0` (hse-crystal-16, hse-external-8, sentinel-02-bench-hse8) |
| `RCC_PLLCKSELR` | `0x028` | `Reserved` | 19:18 | 398 | reserved | 0x0 | `0x0` | — | `0x0` (hse-crystal-16, hse-external-8, sentinel-02-bench-hse8) |
| `RCC_PLLCKSELR` | `0x028` | `Reserved` | 11:10 | 398 | reserved | 0x0 | `0x0` | — | `0x0` (hse-crystal-16, hse-external-8, sentinel-02-bench-hse8) |
| `RCC_PLLCKSELR` | `0x028` | `Reserved` | 3:2 | 399 | reserved | 0x0 | `0x0` | — | `0x0` (hse-crystal-16, hse-external-8, sentinel-02-bench-hse8) |
| `RCC_PLLCFGR` | `0x02C` | `DIVP1EN` | 16:16 | 401 | guard | any | `0x1` | — | `0x1` (hse-crystal-16, hse-external-8, sentinel-02-bench-hse8) |
| `RCC_PLLCFGR` | `0x02C` | `DIVP2EN` | 19:19 | 401 | guard | any | `0x1` | — | `0x0` (hse-crystal-16, hse-external-8, sentinel-02-bench-hse8) |
| `RCC_PLLCFGR` | `0x02C` | `DIVP3EN` | 22:22 | 400 | guard | any | `0x0`, `0x1` | — | `0x0` (hse-crystal-16, hse-external-8, sentinel-02-bench-hse8) |
| `RCC_PLLCFGR` | `0x02C` | `DIVQ1EN` | 17:17 | 401 | guard | any | `0x1` | — | `0x1` (hse-crystal-16, hse-external-8, sentinel-02-bench-hse8) |
| `RCC_PLLCFGR` | `0x02C` | `DIVQ2EN` | 20:20 | 400 | guard | any | `0x1` | — | `0x0` (hse-crystal-16, hse-external-8, sentinel-02-bench-hse8) |
| `RCC_PLLCFGR` | `0x02C` | `DIVQ3EN` | 23:23 | 400 | guard | any | `0x1` | — | `0x0` (hse-crystal-16, hse-external-8, sentinel-02-bench-hse8) |
| `RCC_PLLCFGR` | `0x02C` | `DIVR1EN` | 18:18 | 401 | guard | any | `0x1` | — | `0x0` (hse-crystal-16, hse-external-8, sentinel-02-bench-hse8) |
| `RCC_PLLCFGR` | `0x02C` | `DIVR2EN` | 21:21 | 400 | guard | any | `0x1` | — | `0x0` (hse-crystal-16, hse-external-8, sentinel-02-bench-hse8) |
| `RCC_PLLCFGR` | `0x02C` | `DIVR3EN` | 24:24 | 400 | guard | any | `0x1` | — | `0x0` (hse-crystal-16, hse-external-8, sentinel-02-bench-hse8) |
| `RCC_PLLCFGR` | `0x02C` | `PLL1FRACEN` | 0:0 | 402 | acknowledged | any | `0x0`, `0x1` | — | `0x0` (hse-crystal-16, hse-external-8, sentinel-02-bench-hse8) |
| `RCC_PLLCFGR` | `0x02C` | `PLL1RGE[1:0]` | 3:2 | 402 | acknowledged | any | `0x0`, `0x3` | — | `0x0` (hse-external-8, sentinel-02-bench-hse8); `0x1` (hse-crystal-16) |
| `RCC_PLLCFGR` | `0x02C` | `PLL1VCOSEL` | 1:1 | 402 | acknowledged | any | `0x0` | — | `0x0` (hse-crystal-16); `0x1` (hse-external-8, sentinel-02-bench-hse8) |
| `RCC_PLLCFGR` | `0x02C` | `PLL2FRACEN` | 4:4 | 402 | acknowledged | any | `0x0`, `0x1` | — | `0x0` (hse-crystal-16, hse-external-8, sentinel-02-bench-hse8) |
| `RCC_PLLCFGR` | `0x02C` | `PLL2RGE[1:0]` | 7:6 | 402 | acknowledged | any | `0x0`, `0x3` | — | `0x0` (hse-crystal-16, hse-external-8, sentinel-02-bench-hse8) |
| `RCC_PLLCFGR` | `0x02C` | `PLL2VCOSEL` | 5:5 | 402 | inherited | any | `0x0`, `0x1` | — | `0x0` (hse-crystal-16, hse-external-8, sentinel-02-bench-hse8) |
| `RCC_PLLCFGR` | `0x02C` | `PLL3FRACEN` | 8:8 | 401 | acknowledged | any | `0x0`, `0x1` | — | `0x0` (hse-crystal-16, hse-external-8, sentinel-02-bench-hse8) |
| `RCC_PLLCFGR` | `0x02C` | `PLL3RGE[1:0]` | 11:10 | 401 | acknowledged | any | `0x0`, `0x3` | — | `0x0` (hse-crystal-16, hse-external-8, sentinel-02-bench-hse8) |
| `RCC_PLLCFGR` | `0x02C` | `PLL3VCOSEL` | 9:9 | 401 | acknowledged | any | `0x0`, `0x1` | — | `0x0` (hse-crystal-16, hse-external-8, sentinel-02-bench-hse8) |
| `RCC_PLLCFGR` | `0x02C` | `Reserved` | 31:25 | 400 | reserved | 0x0 | `0x0` | — | `0x0` (hse-crystal-16, hse-external-8, sentinel-02-bench-hse8) |
| `RCC_PLLCFGR` | `0x02C` | `Reserved` | 15:12 | 401 | reserved | 0x0 | `0x0` | — | `0x0` (hse-crystal-16, hse-external-8, sentinel-02-bench-hse8) |
| `RCC_PLL1DIVR` | `0x030` | `DIVN1[8:0]` | 8:0 | 404 | inherited | any | `0x31` | — | `0x4f` (hse-crystal-16); `0x9f` (hse-external-8, sentinel-02-bench-hse8) |
| `RCC_PLL1DIVR` | `0x030` | `DIVP1[6:0]` | 15:9 | 404 | inherited | any | `0x1` | — | `0x3` (hse-crystal-16, hse-external-8, sentinel-02-bench-hse8) |
| `RCC_PLL1DIVR` | `0x030` | `DIVQ1[6:0]` | 22:16 | 404 | inherited | any | `0x9` | — | `0x3` (hse-crystal-16, hse-external-8, sentinel-02-bench-hse8) |
| `RCC_PLL1DIVR` | `0x030` | `DIVR1[6:0]` | 30:24 | 403 | inherited | any | `0x1` | — | `0x1` (hse-crystal-16, hse-external-8, sentinel-02-bench-hse8) |
| `RCC_PLL1DIVR` | `0x030` | `Reserved` | 31:31 | 403 | reserved | 0x0 | `0x0` | — | `0x0` (hse-crystal-16, hse-external-8, sentinel-02-bench-hse8) |
| `RCC_PLL1DIVR` | `0x030` | `Reserved` | 23:23 | 403 | reserved | 0x0 | `0x0` | — | `0x0` (hse-crystal-16, hse-external-8, sentinel-02-bench-hse8) |
| `RCC_PLL1FRACR` | `0x034` | `FRACN1[12:0]` | 15:3 | 405 | tag | 0x0 | `0x0` | — | — |
| `RCC_PLL1FRACR` | `0x034` | `Reserved` | 31:16 | 405 | reserved | 0x0 | `0x0` | — | — |
| `RCC_PLL1FRACR` | `0x034` | `Reserved` | 2:0 | 405 | reserved | 0x0 | `0x0` | — | — |
| `RCC_PLL2DIVR` | `0x038` | `DIVN2[8:0]` | 8:0 | 407 | inherited | any | `0x2c` | — | — |
| `RCC_PLL2DIVR` | `0x038` | `DIVP2[6:0]` | 15:9 | 407 | inherited | any | `0x1` | — | — |
| `RCC_PLL2DIVR` | `0x038` | `DIVQ2[6:0]` | 22:16 | 407 | inherited | any | `0x4` | — | — |
| `RCC_PLL2DIVR` | `0x038` | `DIVR2[6:0]` | 30:24 | 406 | inherited | any | `0x7` | — | — |
| `RCC_PLL2DIVR` | `0x038` | `Reserved` | 31:31 | 406 | reserved | 0x0 | `0x0` | — | — |
| `RCC_PLL2DIVR` | `0x038` | `Reserved` | 23:23 | 406 | reserved | 0x0 | `0x0` | — | — |
| `RCC_PLL2FRACR` | `0x03C` | `FRACN2[12:0]` | 15:3 | 408 | tag | 0x0 | `0x0` | — | — |
| `RCC_PLL2FRACR` | `0x03C` | `Reserved` | 31:16 | 408 | reserved | 0x0 | `0x0` | — | — |
| `RCC_PLL2FRACR` | `0x03C` | `Reserved` | 2:0 | 408 | reserved | 0x0 | `0x0` | — | — |
| `RCC_PLL3DIVR` | `0x040` | `DIVN3[7:0]` | 8:0 | 410 | inherited | any | `0xe` | — | — |
| `RCC_PLL3DIVR` | `0x040` | `DIVP3[6:0]` | 15:9 | 410 | inherited | any | `0x7` | — | — |
| `RCC_PLL3DIVR` | `0x040` | `DIVQ3[6:0]` | 22:16 | 410 | inherited | any | `0x4` | — | — |
| `RCC_PLL3DIVR` | `0x040` | `DIVR3[6:0]` | 30:24 | 409 | inherited | any | `0x7` | — | — |
| `RCC_PLL3DIVR` | `0x040` | `Reserved` | 31:31 | 409 | reserved | 0x0 | `0x0` | — | — |
| `RCC_PLL3DIVR` | `0x040` | `Reserved` | 23:23 | 409 | reserved | 0x0 | `0x0` | — | — |
| `RCC_PLL3FRACR` | `0x044` | `FRACN3[12:0]` | 15:3 | 411 | tag | 0x0 | `0x0` | — | — |
| `RCC_PLL3FRACR` | `0x044` | `Reserved` | 31:16 | 411 | reserved | 0x0 | `0x0` | — | — |
| `RCC_PLL3FRACR` | `0x044` | `Reserved` | 2:0 | 411 | reserved | 0x0 | `0x0` | — | — |
| `RCC_D1CCIPR` | `0x04C` | `CKPERSEL[1:0]` | 29:28 | 412 | guard | 0x0,0x1,0x2,0x3 | `0x0` | — | — |
| `RCC_D1CCIPR` | `0x04C` | `FMCSEL[1:0]` | 1:0 | 412 | guard-copy | any | `0x0` | — | — |
| `RCC_D1CCIPR` | `0x04C` | `QSPISEL[1:0]` | 5:4 | 412 | guard-copy | any | `0x2` | — | — |
| `RCC_D1CCIPR` | `0x04C` | `Reserved` | 31:30 | 412 | reserved | 0x0 | `0x0` | — | — |
| `RCC_D1CCIPR` | `0x04C` | `Reserved` | 27:17 | 412 | reserved | 0x0 | `0x0` | — | — |
| `RCC_D1CCIPR` | `0x04C` | `Reserved` | 15:6 | 412 | reserved | 0x0 | `0x0` | — | — |
| `RCC_D1CCIPR` | `0x04C` | `Reserved` | 3:2 | 412 | reserved | 0x0 | `0x0` | — | — |
| `RCC_D1CCIPR` | `0x04C` | `SDMMCSEL` | 16:16 | 412 | guard-copy | any | `0x0` | — | — |
| `RCC_D2CCIP1R` | `0x050` | `DFSDM1SEL` | 24:24 | 413 | guard-copy | any | `0x0` | — | `0x0` (motors-dshot600, motors-pwm, sentinel-01, sentinel-01-ext-bmp581, sentinel-01-minimal, sentinel-02-bench, sentinel-02-shaped, sentinel-03, serial-dshot-9600) |
| `RCC_D2CCIP1R` | `0x050` | `FDCANSEL` | 29:28 | 413 | guard-copy | any | `0x1` | — | `0x0` (motors-dshot600, motors-pwm, sentinel-01, sentinel-01-ext-bmp581, sentinel-01-minimal, sentinel-02-bench, sentinel-02-shaped, sentinel-03, serial-dshot-9600) |
| `RCC_D2CCIP1R` | `0x050` | `Reserved` | 30:30 | 413 | reserved | 0x0 | `0x0` | — | `0x0` (motors-dshot600, motors-pwm, sentinel-01, sentinel-01-ext-bmp581, sentinel-01-minimal, sentinel-02-bench, sentinel-02-shaped, sentinel-03, serial-dshot-9600) |
| `RCC_D2CCIP1R` | `0x050` | `Reserved` | 27:25 | 413 | reserved | 0x0 | `0x0` | — | `0x0` (motors-dshot600, motors-pwm, sentinel-01, sentinel-01-ext-bmp581, sentinel-01-minimal, sentinel-02-bench, sentinel-02-shaped, sentinel-03, serial-dshot-9600) |
| `RCC_D2CCIP1R` | `0x050` | `Reserved` | 23:22 | 413 | reserved | 0x0 | `0x0` | — | `0x0` (motors-dshot600, motors-pwm, sentinel-01, sentinel-01-ext-bmp581, sentinel-01-minimal, sentinel-02-bench, sentinel-02-shaped, sentinel-03, serial-dshot-9600) |
| `RCC_D2CCIP1R` | `0x050` | `Reserved` | 19:19 | 413 | reserved | 0x0 | `0x0` | — | `0x0` (motors-dshot600, motors-pwm, sentinel-01, sentinel-01-ext-bmp581, sentinel-01-minimal, sentinel-02-bench, sentinel-02-shaped, sentinel-03, serial-dshot-9600) |
| `RCC_D2CCIP1R` | `0x050` | `Reserved` | 15:15 | 414 | reserved | 0x0 | `0x0` | — | `0x0` (motors-dshot600, motors-pwm, sentinel-01, sentinel-01-ext-bmp581, sentinel-01-minimal, sentinel-02-bench, sentinel-02-shaped, sentinel-03, serial-dshot-9600) |
| `RCC_D2CCIP1R` | `0x050` | `Reserved` | 11:9 | 414 | reserved | 0x0 | `0x0` | — | `0x0` (motors-dshot600, motors-pwm, sentinel-01, sentinel-01-ext-bmp581, sentinel-01-minimal, sentinel-02-bench, sentinel-02-shaped, sentinel-03, serial-dshot-9600) |
| `RCC_D2CCIP1R` | `0x050` | `Reserved` | 5:3 | 415 | reserved | 0x0 | `0x0` | — | `0x0` (motors-dshot600, motors-pwm, sentinel-01, sentinel-01-ext-bmp581, sentinel-01-minimal, sentinel-02-bench, sentinel-02-shaped, sentinel-03, serial-dshot-9600) |
| `RCC_D2CCIP1R` | `0x050` | `SAI1SEL[2:0]` | 2:0 | 415 | guard-copy | any | `0x0` | — | `0x0` (motors-dshot600, motors-pwm, sentinel-01, sentinel-01-ext-bmp581, sentinel-01-minimal, sentinel-02-bench, sentinel-02-shaped, sentinel-03, serial-dshot-9600) |
| `RCC_D2CCIP1R` | `0x050` | `SAI23SEL[2:0]` | 8:6 | 415 | guard-copy | any | `0x0` | — | `0x0` (motors-dshot600, motors-pwm, sentinel-01, sentinel-01-ext-bmp581, sentinel-01-minimal, sentinel-02-bench, sentinel-02-shaped, sentinel-03, serial-dshot-9600) |
| `RCC_D2CCIP1R` | `0x050` | `SPDIFSEL[1:0]` | 21:20 | 413 | guard-copy | any | `0x0` | — | `0x0` (motors-dshot600, motors-pwm, sentinel-01, sentinel-01-ext-bmp581, sentinel-01-minimal, sentinel-02-bench, sentinel-02-shaped, sentinel-03, serial-dshot-9600) |
| `RCC_D2CCIP1R` | `0x050` | `SPI123SEL[2:0]` | 14:12 | 414 | guard | 0x0,0x1,0x2,0x4,0x5,0x6,0x7 | `0x0` | — | `0x4` (motors-dshot600, motors-pwm, sentinel-01, sentinel-01-ext-bmp581, sentinel-01-minimal, sentinel-02-bench, sentinel-02-shaped, sentinel-03, serial-dshot-9600) |
| `RCC_D2CCIP1R` | `0x050` | `SPI45SEL[2:0]` | 18:16 | 414 | guard-copy | any | `0x1` | — | `0x0` (motors-dshot600, motors-pwm, sentinel-01, sentinel-01-ext-bmp581, sentinel-01-minimal, sentinel-02-bench, sentinel-02-shaped, sentinel-03, serial-dshot-9600) |
| `RCC_D2CCIP1R` | `0x050` | `SWPSEL` | 31:31 | 413 | guard-copy | any | `0x0` | — | `0x0` (motors-dshot600, motors-pwm, sentinel-01, sentinel-01-ext-bmp581, sentinel-01-minimal, sentinel-02-bench, sentinel-02-shaped, sentinel-03, serial-dshot-9600) |
| `RCC_D2CCIP2R` | `0x054` | `CECSEL[1:0]` | 23:22 | 416 | guard-copy | any | `0x3` | — | — |
| `RCC_D2CCIP2R` | `0x054` | `I2C123SEL[1:0]` | 13:12 | 416 | guard | 0x0,0x1,0x2,0x3 | `0x1` | — | — |
| `RCC_D2CCIP2R` | `0x054` | `LPTIM1SEL[2:0]` | 30:28 | 416 | guard-copy | any | `0x0` | — | — |
| `RCC_D2CCIP2R` | `0x054` | `RNGSEL[1:0]` | 9:8 | 417 | guard | 0x0,0x1 | `0x0` | — | — |
| `RCC_D2CCIP2R` | `0x054` | `Reserved` | 31:31 | 416 | reserved | 0x0 | `0x0` | — | — |
| `RCC_D2CCIP2R` | `0x054` | `Reserved` | 27:24 | 416 | reserved | 0x0 | `0x0` | — | — |
| `RCC_D2CCIP2R` | `0x054` | `Reserved` | 19:14 | 416 | reserved | 0x0 | `0x0` | — | — |
| `RCC_D2CCIP2R` | `0x054` | `Reserved` | 11:10 | 417 | reserved | 0x0 | `0x0` | — | — |
| `RCC_D2CCIP2R` | `0x054` | `Reserved` | 7:6 | 417 | reserved | 0x0 | `0x0` | — | — |
| `RCC_D2CCIP2R` | `0x054` | `USART16SEL[2:0]` | 5:3 | 417 | guard | 0x0,0x1,0x2,0x3,0x4,0x6,0x7 | `0x1` | — | — |
| `RCC_D2CCIP2R` | `0x054` | `USART234578SEL[2:0]` | 2:0 | 417 | guard | 0x0,0x1,0x2,0x3,0x4,0x6,0x7 | `0x1` | — | — |
| `RCC_D2CCIP2R` | `0x054` | `USBSEL[1:0]` | 21:20 | 416 | guard-copy | any | `0x2` | — | — |
| `RCC_D3CCIPR` | `0x058` | `ADCSEL[1:0]` | 17:16 | 419 | guard-copy | any | `0x1` | — | — |
| `RCC_D3CCIPR` | `0x058` | `I2C4SEL[1:0]` | 9:8 | 420 | guard-copy | any | `0x1` | — | — |
| `RCC_D3CCIPR` | `0x058` | `LPTIM2SEL[2:0]` | 12:10 | 419 | guard-copy | any | `0x0` | — | — |
| `RCC_D3CCIPR` | `0x058` | `LPTIM345SEL[2:0]` | 15:13 | 419 | guard-copy | any | `0x0` | — | — |
| `RCC_D3CCIPR` | `0x058` | `LPUART1SEL[2:0]` | 2:0 | 420 | guard-copy | any | `0x0` | — | — |
| `RCC_D3CCIPR` | `0x058` | `Reserved` | 31:31 | 418 | reserved | 0x0 | `0x0` | — | — |
| `RCC_D3CCIPR` | `0x058` | `Reserved` | 27:27 | 418 | reserved | 0x0 | `0x0` | — | — |
| `RCC_D3CCIPR` | `0x058` | `Reserved` | 20:18 | 419 | reserved | 0x0 | `0x0` | — | — |
| `RCC_D3CCIPR` | `0x058` | `Reserved` | 7:3 | 420 | reserved | 0x0 | `0x0` | — | — |
| `RCC_D3CCIPR` | `0x058` | `SAI4ASEL[2:0]` | 23:21 | 419 | guard-copy | any | `0x0` | — | — |
| `RCC_D3CCIPR` | `0x058` | `SAI4BSEL[2:0]` | 26:24 | 418 | guard-copy | any | `0x0` | — | — |
| `RCC_D3CCIPR` | `0x058` | `SPI6SEL[2:0]` | 30:28 | 418 | guard-copy | any | `0x1` | — | — |
| `RCC_CSR` | `0x074` | `LSION` | 0:0 | 429 | inherited | any | `0x0` | — | — |
| `RCC_CSR` | `0x074` | `LSIRDY` | 1:1 | 429 | read-only | any | `0x0` | — | — |
| `RCC_CSR` | `0x074` | `Reserved` | 31:2 | 429 | reserved | 0x0 | `0x0` | — | — |
| `RCC_AHB3RSTR` | `0x07C` | `DMA2DRST` | 4:4 | 431 | inherited | any | `0x0`, `0x1` | — | — |
| `RCC_AHB3RSTR` | `0x07C` | `FMCRST` | 12:12 | 430 | inherited | any | `0x0` | — | — |
| `RCC_AHB3RSTR` | `0x07C` | `JPGDECRST` | 5:5 | 430 | inherited | any | `0x0`, `0x1` | — | — |
| `RCC_AHB3RSTR` | `0x07C` | `MDMARST` | 0:0 | 431 | inherited | any | `0x0`, `0x1` | — | — |
| `RCC_AHB3RSTR` | `0x07C` | `QSPIRST` | 14:14 | 430 | inherited | any | `0x0`, `0x1` | — | — |
| `RCC_AHB3RSTR` | `0x07C` | `Reserved` | 31:17 | 430 | acknowledged | any | `0x0`, `0x3fff` | — | — |
| `RCC_AHB3RSTR` | `0x07C` | `Reserved` | 15:15 | 430 | acknowledged | any | `0x0`, `0x1` | — | — |
| `RCC_AHB3RSTR` | `0x07C` | `Reserved` | 13:13 | 430 | acknowledged | any | `0x0`, `0x1` | — | — |
| `RCC_AHB3RSTR` | `0x07C` | `Reserved` | 11:6 | 430 | acknowledged | any | `0x0`, `0x3f` | — | — |
| `RCC_AHB3RSTR` | `0x07C` | `Reserved` | 3:1 | 431 | acknowledged | any | `0x0`, `0x7` | — | — |
| `RCC_AHB3RSTR` | `0x07C` | `SDMMC1RST` | 16:16 | 430 | inherited | any | `0x0`, `0x1` | — | — |
| `RCC_AHB1RSTR` | `0x080` | `ADC12RST` | 5:5 | 432 | inherited | any | `0x0`, `0x1` | — | — |
| `RCC_AHB1RSTR` | `0x080` | `DMA1RST` | 0:0 | 433 | inherited | any | `0x0`, `0x1` | — | — |
| `RCC_AHB1RSTR` | `0x080` | `DMA2RST` | 1:1 | 433 | inherited | any | `0x0`, `0x1` | — | — |
| `RCC_AHB1RSTR` | `0x080` | `ETH1MACRST` | 15:15 | 432 | inherited | any | `0x0`, `0x1` | — | — |
| `RCC_AHB1RSTR` | `0x080` | `Reserved` | 31:28 | 432 | acknowledged | any | `0x0`, `0x7` | — | — |
| `RCC_AHB1RSTR` | `0x080` | `Reserved` | 26:26 | 432 | acknowledged | any | `0x0`, `0x1` | — | — |
| `RCC_AHB1RSTR` | `0x080` | `Reserved` | 24:16 | 432 | acknowledged | any | `0x0`, `0x1ff` | — | — |
| `RCC_AHB1RSTR` | `0x080` | `Reserved` | 14:6 | 432 | acknowledged | any | `0x0`, `0x1ff` | — | — |
| `RCC_AHB1RSTR` | `0x080` | `Reserved` | 4:2 | 433 | acknowledged | any | `0x0`, `0x7` | — | — |
| `RCC_AHB1RSTR` | `0x080` | `USB1OTGRST` | 25:25 | 432 | inherited | any | `0x0`, `0x1` | — | — |
| `RCC_AHB1RSTR` | `0x080` | `USB2OTGRST` | 27:27 | 432 | inherited | any | `0x0`, `0x1` | — | — |
| `RCC_AHB2RSTR` | `0x084` | `CRYPTRST` | 4:4 | 434 | inherited | any | `0x0`, `0x1` | — | — |
| `RCC_AHB2RSTR` | `0x084` | `DCMIRST` | 0:0 | 434 | inherited | any | `0x0`, `0x1` | — | — |
| `RCC_AHB2RSTR` | `0x084` | `HASHRST` | 5:5 | 434 | inherited | any | `0x0`, `0x1` | — | — |
| `RCC_AHB2RSTR` | `0x084` | `RNGRST` | 6:6 | 434 | inherited | any | `0x0`, `0x1` | — | — |
| `RCC_AHB2RSTR` | `0x084` | `Reserved` | 31:10 | 434 | acknowledged | any | `0x0`, `0x3fffff` | — | — |
| `RCC_AHB2RSTR` | `0x084` | `Reserved` | 8:7 | 434 | acknowledged | any | `0x0`, `0x3` | — | — |
| `RCC_AHB2RSTR` | `0x084` | `Reserved` | 3:1 | 434 | acknowledged | any | `0x0`, `0x7` | — | — |
| `RCC_AHB2RSTR` | `0x084` | `SDMMC2RST` | 9:9 | 434 | inherited | any | `0x0`, `0x1` | — | — |
| `RCC_AHB4RSTR` | `0x088` | `ADC3RST` | 24:24 | 435 | inherited | any | `0x0`, `0x1` | — | — |
| `RCC_AHB4RSTR` | `0x088` | `BDMARST` | 21:21 | 435 | inherited | any | `0x0`, `0x1` | — | — |
| `RCC_AHB4RSTR` | `0x088` | `CRCRST` | 19:19 | 435 | inherited | any | `0x0`, `0x1` | — | — |
| `RCC_AHB4RSTR` | `0x088` | `GPIOARST` | 0:0 | 436 | inherited | any | `0x0`, `0x1` | — | — |
| `RCC_AHB4RSTR` | `0x088` | `GPIOBRST` | 1:1 | 436 | inherited | any | `0x0`, `0x1` | — | — |
| `RCC_AHB4RSTR` | `0x088` | `GPIOCRST` | 2:2 | 436 | inherited | any | `0x0`, `0x1` | — | — |
| `RCC_AHB4RSTR` | `0x088` | `GPIODRST` | 3:3 | 436 | inherited | any | `0x0`, `0x1` | — | — |
| `RCC_AHB4RSTR` | `0x088` | `GPIOERST` | 4:4 | 436 | inherited | any | `0x0`, `0x1` | — | — |
| `RCC_AHB4RSTR` | `0x088` | `GPIOFRST` | 5:5 | 436 | inherited | any | `0x0`, `0x1` | — | — |
| `RCC_AHB4RSTR` | `0x088` | `GPIOGRST` | 6:6 | 436 | inherited | any | `0x0`, `0x1` | — | — |
| `RCC_AHB4RSTR` | `0x088` | `GPIOHRST` | 7:7 | 436 | inherited | any | `0x0`, `0x1` | — | — |
| `RCC_AHB4RSTR` | `0x088` | `GPIOIRST` | 8:8 | 436 | inherited | any | `0x0`, `0x1` | — | — |
| `RCC_AHB4RSTR` | `0x088` | `GPIOJRST` | 9:9 | 435 | inherited | any | `0x0`, `0x1` | — | — |
| `RCC_AHB4RSTR` | `0x088` | `GPIOKRST` | 10:10 | 435 | inherited | any | `0x0`, `0x1` | — | — |
| `RCC_AHB4RSTR` | `0x088` | `HSEMRST` | 25:25 | 435 | inherited | any | `0x0`, `0x1` | — | — |
| `RCC_AHB4RSTR` | `0x088` | `Reserved` | 31:26 | 435 | acknowledged | any | `0x0`, `0x3f` | — | — |
| `RCC_AHB4RSTR` | `0x088` | `Reserved` | 23:22 | 435 | acknowledged | any | `0x0`, `0x3` | — | — |
| `RCC_AHB4RSTR` | `0x088` | `Reserved` | 20:20 | 435 | acknowledged | any | `0x0`, `0x1` | — | — |
| `RCC_AHB4RSTR` | `0x088` | `Reserved` | 18:11 | 435 | acknowledged | any | `0x0`, `0xff` | — | — |
| `RCC_APB3RSTR` | `0x08C` | `LTDCRST` | 3:3 | 437 | inherited | any | `0x0`, `0x1` | — | — |
| `RCC_APB3RSTR` | `0x08C` | `Reserved` | 31:4 | 437 | acknowledged | any | `0x0`, `0xfffffff` | — | — |
| `RCC_APB3RSTR` | `0x08C` | `Reserved` | 2:0 | 437 | acknowledged | any | `0x0`, `0x7` | — | — |
| `RCC_APB1LRSTR` | `0x090` | `CECRST` | 27:27 | 438 | inherited | any | `0x0`, `0x1` | — | — |
| `RCC_APB1LRSTR` | `0x090` | `DAC12RST` | 29:29 | 438 | inherited | any | `0x0`, `0x1` | — | — |
| `RCC_APB1LRSTR` | `0x090` | `I2C1RST` | 21:21 | 439 | inherited | any | `0x0`, `0x1` | — | — |
| `RCC_APB1LRSTR` | `0x090` | `I2C2RST` | 22:22 | 438 | inherited | any | `0x0`, `0x1` | — | — |
| `RCC_APB1LRSTR` | `0x090` | `I2C3RST` | 23:23 | 438 | inherited | any | `0x0`, `0x1` | — | — |
| `RCC_APB1LRSTR` | `0x090` | `LPTIM1RST` | 9:9 | 439 | inherited | any | `0x0`, `0x1` | — | — |
| `RCC_APB1LRSTR` | `0x090` | `Reserved` | 28:28 | 438 | acknowledged | any | `0x0`, `0x1` | — | — |
| `RCC_APB1LRSTR` | `0x090` | `Reserved` | 26:24 | 438 | acknowledged | any | `0x0`, `0x7` | — | — |
| `RCC_APB1LRSTR` | `0x090` | `Reserved` | 13:10 | 439 | acknowledged | any | `0x0`, `0xf` | — | — |
| `RCC_APB1LRSTR` | `0x090` | `SPDIFRXRST` | 16:16 | 439 | inherited | any | `0x0`, `0x1` | — | — |
| `RCC_APB1LRSTR` | `0x090` | `SPI2RST` | 14:14 | 439 | inherited | any | `0x0`, `0x1` | — | — |
| `RCC_APB1LRSTR` | `0x090` | `SPI3RST` | 15:15 | 439 | inherited | any | `0x0`, `0x1` | — | — |
| `RCC_APB1LRSTR` | `0x090` | `TIM12RST` | 6:6 | 440 | inherited | any | `0x0`, `0x1` | — | — |
| `RCC_APB1LRSTR` | `0x090` | `TIM13RST` | 7:7 | 439 | inherited | any | `0x0`, `0x1` | — | — |
| `RCC_APB1LRSTR` | `0x090` | `TIM14RST` | 8:8 | 439 | inherited | any | `0x0`, `0x1` | — | — |
| `RCC_APB1LRSTR` | `0x090` | `TIM2RST` | 0:0 | 440 | inherited | any | `0x0`, `0x1` | — | — |
| `RCC_APB1LRSTR` | `0x090` | `TIM3RST` | 1:1 | 440 | inherited | any | `0x0`, `0x1` | — | — |
| `RCC_APB1LRSTR` | `0x090` | `TIM4RST` | 2:2 | 440 | inherited | any | `0x0`, `0x1` | — | — |
| `RCC_APB1LRSTR` | `0x090` | `TIM5RST` | 3:3 | 440 | inherited | any | `0x0`, `0x1` | — | — |
| `RCC_APB1LRSTR` | `0x090` | `TIM6RST` | 4:4 | 440 | inherited | any | `0x0`, `0x1` | — | — |
| `RCC_APB1LRSTR` | `0x090` | `TIM7RST` | 5:5 | 440 | inherited | any | `0x0`, `0x1` | — | — |
| `RCC_APB1LRSTR` | `0x090` | `UART4RST` | 19:19 | 439 | inherited | any | `0x0`, `0x1` | — | — |
| `RCC_APB1LRSTR` | `0x090` | `UART5RST` | 20:20 | 439 | inherited | any | `0x0`, `0x1` | — | — |
| `RCC_APB1LRSTR` | `0x090` | `UART7RST` | 30:30 | 438 | inherited | any | `0x0`, `0x1` | — | — |
| `RCC_APB1LRSTR` | `0x090` | `UART8RST` | 31:31 | 438 | inherited | any | `0x0`, `0x1` | — | — |
| `RCC_APB1LRSTR` | `0x090` | `USART2RST` | 17:17 | 439 | inherited | any | `0x0`, `0x1` | — | — |
| `RCC_APB1LRSTR` | `0x090` | `USART3RST` | 18:18 | 439 | inherited | any | `0x0`, `0x1` | — | — |
| `RCC_APB1HRSTR` | `0x094` | `CRSRST` | 1:1 | 441 | inherited | any | `0x0`, `0x1` | — | — |
| `RCC_APB1HRSTR` | `0x094` | `FDCANRST` | 8:8 | 441 | inherited | any | `0x0`, `0x1` | — | — |
| `RCC_APB1HRSTR` | `0x094` | `MDIOSRST` | 5:5 | 441 | inherited | any | `0x0`, `0x1` | — | — |
| `RCC_APB1HRSTR` | `0x094` | `OPAMPRST` | 4:4 | 441 | inherited | any | `0x0`, `0x1` | — | — |
| `RCC_APB1HRSTR` | `0x094` | `Reserved` | 31:9 | 441 | acknowledged | any | `0x0`, `0x7fffff` | — | — |
| `RCC_APB1HRSTR` | `0x094` | `Reserved` | 7:6 | 441 | acknowledged | any | `0x0`, `0x3` | — | — |
| `RCC_APB1HRSTR` | `0x094` | `Reserved` | 3:3 | 441 | acknowledged | any | `0x0`, `0x1` | — | — |
| `RCC_APB1HRSTR` | `0x094` | `Reserved` | 0:0 | 441 | acknowledged | any | `0x0`, `0x1` | — | — |
| `RCC_APB1HRSTR` | `0x094` | `SWPRST` | 2:2 | 441 | inherited | any | `0x0`, `0x1` | — | — |
| `RCC_APB2RSTR` | `0x098` | `DFSDM1RST` | 28:28 | 442 | inherited | any | `0x0`, `0x1` | — | — |
| `RCC_APB2RSTR` | `0x098` | `HRTIMRST` | 29:29 | 442 | inherited | any | `0x0`, `0x1` | — | — |
| `RCC_APB2RSTR` | `0x098` | `Reserved` | 31:30 | 442 | acknowledged | any | `0x0`, `0x3` | — | — |
| `RCC_APB2RSTR` | `0x098` | `Reserved` | 27:25 | 442 | acknowledged | any | `0x0`, `0x7` | — | — |
| `RCC_APB2RSTR` | `0x098` | `Reserved` | 21:21 | 442 | acknowledged | any | `0x0`, `0x1` | — | — |
| `RCC_APB2RSTR` | `0x098` | `Reserved` | 19:19 | 442 | acknowledged | any | `0x0`, `0x1` | — | — |
| `RCC_APB2RSTR` | `0x098` | `Reserved` | 15:14 | 443 | acknowledged | any | `0x0`, `0x3` | — | — |
| `RCC_APB2RSTR` | `0x098` | `Reserved` | 11:6 | 443 | acknowledged | any | `0x0`, `0x3f` | — | — |
| `RCC_APB2RSTR` | `0x098` | `Reserved` | 3:2 | 443 | acknowledged | any | `0x0`, `0x3` | — | — |
| `RCC_APB2RSTR` | `0x098` | `SAI1RST` | 22:22 | 442 | inherited | any | `0x0`, `0x1` | — | — |
| `RCC_APB2RSTR` | `0x098` | `SAI2RST` | 23:23 | 442 | inherited | any | `0x0`, `0x1` | — | — |
| `RCC_APB2RSTR` | `0x098` | `SAI3RST` | 24:24 | 442 | inherited | any | `0x0`, `0x1` | — | — |
| `RCC_APB2RSTR` | `0x098` | `SPI1RST` | 12:12 | 443 | inherited | any | `0x0`, `0x1` | — | — |
| `RCC_APB2RSTR` | `0x098` | `SPI4RST` | 13:13 | 443 | inherited | any | `0x0`, `0x1` | — | — |
| `RCC_APB2RSTR` | `0x098` | `SPI5RST` | 20:20 | 442 | inherited | any | `0x0`, `0x1` | — | — |
| `RCC_APB2RSTR` | `0x098` | `TIM15RST` | 16:16 | 443 | inherited | any | `0x0`, `0x1` | — | — |
| `RCC_APB2RSTR` | `0x098` | `TIM16RST` | 17:17 | 443 | inherited | any | `0x0`, `0x1` | — | — |
| `RCC_APB2RSTR` | `0x098` | `TIM17RST` | 18:18 | 443 | inherited | any | `0x0`, `0x1` | — | — |
| `RCC_APB2RSTR` | `0x098` | `TIM1RST` | 0:0 | 443 | inherited | any | `0x0`, `0x1` | — | — |
| `RCC_APB2RSTR` | `0x098` | `TIM8RST` | 1:1 | 443 | inherited | any | `0x0`, `0x1` | — | — |
| `RCC_APB2RSTR` | `0x098` | `USART1RST` | 4:4 | 443 | inherited | any | `0x0`, `0x1` | — | — |
| `RCC_APB2RSTR` | `0x098` | `USART6RST` | 5:5 | 443 | inherited | any | `0x0`, `0x1` | — | — |
| `RCC_APB4RSTR` | `0x09C` | `COMP12RST` | 14:14 | 444 | inherited | any | `0x0`, `0x1` | — | — |
| `RCC_APB4RSTR` | `0x09C` | `I2C4RST` | 7:7 | 445 | inherited | any | `0x0`, `0x1` | — | — |
| `RCC_APB4RSTR` | `0x09C` | `LPTIM2RST` | 9:9 | 445 | inherited | any | `0x0`, `0x1` | — | — |
| `RCC_APB4RSTR` | `0x09C` | `LPTIM3RST` | 10:10 | 444 | inherited | any | `0x0`, `0x1` | — | — |
| `RCC_APB4RSTR` | `0x09C` | `LPTIM4RST` | 11:11 | 444 | inherited | any | `0x0`, `0x1` | — | — |
| `RCC_APB4RSTR` | `0x09C` | `LPTIM5RST` | 12:12 | 444 | inherited | any | `0x0`, `0x1` | — | — |
| `RCC_APB4RSTR` | `0x09C` | `LPUART1RST` | 3:3 | 445 | inherited | any | `0x0`, `0x1` | — | — |
| `RCC_APB4RSTR` | `0x09C` | `Reserved` | 31:26 | 444 | acknowledged | any | `0x0`, `0x3f` | — | — |
| `RCC_APB4RSTR` | `0x09C` | `Reserved` | 25:22 | 444 | acknowledged | any | `0x0`, `0xf` | — | — |
| `RCC_APB4RSTR` | `0x09C` | `Reserved` | 20:16 | 444 | acknowledged | any | `0x0`, `0x1f` | — | — |
| `RCC_APB4RSTR` | `0x09C` | `Reserved` | 13:13 | 444 | acknowledged | any | `0x0`, `0x1` | — | — |
| `RCC_APB4RSTR` | `0x09C` | `Reserved` | 8:8 | 445 | acknowledged | any | `0x0`, `0x1` | — | — |
| `RCC_APB4RSTR` | `0x09C` | `Reserved` | 6:6 | 445 | acknowledged | any | `0x0`, `0x1` | — | — |
| `RCC_APB4RSTR` | `0x09C` | `Reserved` | 4:4 | 445 | acknowledged | any | `0x0`, `0x1` | — | — |
| `RCC_APB4RSTR` | `0x09C` | `Reserved` | 2:2 | 445 | acknowledged | any | `0x0`, `0x1` | — | — |
| `RCC_APB4RSTR` | `0x09C` | `Reserved` | 0:0 | 445 | acknowledged | any | `0x0`, `0x1` | — | — |
| `RCC_APB4RSTR` | `0x09C` | `SAI4RST` | 21:21 | 444 | inherited | any | `0x0`, `0x1` | — | — |
| `RCC_APB4RSTR` | `0x09C` | `SPI6RST` | 5:5 | 445 | inherited | any | `0x0`, `0x1` | — | — |
| `RCC_APB4RSTR` | `0x09C` | `SYSCFGRST` | 1:1 | 445 | inherited | any | `0x0` | — | — |
| `RCC_APB4RSTR` | `0x09C` | `VREFRST` | 15:15 | 444 | inherited | any | `0x0`, `0x1` | — | — |
| `RCC_RSR` | `0x0D0` | `BORRSTF` | 21:21 | 451 | read-only | any | `0x0` | — | — |
| `RCC_RSR` | `0x0D0` | `CPURSTF` | 17:17 | 451 | read-only | any | `0x0` | — | — |
| `RCC_RSR` | `0x0D0` | `D1RSTF` | 19:19 | 451 | read-only | any | `0x0` | — | — |
| `RCC_RSR` | `0x0D0` | `D2RSTF` | 20:20 | 451 | read-only | any | `0x0` | — | — |
| `RCC_RSR` | `0x0D0` | `IWDG1RSTF` | 26:26 | 450 | read-only | any | `0x0` | — | — |
| `RCC_RSR` | `0x0D0` | `LPWRRSTF` | 30:30 | 450 | read-only | any | `0x0` | — | — |
| `RCC_RSR` | `0x0D0` | `PINRSTF` | 22:22 | 451 | read-only | any | `0x0` | — | — |
| `RCC_RSR` | `0x0D0` | `PORRSTF` | 23:23 | 451 | read-only | any | `0x0` | — | — |
| `RCC_RSR` | `0x0D0` | `RMVF` | 16:16 | 451 | inherited | any | `0x1` | — | — |
| `RCC_RSR` | `0x0D0` | `Reserved` | 31:31 | 450 | reserved | 0x0 | `0x0` | — | — |
| `RCC_RSR` | `0x0D0` | `Reserved` | 29:29 | 450 | reserved | 0x0 | `0x0` | — | — |
| `RCC_RSR` | `0x0D0` | `Reserved` | 27:27 | 450 | reserved | 0x0 | `0x0` | — | — |
| `RCC_RSR` | `0x0D0` | `Reserved` | 25:25 | 450 | reserved | 0x0 | `0x0` | — | — |
| `RCC_RSR` | `0x0D0` | `Reserved` | 18:18 | 451 | acknowledged | 0x0,0x1 | `0x0` | — | — |
| `RCC_RSR` | `0x0D0` | `Reserved` | 15:0 | 451 | reserved | 0x0 | `0x0` | — | — |
| `RCC_RSR` | `0x0D0` | `SFTRSTF` | 24:24 | 451 | read-only | any | `0x0` | — | — |
| `RCC_RSR` | `0x0D0` | `WWDG1RSTF` | 28:28 | 450 | read-only | any | `0x0` | — | — |
| `RCC_AHB3ENR` | `0x0D4` | `DMA2DEN` | 4:4 | 453 | inherited | any | — | `0x0` | — |
| `RCC_AHB3ENR` | `0x0D4` | `FMCEN` | 12:12 | 452 | kernel-unmodeled | 0x0 | — | `0x0` | — |
| `RCC_AHB3ENR` | `0x0D4` | `JPGDECEN` | 5:5 | 452 | inherited | any | — | `0x0` | — |
| `RCC_AHB3ENR` | `0x0D4` | `MDMAEN` | 0:0 | 453 | inherited | any | — | `0x0` | — |
| `RCC_AHB3ENR` | `0x0D4` | `QSPIEN` | 14:14 | 452 | kernel-unmodeled | 0x0 | — | `0x0` | — |
| `RCC_AHB3ENR` | `0x0D4` | `Reserved` | 31:17 | 452 | reserved | 0x0 | — | `0x0` | — |
| `RCC_AHB3ENR` | `0x0D4` | `Reserved` | 15:15 | 452 | reserved | 0x0 | — | `0x0` | — |
| `RCC_AHB3ENR` | `0x0D4` | `Reserved` | 13:13 | 452 | reserved | 0x0 | — | `0x0` | — |
| `RCC_AHB3ENR` | `0x0D4` | `Reserved` | 11:6 | 452 | reserved | 0x0 | — | `0x0` | — |
| `RCC_AHB3ENR` | `0x0D4` | `Reserved` | 3:1 | 453 | reserved | 0x0 | — | `0x0` | — |
| `RCC_AHB3ENR` | `0x0D4` | `SDMMC1EN` | 16:16 | 452 | kernel-unmodeled | 0x0 | — | `0x0` | — |
| `RCC_AHB1ENR` | `0x0D8` | `ADC12EN` | 5:5 | 455 | kernel-unmodeled | 0x0 | `0x0` | `0x0` | — |
| `RCC_AHB1ENR` | `0x0D8` | `DMA1EN` | 0:0 | 455 | inherited | any | `0x1` | `0x0` | — |
| `RCC_AHB1ENR` | `0x0D8` | `DMA2EN` | 1:1 | 455 | inherited | any | `0x0`, `0x1` | `0x0` | — |
| `RCC_AHB1ENR` | `0x0D8` | `ETH1MACEN` | 15:15 | 455 | inherited | any | `0x0` | `0x0` | — |
| `RCC_AHB1ENR` | `0x0D8` | `ETH1RXEN` | 17:17 | 455 | inherited | any | `0x0` | `0x0` | — |
| `RCC_AHB1ENR` | `0x0D8` | `ETH1TXEN` | 16:16 | 455 | inherited | any | `0x0` | `0x0` | — |
| `RCC_AHB1ENR` | `0x0D8` | `Reserved` | 31:29 | 454 | reserved | 0x0 | `0x0` | `0x0` | — |
| `RCC_AHB1ENR` | `0x0D8` | `Reserved` | 24:18 | 455 | reserved | 0x0 | `0x0` | `0x0` | — |
| `RCC_AHB1ENR` | `0x0D8` | `Reserved` | 14:6 | 455 | reserved | 0x0 | `0x0` | `0x0` | — |
| `RCC_AHB1ENR` | `0x0D8` | `Reserved` | 4:2 | 455 | reserved | 0x0 | `0x0` | `0x0` | — |
| `RCC_AHB1ENR` | `0x0D8` | `USB1OTGHSEN` | 25:25 | 454 | kernel-unmodeled | 0x0 | `0x0` | `0x0` | — |
| `RCC_AHB1ENR` | `0x0D8` | `USB1OTGHSULPIEN` | 26:26 | 454 | inherited | any | `0x0` | `0x0` | — |
| `RCC_AHB1ENR` | `0x0D8` | `USB2OTGHSEN` | 27:27 | 454 | kernel-unmodeled | 0x0 | `0x0` | `0x0` | — |
| `RCC_AHB1ENR` | `0x0D8` | `USB2OTGHSULPIEN` | 28:28 | 454 | inherited | any | `0x0` | `0x0` | — |
| `RCC_AHB2ENR` | `0x0DC` | `CRYPTEN` | 4:4 | 457 | inherited | any | `0x0` | `0x0` | — |
| `RCC_AHB2ENR` | `0x0DC` | `DCMIEN` | 0:0 | 457 | inherited | any | `0x0` | `0x0` | — |
| `RCC_AHB2ENR` | `0x0DC` | `HASHEN` | 5:5 | 457 | inherited | any | `0x0` | `0x0` | — |
| `RCC_AHB2ENR` | `0x0DC` | `RNGEN` | 6:6 | 457 | guard | any | `0x0`, `0x1` | `0x0` | — |
| `RCC_AHB2ENR` | `0x0DC` | `Reserved` | 28:10 | 456 | reserved | 0x0 | `0x0` | `0x0` | — |
| `RCC_AHB2ENR` | `0x0DC` | `Reserved` | 8:7 | 456 | reserved | 0x0 | `0x0` | `0x0` | — |
| `RCC_AHB2ENR` | `0x0DC` | `Reserved` | 3:1 | 457 | reserved | 0x0 | `0x0` | `0x0` | — |
| `RCC_AHB2ENR` | `0x0DC` | `SDMMC2EN` | 9:9 | 456 | kernel-unmodeled | 0x0 | `0x0` | `0x0` | — |
| `RCC_AHB2ENR` | `0x0DC` | `SRAM1EN` | 29:29 | 456 | inherited | any | `0x0`, `0x1` | `0x0` | — |
| `RCC_AHB2ENR` | `0x0DC` | `SRAM2EN` | 30:30 | 456 | inherited | any | `0x0`, `0x1` | `0x0` | — |
| `RCC_AHB2ENR` | `0x0DC` | `SRAM3EN` | 31:31 | 456 | inherited | any | `0x0`, `0x1` | `0x0` | — |
| `RCC_AHB4ENR` | `0x0E0` | `ADC3EN` | 24:24 | 458 | kernel-unmodeled **outside** | 0x0 | `0x0` | `0x0` | `0x0` (all); `0x1` (d1-drv8876) |
| `RCC_AHB4ENR` | `0x0E0` | `BDMAEN` | 21:21 | 458 | inherited | any | `0x0` | `0x0` | `0x0` (all) |
| `RCC_AHB4ENR` | `0x0E0` | `BKPRAMEN` | 28:28 | 458 | inherited | any | `0x0` | `0x0` | `0x0` (all) |
| `RCC_AHB4ENR` | `0x0E0` | `CRCEN` | 19:19 | 459 | inherited | any | `0x0` | `0x0` | `0x0` (all) |
| `RCC_AHB4ENR` | `0x0E0` | `GPIOAEN` | 0:0 | 460 | inherited | any | `0x1` | `0x0` | `0x1` (all) |
| `RCC_AHB4ENR` | `0x0E0` | `GPIOBEN` | 1:1 | 460 | inherited | any | `0x1` | `0x0` | `0x0` (all); `0x1` (all) |
| `RCC_AHB4ENR` | `0x0E0` | `GPIOCEN` | 2:2 | 460 | inherited | any | `0x1` | `0x0` | `0x0` (all); `0x1` (sentinel-03) |
| `RCC_AHB4ENR` | `0x0E0` | `GPIODEN` | 3:3 | 459 | inherited | any | `0x1` | `0x0` | `0x0` (all); `0x1` (sentinel-02-bench, sentinel-02-bench-hse8) |
| `RCC_AHB4ENR` | `0x0E0` | `GPIOEEN` | 4:4 | 459 | inherited | any | `0x1` | `0x0` | `0x0` (all); `0x1` (sentinel-03) |
| `RCC_AHB4ENR` | `0x0E0` | `GPIOFEN` | 5:5 | 459 | inherited | any | `0x1` | `0x0` | `0x0` (all); `0x1` (d1-drv8876) |
| `RCC_AHB4ENR` | `0x0E0` | `GPIOGEN` | 6:6 | 459 | inherited | any | `0x1` | `0x0` | `0x0` (all) |
| `RCC_AHB4ENR` | `0x0E0` | `GPIOHEN` | 7:7 | 459 | inherited | any | `0x1` | `0x0` | `0x0` (all) |
| `RCC_AHB4ENR` | `0x0E0` | `GPIOIEN` | 8:8 | 459 | inherited | any | `0x1` | `0x0` | `0x0` (all) |
| `RCC_AHB4ENR` | `0x0E0` | `GPIOJEN` | 9:9 | 459 | inherited | any | `0x1` | `0x0` | `0x0` (all) |
| `RCC_AHB4ENR` | `0x0E0` | `GPIOKEN` | 10:10 | 459 | inherited | any | `0x1` | `0x0` | `0x0` (all) |
| `RCC_AHB4ENR` | `0x0E0` | `HSEMEN` | 25:25 | 458 | inherited | any | `0x0` | `0x0` | `0x0` (all) |
| `RCC_AHB4ENR` | `0x0E0` | `Reserved` | 31:29 | 458 | reserved | 0x0 | `0x0` | `0x0` | `0x0` (all) |
| `RCC_AHB4ENR` | `0x0E0` | `Reserved` | 27:26 | 458 | reserved | 0x0 | `0x0` | `0x0` | `0x0` (all) |
| `RCC_AHB4ENR` | `0x0E0` | `Reserved` | 23:22 | 458 | reserved | 0x0 | `0x0` | `0x0` | `0x0` (all) |
| `RCC_AHB4ENR` | `0x0E0` | `Reserved` | 20:20 | 458 | reserved | 0x0 | `0x0` | `0x0` | `0x0` (all) |
| `RCC_AHB4ENR` | `0x0E0` | `Reserved` | 18:11 | 459 | reserved | 0x0 | `0x0` | `0x0` | `0x0` (all) |
| `RCC_APB3ENR` | `0x0E4` | `LTDCEN` | 3:3 | 461 | kernel-unmodeled | 0x0 | — | `0x0` | — |
| `RCC_APB3ENR` | `0x0E4` | `Reserved` | 31:7 | 461 | reserved | 0x0 | — | `0x0` | — |
| `RCC_APB3ENR` | `0x0E4` | `Reserved` | 5:4 | 461 | reserved | 0x0 | — | `0x0` | — |
| `RCC_APB3ENR` | `0x0E4` | `Reserved` | 2:0 | 461 | reserved | 0x0 | — | `0x0` | — |
| `RCC_APB3ENR` | `0x0E4` | `WWDG1EN` | 6:6 | 461 | inherited | any | — | `0x0` | — |
| `RCC_APB1LENR` | `0x0E8` | `CECEN` | 27:27 | 462 | kernel-unmodeled | 0x0 | `0x0` | `0x0` | `0x0` (all) |
| `RCC_APB1LENR` | `0x0E8` | `DAC12EN` | 29:29 | 462 | inherited | any | `0x0` | `0x0` | `0x0` (all) |
| `RCC_APB1LENR` | `0x0E8` | `I2C1EN` | 21:21 | 463 | guard | any | `0x0`, `0x1` | `0x0` | `0x0` (d1-drv8876, driver-judges-nothing); `0x1` (hse-crystal-16, hse-external-8, motors-dshot600, motors-pwm, sentinel-01, sentinel-01-ext-bmp581, sentinel-01-minimal, sentinel-02-bench, sentinel-02-bench-hse8, sentinel-02-shaped, sentinel-03, serial-dshot-9600) |
| `RCC_APB1LENR` | `0x0E8` | `I2C2EN` | 22:22 | 463 | guard | any | `0x0` | `0x0` | `0x0` (all); `0x1` (sentinel-01, sentinel-01-ext-bmp581, sentinel-03) |
| `RCC_APB1LENR` | `0x0E8` | `I2C3EN` | 23:23 | 463 | kernel-unmodeled | 0x0 | `0x0` | `0x0` | `0x0` (all) |
| `RCC_APB1LENR` | `0x0E8` | `LPTIM1EN` | 9:9 | 464 | kernel-unmodeled | 0x0 | `0x0` | `0x0` | `0x0` (all) |
| `RCC_APB1LENR` | `0x0E8` | `Reserved` | 28:28 | 462 | reserved | 0x0 | `0x0` | `0x0` | `0x0` (all) |
| `RCC_APB1LENR` | `0x0E8` | `Reserved` | 26:24 | 462 | reserved | 0x0 | `0x0` | `0x0` | `0x0` (all) |
| `RCC_APB1LENR` | `0x0E8` | `Reserved` | 13:10 | 464 | reserved | 0x0 | `0x0` | `0x0` | `0x0` (all) |
| `RCC_APB1LENR` | `0x0E8` | `SPDIFRXEN` | 16:16 | 463 | kernel-unmodeled | 0x0 | `0x0` | `0x0` | `0x0` (all) |
| `RCC_APB1LENR` | `0x0E8` | `SPI2EN` | 14:14 | 464 | guard | any | `0x0`, `0x1` | `0x0` | `0x0` (all); `0x1` (hse-crystal-16, hse-external-8, sentinel-02-shaped, sentinel-03) |
| `RCC_APB1LENR` | `0x0E8` | `SPI3EN` | 15:15 | 464 | kernel-unmodeled | 0x0 | `0x0` | `0x0` | `0x0` (all) |
| `RCC_APB1LENR` | `0x0E8` | `TIM12EN` | 6:6 | 464 | inherited | any | `0x0` | `0x0` | `0x0` (all) |
| `RCC_APB1LENR` | `0x0E8` | `TIM13EN` | 7:7 | 464 | inherited | any | `0x0` | `0x0` | `0x0` (all) |
| `RCC_APB1LENR` | `0x0E8` | `TIM14EN` | 8:8 | 464 | inherited | any | `0x0` | `0x0` | `0x0` (all) |
| `RCC_APB1LENR` | `0x0E8` | `TIM2EN` | 0:0 | 465 | inherited | any | `0x0` | `0x0` | `0x0` (hse-crystal-16, hse-external-8, motors-dshot600, motors-pwm, sentinel-01, sentinel-01-ext-bmp581, sentinel-01-minimal, sentinel-02-bench, sentinel-02-bench-hse8, sentinel-02-shaped, sentinel-03, serial-dshot-9600); `0x1` (d1-drv8876, driver-judges-nothing, hse-crystal-16, hse-external-8) |
| `RCC_APB1LENR` | `0x0E8` | `TIM3EN` | 1:1 | 465 | inherited | any | `0x0` | `0x0` | `0x0` (all) |
| `RCC_APB1LENR` | `0x0E8` | `TIM4EN` | 2:2 | 465 | inherited | any | `0x0` | `0x0` | `0x0` (all) |
| `RCC_APB1LENR` | `0x0E8` | `TIM5EN` | 3:3 | 464 | inherited | any | `0x1` | `0x0` | `0x0` (all) |
| `RCC_APB1LENR` | `0x0E8` | `TIM6EN` | 4:4 | 464 | inherited | any | `0x0` | `0x0` | `0x0` (all) |
| `RCC_APB1LENR` | `0x0E8` | `TIM7EN` | 5:5 | 464 | inherited | any | `0x0` | `0x0` | `0x0` (all) |
| `RCC_APB1LENR` | `0x0E8` | `UART4EN` | 19:19 | 463 | kernel-unmodeled | 0x0 | `0x0` | `0x0` | `0x0` (all) |
| `RCC_APB1LENR` | `0x0E8` | `UART5EN` | 20:20 | 463 | kernel-unmodeled | 0x0 | `0x0` | `0x0` | `0x0` (all) |
| `RCC_APB1LENR` | `0x0E8` | `UART7EN` | 30:30 | 462 | kernel-unmodeled | 0x0 | `0x0` | `0x0` | `0x0` (all) |
| `RCC_APB1LENR` | `0x0E8` | `UART8EN` | 31:31 | 462 | kernel-unmodeled | 0x0 | `0x0` | `0x0` | `0x0` (all) |
| `RCC_APB1LENR` | `0x0E8` | `USART2EN` | 17:17 | 463 | guard | any | `0x0` | `0x0` | `0x0` (all); `0x1` (hse-crystal-16, hse-external-8, sentinel-02-shaped, sentinel-03) |
| `RCC_APB1LENR` | `0x0E8` | `USART3EN` | 18:18 | 463 | guard | any | `0x0` | `0x0` | `0x0` (all); `0x1` (hse-crystal-16, hse-external-8, sentinel-02-shaped, sentinel-03) |
| `RCC_APB1HENR` | `0x0EC` | `CRSEN` | 1:1 | 467 | inherited | any | — | `0x0` | — |
| `RCC_APB1HENR` | `0x0EC` | `FDCANEN` | 8:8 | 466 | kernel-unmodeled | 0x0 | — | `0x0` | — |
| `RCC_APB1HENR` | `0x0EC` | `MDIOSEN` | 5:5 | 466 | inherited | any | — | `0x0` | — |
| `RCC_APB1HENR` | `0x0EC` | `OPAMPEN` | 4:4 | 466 | inherited | any | — | `0x0` | — |
| `RCC_APB1HENR` | `0x0EC` | `Reserved` | 31:9 | 466 | reserved | 0x0 | — | `0x0` | — |
| `RCC_APB1HENR` | `0x0EC` | `Reserved` | 7:6 | 466 | reserved | 0x0 | — | `0x0` | — |
| `RCC_APB1HENR` | `0x0EC` | `Reserved` | 3:3 | 466 | reserved | 0x0 | — | `0x0` | — |
| `RCC_APB1HENR` | `0x0EC` | `Reserved` | 0:0 | 467 | reserved | 0x0 | — | `0x0` | — |
| `RCC_APB1HENR` | `0x0EC` | `SWPEN` | 2:2 | 467 | kernel-unmodeled | 0x0 | — | `0x0` | — |
| `RCC_APB2ENR` | `0x0F0` | `DFSDM1EN` | 28:28 | 468 | kernel-unmodeled | 0x0 | `0x0` | `0x0` | `0x0` (all) |
| `RCC_APB2ENR` | `0x0F0` | `HRTIMEN` | 29:29 | 468 | inherited | any | `0x0` | `0x0` | `0x0` (all) |
| `RCC_APB2ENR` | `0x0F0` | `Reserved` | 31:30 | 468 | reserved | 0x0 | `0x0` | `0x0` | `0x0` (all) |
| `RCC_APB2ENR` | `0x0F0` | `Reserved` | 27:25 | 468 | reserved | 0x0 | `0x0` | `0x0` | `0x0` (all) |
| `RCC_APB2ENR` | `0x0F0` | `Reserved` | 21:21 | 469 | reserved | 0x0 | `0x0` | `0x0` | `0x0` (all) |
| `RCC_APB2ENR` | `0x0F0` | `Reserved` | 19:19 | 469 | reserved | 0x0 | `0x0` | `0x0` | `0x0` (all) |
| `RCC_APB2ENR` | `0x0F0` | `Reserved` | 15:14 | 469 | reserved | 0x0 | `0x0` | `0x0` | `0x0` (all) |
| `RCC_APB2ENR` | `0x0F0` | `Reserved` | 11:6 | 469 | reserved | 0x0 | `0x0` | `0x0` | `0x0` (all) |
| `RCC_APB2ENR` | `0x0F0` | `Reserved` | 3:2 | 470 | reserved | 0x0 | `0x0` | `0x0` | `0x0` (all) |
| `RCC_APB2ENR` | `0x0F0` | `SAI1EN` | 22:22 | 469 | kernel-unmodeled | 0x0 | `0x0` | `0x0` | `0x0` (all) |
| `RCC_APB2ENR` | `0x0F0` | `SAI2EN` | 23:23 | 468 | kernel-unmodeled | 0x0 | `0x0` | `0x0` | `0x0` (all) |
| `RCC_APB2ENR` | `0x0F0` | `SAI3EN` | 24:24 | 468 | kernel-unmodeled | 0x0 | `0x0` | `0x0` | `0x0` (all) |
| `RCC_APB2ENR` | `0x0F0` | `SPI1EN` | 12:12 | 469 | guard | any | `0x0`, `0x1` | `0x0` | `0x0` (d1-drv8876, driver-judges-nothing); `0x1` (hse-crystal-16, hse-external-8, motors-dshot600, motors-pwm, sentinel-01, sentinel-01-ext-bmp581, sentinel-01-minimal, sentinel-02-bench, sentinel-02-bench-hse8, sentinel-02-shaped, sentinel-03, serial-dshot-9600) |
| `RCC_APB2ENR` | `0x0F0` | `SPI4EN` | 13:13 | 469 | kernel-unmodeled | 0x0 | `0x0` | `0x0` | `0x0` (all) |
| `RCC_APB2ENR` | `0x0F0` | `SPI5EN` | 20:20 | 469 | kernel-unmodeled | 0x0 | `0x0` | `0x0` | `0x0` (all) |
| `RCC_APB2ENR` | `0x0F0` | `TIM15EN` | 16:16 | 469 | inherited | any | `0x0` | `0x0` | `0x0` (all) |
| `RCC_APB2ENR` | `0x0F0` | `TIM16EN` | 17:17 | 469 | inherited | any | `0x0` | `0x0` | `0x0` (all) |
| `RCC_APB2ENR` | `0x0F0` | `TIM17EN` | 18:18 | 469 | inherited | any | `0x0` | `0x0` | `0x0` (all) |
| `RCC_APB2ENR` | `0x0F0` | `TIM1EN` | 0:0 | 470 | inherited | any | `0x0` | `0x0` | `0x0` (hse-crystal-16, hse-external-8, motors-dshot600, motors-pwm, sentinel-01, sentinel-01-ext-bmp581, sentinel-01-minimal, sentinel-02-bench, sentinel-02-bench-hse8, sentinel-02-shaped, sentinel-03, serial-dshot-9600); `0x1` (d1-drv8876, driver-judges-nothing, hse-crystal-16, hse-external-8, motors-dshot600, motors-pwm, sentinel-02-shaped, sentinel-03, serial-dshot-9600) |
| `RCC_APB2ENR` | `0x0F0` | `TIM8EN` | 1:1 | 470 | inherited | any | `0x0` | `0x0` | `0x0` (all) |
| `RCC_APB2ENR` | `0x0F0` | `USART1EN` | 4:4 | 470 | guard | any | `0x1` | `0x0` | `0x0` (all); `0x1` (d1-drv8876, driver-judges-nothing, sentinel-01, sentinel-01-ext-bmp581, sentinel-01-minimal, sentinel-03, serial-dshot-9600) |
| `RCC_APB2ENR` | `0x0F0` | `USART6EN` | 5:5 | 469 | kernel-unmodeled | 0x0 | `0x0` | `0x0` | `0x0` (all) |
| `RCC_APB4ENR` | `0x0F4` | `COMP12EN` | 14:14 | 471 | inherited | any | `0x0` | `0x0` | — |
| `RCC_APB4ENR` | `0x0F4` | `I2C4EN` | 7:7 | 472 | kernel-unmodeled | 0x0 | `0x0` | `0x0` | — |
| `RCC_APB4ENR` | `0x0F4` | `LPTIM2EN` | 9:9 | 472 | kernel-unmodeled | 0x0 | `0x0` | `0x0` | — |
| `RCC_APB4ENR` | `0x0F4` | `LPTIM3EN` | 10:10 | 472 | kernel-unmodeled | 0x0 | `0x0` | `0x0` | — |
| `RCC_APB4ENR` | `0x0F4` | `LPTIM4EN` | 11:11 | 472 | kernel-unmodeled | 0x0 | `0x0` | `0x0` | — |
| `RCC_APB4ENR` | `0x0F4` | `LPTIM5EN` | 12:12 | 472 | kernel-unmodeled | 0x0 | `0x0` | `0x0` | — |
| `RCC_APB4ENR` | `0x0F4` | `LPUART1EN` | 3:3 | 472 | kernel-unmodeled | 0x0 | `0x0` | `0x0` | — |
| `RCC_APB4ENR` | `0x0F4` | `RTCAPBEN` | 16:16 | 471 | tag | 0x1 | `0x1` | `0x1` | — |
| `RCC_APB4ENR` | `0x0F4` | `Reserved` | 31:26 | 471 | reserved | 0x0 | `0x0` | `0x0` | — |
| `RCC_APB4ENR` | `0x0F4` | `Reserved` | 25:22 | 471 | reserved | 0x0 | `0x0` | `0x0` | — |
| `RCC_APB4ENR` | `0x0F4` | `Reserved` | 20:17 | 471 | reserved | 0x0 | `0x0` | `0x0` | — |
| `RCC_APB4ENR` | `0x0F4` | `Reserved` | 13:13 | 471 | reserved | 0x0 | `0x0` | `0x0` | — |
| `RCC_APB4ENR` | `0x0F4` | `Reserved` | 8:8 | 472 | reserved | 0x0 | `0x0` | `0x0` | — |
| `RCC_APB4ENR` | `0x0F4` | `Reserved` | 6:6 | 472 | reserved | 0x0 | `0x0` | `0x0` | — |
| `RCC_APB4ENR` | `0x0F4` | `Reserved` | 4:4 | 472 | reserved | 0x0 | `0x0` | `0x0` | — |
| `RCC_APB4ENR` | `0x0F4` | `Reserved` | 2:2 | 473 | reserved | 0x0 | `0x0` | `0x0` | — |
| `RCC_APB4ENR` | `0x0F4` | `Reserved` | 0:0 | 473 | reserved | 0x0 | `0x0` | `0x0` | — |
| `RCC_APB4ENR` | `0x0F4` | `SAI4EN` | 21:21 | 471 | kernel-unmodeled | 0x0 | `0x0` | `0x0` | — |
| `RCC_APB4ENR` | `0x0F4` | `SPI6EN` | 5:5 | 472 | kernel-unmodeled | 0x0 | `0x0` | `0x0` | — |
| `RCC_APB4ENR` | `0x0F4` | `SYSCFGEN` | 1:1 | 473 | inherited | any | `0x1` | `0x0` | — |
| `RCC_APB4ENR` | `0x0F4` | `VREFEN` | 15:15 | 471 | inherited | any | `0x0` | `0x0` | — |
| `RCC_AHB1LPENR` | `0x100` | `ADC12LPEN` | 5:5 | 477 | tag | 0x1 | `0x1` | — | — |
| `RCC_AHB1LPENR` | `0x100` | `DMA1LPEN` | 0:0 | 477 | tag | 0x1 | `0x1` | — | — |
| `RCC_AHB1LPENR` | `0x100` | `DMA2LPEN` | 1:1 | 477 | acknowledged | 0x0,0x1 | `0x0`, `0x1` | — | — |
| `RCC_AHB1LPENR` | `0x100` | `ETH1MACLPEN` | 15:15 | 477 | tag | 0x1 | `0x1` | — | — |
| `RCC_AHB1LPENR` | `0x100` | `ETH1RXLPEN` | 17:17 | 477 | tag | 0x1 | `0x1` | — | — |
| `RCC_AHB1LPENR` | `0x100` | `ETH1TXLPEN` | 16:16 | 477 | tag | 0x1 | `0x1` | — | — |
| `RCC_AHB1LPENR` | `0x100` | `Reserved` | 31:29 | 476 | reserved | 0x0 | `0x0` | — | — |
| `RCC_AHB1LPENR` | `0x100` | `Reserved` | 24:18 | 477 | reserved | 0x0 | `0x0` | — | — |
| `RCC_AHB1LPENR` | `0x100` | `Reserved` | 14:6 | 477 | reserved | 0x100 | `0x100` | — | — |
| `RCC_AHB1LPENR` | `0x100` | `Reserved` | 4:2 | 477 | reserved | 0x0 | `0x0` | — | — |
| `RCC_AHB1LPENR` | `0x100` | `USB1OTGHSLPEN` | 25:25 | 477 | tag | 0x1 | `0x1` | — | — |
| `RCC_AHB1LPENR` | `0x100` | `USB1OTGHSULPILPEN` | 26:26 | 476 | tag | 0x1 | `0x1` | — | — |
| `RCC_AHB1LPENR` | `0x100` | `USB2OTGHSLPEN` | 27:27 | 476 | tag | 0x1 | `0x1` | — | — |
| `RCC_AHB1LPENR` | `0x100` | `USB2OTGHSULPILPEN` | 28:28 | 476 | tag | 0x1 | `0x1` | — | — |
| `RCC_AHB2LPENR` | `0x104` | `CRYPTLPEN` | 4:4 | 479 | tag | 0x1 | `0x1` | — | — |
| `RCC_AHB2LPENR` | `0x104` | `DCMILPEN` | 0:0 | 479 | tag | 0x1 | `0x1` | — | — |
| `RCC_AHB2LPENR` | `0x104` | `HASHLPEN` | 5:5 | 479 | tag | 0x1 | `0x1` | — | — |
| `RCC_AHB2LPENR` | `0x104` | `RNGLPEN` | 6:6 | 478 | acknowledged | 0x0,0x1 | `0x0`, `0x1` | — | — |
| `RCC_AHB2LPENR` | `0x104` | `Reserved` | 28:10 | 478 | reserved | 0x0 | `0x0` | — | — |
| `RCC_AHB2LPENR` | `0x104` | `Reserved` | 8:7 | 478 | reserved | 0x0 | `0x0` | — | — |
| `RCC_AHB2LPENR` | `0x104` | `Reserved` | 3:1 | 479 | reserved | 0x0 | `0x0` | — | — |
| `RCC_AHB2LPENR` | `0x104` | `SDMMC2LPEN` | 9:9 | 478 | tag | 0x1 | `0x1` | — | — |
| `RCC_AHB2LPENR` | `0x104` | `SRAM1LPEN` | 29:29 | 478 | tag | 0x1 | `0x1` | — | — |
| `RCC_AHB2LPENR` | `0x104` | `SRAM2LPEN` | 30:30 | 478 | tag | 0x1 | `0x1` | — | — |
| `RCC_AHB2LPENR` | `0x104` | `SRAM3LPEN` | 31:31 | 478 | tag | 0x1 | `0x1` | — | — |
| `RCC_AHB4LPENR` | `0x108` | `ADC3LPEN` | 24:24 | 480 | tag | 0x1 | `0x1` | — | — |
| `RCC_AHB4LPENR` | `0x108` | `BDMALPEN` | 21:21 | 480 | tag | 0x1 | `0x1` | — | — |
| `RCC_AHB4LPENR` | `0x108` | `BKPRAMLPEN` | 28:28 | 480 | tag | 0x1 | `0x1` | — | — |
| `RCC_AHB4LPENR` | `0x108` | `CRCLPEN` | 19:19 | 481 | tag | 0x1 | `0x1` | — | — |
| `RCC_AHB4LPENR` | `0x108` | `GPIOALPEN` | 0:0 | 482 | tag | 0x1 | `0x1` | — | — |
| `RCC_AHB4LPENR` | `0x108` | `GPIOBLPEN` | 1:1 | 482 | tag | 0x1 | `0x1` | — | — |
| `RCC_AHB4LPENR` | `0x108` | `GPIOCLPEN` | 2:2 | 482 | tag | 0x1 | `0x1` | — | — |
| `RCC_AHB4LPENR` | `0x108` | `GPIODLPEN` | 3:3 | 481 | tag | 0x1 | `0x1` | — | — |
| `RCC_AHB4LPENR` | `0x108` | `GPIOELPEN` | 4:4 | 481 | tag | 0x1 | `0x1` | — | — |
| `RCC_AHB4LPENR` | `0x108` | `GPIOFLPEN` | 5:5 | 481 | tag | 0x1 | `0x1` | — | — |
| `RCC_AHB4LPENR` | `0x108` | `GPIOGLPEN` | 6:6 | 481 | tag | 0x1 | `0x1` | — | — |
| `RCC_AHB4LPENR` | `0x108` | `GPIOHLPEN` | 7:7 | 481 | tag | 0x1 | `0x1` | — | — |
| `RCC_AHB4LPENR` | `0x108` | `GPIOILPEN` | 8:8 | 481 | tag | 0x1 | `0x1` | — | — |
| `RCC_AHB4LPENR` | `0x108` | `GPIOJLPEN` | 9:9 | 481 | tag | 0x1 | `0x1` | — | — |
| `RCC_AHB4LPENR` | `0x108` | `GPIOKLPEN` | 10:10 | 481 | tag | 0x1 | `0x1` | — | — |
| `RCC_AHB4LPENR` | `0x108` | `Reserved` | 31:30 | 480 | reserved | 0x0 | `0x0` | — | — |
| `RCC_AHB4LPENR` | `0x108` | `Reserved` | 27:25 | 480 | reserved | 0x0 | `0x0` | — | — |
| `RCC_AHB4LPENR` | `0x108` | `Reserved` | 23:22 | 480 | reserved | 0x0 | `0x0` | — | — |
| `RCC_AHB4LPENR` | `0x108` | `Reserved` | 20:20 | 480 | reserved | 0x0 | `0x0` | — | — |
| `RCC_AHB4LPENR` | `0x108` | `Reserved` | 18:11 | 481 | reserved | 0x0 | `0x0` | — | — |
| `RCC_AHB4LPENR` | `0x108` | `SRAM4LPEN` | 29:29 | 480 | tag | 0x1 | `0x1` | — | — |
| `RCC_APB1LLPENR` | `0x110` | `CECLPEN` | 27:27 | 484 | tag | 0x1 | `0x1` | — | — |
| `RCC_APB1LLPENR` | `0x110` | `DAC12LPEN` | 29:29 | 484 | tag | 0x1 | `0x1` | — | — |
| `RCC_APB1LLPENR` | `0x110` | `I2C1LPEN` | 21:21 | 485 | tag | 0x1 | `0x1` | — | — |
| `RCC_APB1LLPENR` | `0x110` | `I2C2LPEN` | 22:22 | 485 | tag | 0x1 | `0x1` | — | — |
| `RCC_APB1LLPENR` | `0x110` | `I2C3LPEN` | 23:23 | 485 | tag | 0x1 | `0x1` | — | — |
| `RCC_APB1LLPENR` | `0x110` | `LPTIM1LPEN` | 9:9 | 486 | tag | 0x1 | `0x1` | — | — |
| `RCC_APB1LLPENR` | `0x110` | `Reserved` | 28:28 | 484 | reserved | 0x0 | `0x0` | — | — |
| `RCC_APB1LLPENR` | `0x110` | `Reserved` | 26:24 | 484 | reserved | 0x0 | `0x0` | — | — |
| `RCC_APB1LLPENR` | `0x110` | `Reserved` | 13:10 | 486 | reserved | 0x2 | `0x2` | — | — |
| `RCC_APB1LLPENR` | `0x110` | `SPDIFRXLPEN` | 16:16 | 485 | tag | 0x1 | `0x1` | — | — |
| `RCC_APB1LLPENR` | `0x110` | `SPI2LPEN` | 14:14 | 486 | acknowledged | 0x0,0x1 | `0x0`, `0x1` | — | — |
| `RCC_APB1LLPENR` | `0x110` | `SPI3LPEN` | 15:15 | 486 | tag | 0x1 | `0x1` | — | — |
| `RCC_APB1LLPENR` | `0x110` | `TIM12LPEN` | 6:6 | 486 | tag | 0x1 | `0x1` | — | — |
| `RCC_APB1LLPENR` | `0x110` | `TIM13LPEN` | 7:7 | 486 | tag | 0x1 | `0x1` | — | — |
| `RCC_APB1LLPENR` | `0x110` | `TIM14LPEN` | 8:8 | 486 | tag | 0x1 | `0x1` | — | — |
| `RCC_APB1LLPENR` | `0x110` | `TIM2LPEN` | 0:0 | 487 | tag | 0x1 | `0x1` | — | — |
| `RCC_APB1LLPENR` | `0x110` | `TIM3LPEN` | 1:1 | 487 | tag | 0x1 | `0x1` | — | — |
| `RCC_APB1LLPENR` | `0x110` | `TIM4LPEN` | 2:2 | 487 | tag | 0x1 | `0x1` | — | — |
| `RCC_APB1LLPENR` | `0x110` | `TIM5LPEN` | 3:3 | 486 | tag | 0x1 | `0x1` | — | — |
| `RCC_APB1LLPENR` | `0x110` | `TIM6LPEN` | 4:4 | 486 | tag | 0x1 | `0x1` | — | — |
| `RCC_APB1LLPENR` | `0x110` | `TIM7LPEN` | 5:5 | 486 | tag | 0x1 | `0x1` | — | — |
| `RCC_APB1LLPENR` | `0x110` | `UART4LPEN` | 19:19 | 485 | tag | 0x1 | `0x1` | — | — |
| `RCC_APB1LLPENR` | `0x110` | `UART5LPEN` | 20:20 | 485 | tag | 0x1 | `0x1` | — | — |
| `RCC_APB1LLPENR` | `0x110` | `UART7LPEN` | 30:30 | 484 | tag | 0x1 | `0x1` | — | — |
| `RCC_APB1LLPENR` | `0x110` | `UART8LPEN` | 31:31 | 484 | tag | 0x1 | `0x1` | — | — |
| `RCC_APB1LLPENR` | `0x110` | `USART2LPEN` | 17:17 | 485 | tag | 0x1 | `0x1` | — | — |
| `RCC_APB1LLPENR` | `0x110` | `USART3LPEN` | 18:18 | 485 | tag | 0x1 | `0x1` | — | — |
| `RCC_APB2LPENR` | `0x118` | `DFSDM1LPEN` | 28:28 | 490 | tag | 0x1 | `0x1` | — | — |
| `RCC_APB2LPENR` | `0x118` | `HRTIMLPEN` | 29:29 | 490 | tag | 0x1 | `0x1` | — | — |
| `RCC_APB2LPENR` | `0x118` | `Reserved` | 31:30 | 490 | reserved | 0x0 | `0x0` | — | — |
| `RCC_APB2LPENR` | `0x118` | `Reserved` | 27:25 | 490 | reserved | 0x0 | `0x0` | — | — |
| `RCC_APB2LPENR` | `0x118` | `Reserved` | 21:21 | 491 | reserved | 0x0 | `0x0` | — | — |
| `RCC_APB2LPENR` | `0x118` | `Reserved` | 19:19 | 491 | reserved | 0x0 | `0x0` | — | — |
| `RCC_APB2LPENR` | `0x118` | `Reserved` | 15:14 | 491 | reserved | 0x0 | `0x0` | — | — |
| `RCC_APB2LPENR` | `0x118` | `Reserved` | 11:6 | 491 | reserved | 0x0 | `0x0` | — | — |
| `RCC_APB2LPENR` | `0x118` | `Reserved` | 3:2 | 492 | reserved | 0x0 | `0x0` | — | — |
| `RCC_APB2LPENR` | `0x118` | `SAI1LPEN` | 22:22 | 491 | tag | 0x1 | `0x1` | — | — |
| `RCC_APB2LPENR` | `0x118` | `SAI2LPEN` | 23:23 | 490 | tag | 0x1 | `0x1` | — | — |
| `RCC_APB2LPENR` | `0x118` | `SAI3LPEN` | 24:24 | 490 | tag | 0x1 | `0x1` | — | — |
| `RCC_APB2LPENR` | `0x118` | `SPI1LPEN` | 12:12 | 491 | acknowledged | 0x0,0x1 | `0x0`, `0x1` | — | — |
| `RCC_APB2LPENR` | `0x118` | `SPI4LPEN` | 13:13 | 491 | tag | 0x1 | `0x1` | — | — |
| `RCC_APB2LPENR` | `0x118` | `SPI5LPEN` | 20:20 | 491 | tag | 0x1 | `0x1` | — | — |
| `RCC_APB2LPENR` | `0x118` | `TIM15LPEN` | 16:16 | 491 | tag | 0x1 | `0x1` | — | — |
| `RCC_APB2LPENR` | `0x118` | `TIM16LPEN` | 17:17 | 491 | tag | 0x1 | `0x1` | — | — |
| `RCC_APB2LPENR` | `0x118` | `TIM17LPEN` | 18:18 | 491 | tag | 0x1 | `0x1` | — | — |
| `RCC_APB2LPENR` | `0x118` | `TIM1LPEN` | 0:0 | 492 | tag | 0x1 | `0x1` | — | — |
| `RCC_APB2LPENR` | `0x118` | `TIM8LPEN` | 1:1 | 492 | tag | 0x1 | `0x1` | — | — |
| `RCC_APB2LPENR` | `0x118` | `USART1LPEN` | 4:4 | 492 | tag | 0x1 | `0x1` | — | — |
| `RCC_APB2LPENR` | `0x118` | `USART6LPEN` | 5:5 | 491 | tag | 0x1 | `0x1` | — | — |
| `RCC_APB4LPENR` | `0x11C` | `COMP12LPEN` | 14:14 | 493 | tag | 0x1 | `0x1` | — | — |
| `RCC_APB4LPENR` | `0x11C` | `I2C4LPEN` | 7:7 | 494 | tag | 0x1 | `0x1` | — | — |
| `RCC_APB4LPENR` | `0x11C` | `LPTIM2LPEN` | 9:9 | 494 | tag | 0x1 | `0x1` | — | — |
| `RCC_APB4LPENR` | `0x11C` | `LPTIM3LPEN` | 10:10 | 494 | tag | 0x1 | `0x1` | — | — |
| `RCC_APB4LPENR` | `0x11C` | `LPTIM4LPEN` | 11:11 | 494 | tag | 0x1 | `0x1` | — | — |
| `RCC_APB4LPENR` | `0x11C` | `LPTIM5LPEN` | 12:12 | 494 | tag | 0x1 | `0x1` | — | — |
| `RCC_APB4LPENR` | `0x11C` | `LPUART1LPEN` | 3:3 | 494 | tag | 0x1 | `0x1` | — | — |
| `RCC_APB4LPENR` | `0x11C` | `RTCAPBLPEN` | 16:16 | 493 | tag | 0x1 | `0x1` | — | — |
| `RCC_APB4LPENR` | `0x11C` | `Reserved` | 31:26 | 493 | reserved | 0x1 | `0x1` | — | — |
| `RCC_APB4LPENR` | `0x11C` | `Reserved` | 25:22 | 493 | reserved | 0x0 | `0x0` | — | — |
| `RCC_APB4LPENR` | `0x11C` | `Reserved` | 20:17 | 493 | reserved | 0x0 | `0x0` | — | — |
| `RCC_APB4LPENR` | `0x11C` | `Reserved` | 13:13 | 493 | reserved | 0x0 | `0x0` | — | — |
| `RCC_APB4LPENR` | `0x11C` | `Reserved` | 8:8 | 494 | reserved | 0x0 | `0x0` | — | — |
| `RCC_APB4LPENR` | `0x11C` | `Reserved` | 6:6 | 494 | reserved | 0x0 | `0x0` | — | — |
| `RCC_APB4LPENR` | `0x11C` | `Reserved` | 4:4 | 494 | reserved | 0x0 | `0x0` | — | — |
| `RCC_APB4LPENR` | `0x11C` | `Reserved` | 2:2 | 495 | reserved | 0x0 | `0x0` | — | — |
| `RCC_APB4LPENR` | `0x11C` | `Reserved` | 0:0 | 495 | reserved | 0x0 | `0x0` | — | — |
| `RCC_APB4LPENR` | `0x11C` | `SAI4LPEN` | 21:21 | 493 | tag | 0x1 | `0x1` | — | — |
| `RCC_APB4LPENR` | `0x11C` | `SPI6LPEN` | 5:5 | 494 | tag | 0x1 | `0x1` | — | — |
| `RCC_APB4LPENR` | `0x11C` | `SYSCFGLPEN` | 1:1 | 495 | tag | 0x1 | `0x1` | — | — |
| `RCC_APB4LPENR` | `0x11C` | `VREFLPEN` | 15:15 | 493 | tag | 0x1 | `0x1` | — | — |

## 5. The order of enable, source and start, per firmware

For each kernel-clocked peripheral of the query, by firmware: the write that first sets its enable, the write after which its kernel clock first runs (the mux settled on a running source), its starts (SPI `CR1.SPE` or `CSTART`; I2C `CR1.PE`; USART `CR1.UE`; RNG `CR.RNGEN`), and the starts that find **no** kernel clock under §2's rule. `#n` is the write's position in that firmware's sequence of RCC and peripheral writes. Times are virtual. `tests/1300/order.py` evaluates the rule as §2 states it.

| Firmware | Peripheral | Bus-clock enable first set | Kernel clock first runs | Starts | Starts with no kernel clock |
|---|---|---|---|---|---|
| ArduPilot | spi1 | #273 00:00:00.161120820 pc 0x08100E12 | #32 00:00:00.000003540 pc 0x081038B2 (SPI123SEL = pll1_q_ck) | 55 (first #282 00:00:00.161120820 (SPI123SEL = pll1_q_ck (running))) | 0 |
| ArduPilot | spi2 | #13573 00:00:04.353100000 pc 0x08100F0C | #32 00:00:00.000003540 pc 0x081038B2 (SPI123SEL = pll1_q_ck) | 7062 (first #13582 00:00:04.353100000 (SPI123SEL = pll1_q_ck (running))) | 0 |
| ArduPilot | i2c1 | #965 00:00:00.164652020 pc 0x08100B02 | #1 00:00:00.000000000 pc none (I2C123SEL = rcc_pclk1) | 2057 (first #969 00:00:00.164652020 (I2C123SEL = pll3_r_ck (running))) | 0 |
| ArduPilot | usart1 | #74 00:00:00.003100000 pc 0x08101204 | #1 00:00:00.000000000 pc none (USART16SEL = rcc_pclk2) | 8 (first #79 00:00:00.003100000 (USART16SEL = pll2_q_ck (running))) | 0 |
| ArduPilot | rng | #67 00:00:00.000932240 pc 0x08103E1C | #23 00:00:00.000000000 pc 0x08103880 (RNGSEL = hsi48_ck) | 1 (first #70 00:00:00.000932240 (RNGSEL = hsi48_ck (running))) | 0 |
| d1-drv8876 | usart1 | #15 00:00:00.000000000 pc 0x080008BA | #1 00:00:00.000000000 pc none (USART16SEL = rcc_pclk2) | 1 (first #20 00:00:00.000009580 (USART16SEL = rcc_pclk2 (running))) | 0 |
| driver-judges-nothing | usart1 | #14 00:00:00.000000000 pc 0x080006E6 | #1 00:00:00.000000000 pc none (USART16SEL = rcc_pclk2) | 1 (first #18 00:00:00.000000000 (USART16SEL = rcc_pclk2 (running))) | 0 |
| hse-crystal-16 | spi1 | #20 00:00:00.000003900 pc 0x08001980 | #15 00:00:00.000002780 pc 0x08001980 (SPI123SEL = pll1_q_ck) | 2 (first #49 00:00:00.000044010 (SPI123SEL = pll1_q_ck (running))) | 0 |
| hse-crystal-16 | spi2 | #21 00:00:00.000003900 pc 0x08001980 | #15 00:00:00.000002780 pc 0x08001980 (SPI123SEL = pll1_q_ck) | 10 (first #58 00:00:00.000044010 (SPI123SEL = pll1_q_ck (running))) | 0 |
| hse-crystal-16 | i2c1 | #19 00:00:00.000003900 pc 0x08001980 | #1 00:00:00.000000000 pc none (I2C123SEL = rcc_pclk1) | 1 (first #28 00:00:00.000003900 (I2C123SEL = rcc_pclk1 (running))) | 0 |
| hse-external-8 | spi1 | #21 00:00:00.000004050 pc 0x08001994 | #16 00:00:00.000002930 pc 0x08001994 (SPI123SEL = pll1_q_ck) | 2 (first #50 00:00:00.000044160 (SPI123SEL = pll1_q_ck (running))) | 0 |
| hse-external-8 | spi2 | #22 00:00:00.000004050 pc 0x08001994 | #16 00:00:00.000002930 pc 0x08001994 (SPI123SEL = pll1_q_ck) | 10 (first #59 00:00:00.000044160 (SPI123SEL = pll1_q_ck (running))) | 0 |
| hse-external-8 | i2c1 | #20 00:00:00.000004050 pc 0x08001994 | #1 00:00:00.000000000 pc none (I2C123SEL = rcc_pclk1) | 1 (first #29 00:00:00.000004050 (I2C123SEL = rcc_pclk1 (running))) | 0 |
| motors-dshot600 | spi1 | #14 00:00:00.000000000 pc 0x08000B14 | never | 2 (first #27 00:00:00.000015760 (SPI123SEL pending: input pll1_q_ck -> per_ck)) | **2**, first #27 00:00:00.000015760 SPI123SEL pending: input pll1_q_ck -> per_ck |
| motors-dshot600 | i2c1 | #13 00:00:00.000000000 pc 0x08000B14 | #1 00:00:00.000000000 pc none (I2C123SEL = rcc_pclk1) | 1 (first #18 00:00:00.000000000 (I2C123SEL = rcc_pclk1 (running))) | 0 |
| motors-pwm | spi1 | #14 00:00:00.000000000 pc 0x08000B1E | never | 2 (first #27 00:00:00.000015760 (SPI123SEL pending: input pll1_q_ck -> per_ck)) | **2**, first #27 00:00:00.000015760 SPI123SEL pending: input pll1_q_ck -> per_ck |
| motors-pwm | i2c1 | #13 00:00:00.000000000 pc 0x08000B1E | #1 00:00:00.000000000 pc none (I2C123SEL = rcc_pclk1) | 1 (first #18 00:00:00.000000000 (I2C123SEL = rcc_pclk1 (running))) | 0 |
| sentinel-01 | spi1 | #15 00:00:00.000000000 pc 0x08000BD6 | never | 2 (first #47 00:00:00.000023760 (SPI123SEL pending: input pll1_q_ck -> per_ck)) | **2**, first #47 00:00:00.000023760 SPI123SEL pending: input pll1_q_ck -> per_ck |
| sentinel-01 | i2c1 | #13 00:00:00.000000000 pc 0x08000BD6 | #1 00:00:00.000000000 pc none (I2C123SEL = rcc_pclk1) | 1 (first #19 00:00:00.000000000 (I2C123SEL = rcc_pclk1 (running))) | 0 |
| sentinel-01 | i2c2 | #14 00:00:00.000000000 pc 0x08000BD6 | #1 00:00:00.000000000 pc none (I2C123SEL = rcc_pclk1) | 1 (first #22 00:00:00.000000000 (I2C123SEL = rcc_pclk1 (running))) | 0 |
| sentinel-01 | usart1 | #16 00:00:00.000000000 pc 0x08000BD6 | #1 00:00:00.000000000 pc none (USART16SEL = rcc_pclk2) | 1 (first #29 00:00:00.000000000 (USART16SEL = rcc_pclk2 (running))) | 0 |
| sentinel-01-ext-bmp581 | spi1 | #15 00:00:00.000000000 pc 0x08000E90 | never | 2 (first #56 00:00:00.000027470 (SPI123SEL pending: input pll1_q_ck -> per_ck)) | **2**, first #56 00:00:00.000027470 SPI123SEL pending: input pll1_q_ck -> per_ck |
| sentinel-01-ext-bmp581 | i2c1 | #13 00:00:00.000000000 pc 0x08000E90 | #1 00:00:00.000000000 pc none (I2C123SEL = rcc_pclk1) | 1 (first #19 00:00:00.000000000 (I2C123SEL = rcc_pclk1 (running))) | 0 |
| sentinel-01-ext-bmp581 | i2c2 | #14 00:00:00.000000000 pc 0x08000E90 | #1 00:00:00.000000000 pc none (I2C123SEL = rcc_pclk1) | 1 (first #22 00:00:00.000000000 (I2C123SEL = rcc_pclk1 (running))) | 0 |
| sentinel-01-ext-bmp581 | usart1 | #16 00:00:00.000000000 pc 0x08000E90 | #1 00:00:00.000000000 pc none (USART16SEL = rcc_pclk2) | 1 (first #29 00:00:00.000000000 (USART16SEL = rcc_pclk2 (running))) | 0 |
| sentinel-01-minimal | spi1 | #14 00:00:00.000000000 pc 0x08000AE6 | never | 2 (first #43 00:00:00.000021330 (SPI123SEL pending: input pll1_q_ck -> per_ck)) | **2**, first #43 00:00:00.000021330 SPI123SEL pending: input pll1_q_ck -> per_ck |
| sentinel-01-minimal | i2c1 | #13 00:00:00.000000000 pc 0x08000AE6 | #1 00:00:00.000000000 pc none (I2C123SEL = rcc_pclk1) | 1 (first #18 00:00:00.000000000 (I2C123SEL = rcc_pclk1 (running))) | 0 |
| sentinel-01-minimal | usart1 | #15 00:00:00.000000000 pc 0x08000AE6 | #1 00:00:00.000000000 pc none (USART16SEL = rcc_pclk2) | 1 (first #25 00:00:00.000000000 (USART16SEL = rcc_pclk2 (running))) | 0 |
| sentinel-02-bench | spi1 | #15 00:00:00.000000000 pc 0x08000C16 | never | 10 (first #32 00:00:00.000015040 (SPI123SEL pending: input pll1_q_ck -> per_ck)) | **10**, first #32 00:00:00.000015040 SPI123SEL pending: input pll1_q_ck -> per_ck |
| sentinel-02-bench | i2c1 | #14 00:00:00.000000000 pc 0x08000C16 | #1 00:00:00.000000000 pc none (I2C123SEL = rcc_pclk1) | 1 (first #18 00:00:00.000000000 (I2C123SEL = rcc_pclk1 (running))) | 0 |
| sentinel-02-bench-hse8 | spi1 | #22 00:00:00.000004050 pc 0x08000E16 | #16 00:00:00.000002930 pc 0x08000E16 (SPI123SEL = pll1_q_ck) | 10 (first #39 00:00:00.000017670 (SPI123SEL = pll1_q_ck (running))) | 0 |
| sentinel-02-bench-hse8 | i2c1 | #21 00:00:00.000004050 pc 0x08000E16 | #1 00:00:00.000000000 pc none (I2C123SEL = rcc_pclk1) | 1 (first #25 00:00:00.000004050 (I2C123SEL = rcc_pclk1 (running))) | 0 |
| sentinel-02-shaped | spi1 | #14 00:00:00.000000000 pc 0x080014A4 | never | 2 (first #42 00:00:00.000040030 (SPI123SEL pending: input pll1_q_ck -> per_ck)) | **2**, first #42 00:00:00.000040030 SPI123SEL pending: input pll1_q_ck -> per_ck |
| sentinel-02-shaped | spi2 | #15 00:00:00.000000000 pc 0x080014A4 | never | 10 (first #51 00:00:00.000040030 (SPI123SEL pending: input pll1_q_ck -> per_ck)) | **10**, first #51 00:00:00.000040030 SPI123SEL pending: input pll1_q_ck -> per_ck |
| sentinel-02-shaped | i2c1 | #13 00:00:00.000000000 pc 0x080014A4 | #1 00:00:00.000000000 pc none (I2C123SEL = rcc_pclk1) | 1 (first #21 00:00:00.000000000 (I2C123SEL = rcc_pclk1 (running))) | 0 |
| sentinel-03 | spi1 | #17 00:00:00.000000000 pc 0x08001784 | never | 2 (first #61 00:00:00.000070990 (SPI123SEL pending: input pll1_q_ck -> per_ck)) | **2**, first #61 00:00:00.000070990 SPI123SEL pending: input pll1_q_ck -> per_ck |
| sentinel-03 | spi2 | #18 00:00:00.000000000 pc 0x08001784 | never | 10 (first #70 00:00:00.000070990 (SPI123SEL pending: input pll1_q_ck -> per_ck)) | **10**, first #70 00:00:00.000070990 SPI123SEL pending: input pll1_q_ck -> per_ck |
| sentinel-03 | i2c1 | #15 00:00:00.000000000 pc 0x08001784 | #1 00:00:00.000000000 pc none (I2C123SEL = rcc_pclk1) | 1 (first #25 00:00:00.000000000 (I2C123SEL = rcc_pclk1 (running))) | 0 |
| sentinel-03 | i2c2 | #16 00:00:00.000000000 pc 0x08001784 | #1 00:00:00.000000000 pc none (I2C123SEL = rcc_pclk1) | 1 (first #28 00:00:00.000000000 (I2C123SEL = rcc_pclk1 (running))) | 0 |
| sentinel-03 | usart1 | #20 00:00:00.000000000 pc 0x08001784 | #1 00:00:00.000000000 pc none (USART16SEL = rcc_pclk2) | 1 (first #38 00:00:00.000000000 (USART16SEL = rcc_pclk2 (running))) | 0 |
| serial-dshot-9600 | spi1 | #14 00:00:00.000000000 pc 0x08000CCE | never | 2 (first #44 00:00:00.000024770 (SPI123SEL pending: input pll1_q_ck -> per_ck)) | **2**, first #44 00:00:00.000024770 SPI123SEL pending: input pll1_q_ck -> per_ck |
| serial-dshot-9600 | i2c1 | #13 00:00:00.000000000 pc 0x08000CCE | #1 00:00:00.000000000 pc none (I2C123SEL = rcc_pclk1) | 1 (first #19 00:00:00.000000000 (I2C123SEL = rcc_pclk1 (running))) | 0 |
| serial-dshot-9600 | usart1 | #16 00:00:00.000000000 pc 0x08000CCE | #1 00:00:00.000000000 pc none (USART16SEL = rcc_pclk2) | 1 (first #26 00:00:00.000000000 (USART16SEL = rcc_pclk2 (running))) | 0 |

**Reading.** ArduPilot starts PLL1, PLL2 and PLL3 (`RCC_CR` `0x15003025` at `pc 0x081038B2`) before it writes the selectors, and every start finds its clock. The HSE fixtures start PLL1 and leave `SPI123SEL` at `pll1_q_ck`. **The nine `per_ck` boards switch `SPI123SEL` to `per_ck` with PLL1 never started**, so by p371–372 the mux keeps its input `pll1_q_ck`, which has no clock, and SPI1 (and SPI2, where enabled) starts with no kernel clock. The RCC guard refuses none of this: enabling a bus clock is never refused (WO (4)). It is what `TVI-WO-1300a`'s SPI start would refuse as `predicted-fault`.

**Ruled by the architect (13:45Z): not a stop; build on.** The bench has not settled it: no generated `per_ck` firmware has run SPI1 on silicon since the `SPI123SEL` change, and the 3302 bench (before that change) showed `SPI123SEL` = `pll1_q_ck` with PLL1 off and no kernel clock. A Nucleo check is asked of Scotty, and an Aethelgard work order will keep the generator from relying on a mux switching away from a dead input.

## 6. For the architect: what to acknowledge, and what to rule

### 6.1 Fields outside the draft allow-list, by name

Each was given a value the model does not implement. Acknowledge each by name, or rule it refused. A lean is given where I have one.

| # | Field(s) | Given by | The stock model | Silicon (page) | Lean |
|---|---|---|---|---|---|
| F1 | `RCC_PLLCFGR.PLL1RGE`, `PLL2RGE`, `PLL3RGE` (`11`; `hse-crystal-16`: PLL1 `01`), `PLL1FRACEN`, `PLL2FRACEN`, `PLL3FRACEN` (`1`), `PLL1VCOSEL` (`hse-external-8`, `sentinel-02-bench-hse8`: `1`), `PLL3VCOSEL` (`1`) | ArduPilot; `hse-crystal-16`; `hse-external-8`; `sentinel-02-bench-hse8` | tags: dropped, read their reset | input range, fractional mode and VCO range (p401–402). They set frequency and lock range, not whether the output runs | acknowledge: the twin models no PLL frequency beyond PLL1's integer one |
| F2 | `RCC_CFGR.MCO1PRE` (`0100`), `MCO2PRE` (`0100`), `RTCPRE` (`001000`) | ArduPilot | tags | MCO pin prescalers and the RTC's HSE divider (p391–392) | acknowledge: no MCO pin and no RTC clock are modeled; with `MCOx` left at reset, no pin is driven |
| F3 | `RCC_CR.HSEBYP` (`1`) | `hse-external-8`, `sentinel-02-bench-hse8` | tag | the HSE is an external clock on OSC_IN, not a crystal (p384, p340) | acknowledge: the twin has no oscillator, so bypass and crystal behave alike. `HSEBYP` "can be written only if the HSE oscillator is disabled" (p384): the order is not checked |
| F4 | the `Reserved` bits of `RCC_AHB3RSTR`, `AHB1RSTR`, `AHB2RSTR`, `AHB4RSTR`, `APB3RSTR`, `APB1LRSTR`, `APB1HRSTR`, `APB2RSTR`, `APB4RSTR`, written `1` | ArduPilot (all-ones resets, at `0x081036EC`–`0x081037B8`) | reserved: dropped | "must be kept at reset value" (§8.7.28–8.7.36, p430–445) | acknowledge: no flop is there on either side |
| F5 | `RCC_RSR` bit 18 (Reserved, reset `1` by the register's `0x00FE0000`), written `0` | ArduPilot (`RMVF` = 1, `0x081032BE`) | reserved | p450–451 | acknowledge, as F4 |
| F6 | `RCC_AHB1LPENR.DMA2LPEN`, `AHB2LPENR.RNGLPEN`, `APB1LLPENR.SPI2LPEN`, `APB2LPENR.SPI1LPEN`, written `0` | ArduPilot (2,353 times for `SPI2LPEN`, beside each `SPI2EN`) | tags reading `1`: the twin never gates a clock in CSleep | `0`: the peripheral's clocks stop in CSleep (Table 62, p376) | acknowledge only with a backlog row: the twin does not model CSleep, so a transfer in flight across a `WFI` keeps running in the twin. Or rule refused, which refuses ArduPilot's boot at `0x08100706` |
| F7 | `RCC_APB1LENR.USART2EN`, `USART3EN` (`1`) | `hse-crystal-16`, `hse-external-8`, `sentinel-02-shaped`, `sentinel-03` | the stock bus enable | kernel-clocked by `USART234578SEL` (p417) | **extend the query** to USART2 and USART3 (one more mux; `rcc_pclk1` at reset, so these boards' starts would find their clock), rather than refuse four generated boards |
| F8 | `RCC_AHB4ENR.ADC3EN` (`1`) | `d1-drv8876` | the stock bus enable | kernel-clocked by `ADCSEL` (p419): `00` is `pll2_p_ck`, and this board never starts PLL2. The ADC's own `CKMODE` can instead select the bus clock (an ADC register, outside the RCC) | rule: `unmodeled` until an ADC guard asks the query; or extend the query to ADC3 now, which needs the ADC's `CKMODE` (outside this WO's fence) |

The draft also has rows no firmware gives a value outside of. They are in `model/GuardedSTM32H7_RCC.cs`'s `Fields` when built, generated from the RM0433 field list and the stock model's kinds, with the guard's fields overriding. Of the 682 rows: 24 are the guard's own, 22 are selectors it stores, 179 are inherited, 37 are kernel-clocked enables outside the query, 170 are tags, 210 are reserved and 40 are read-only. **Proposed for acknowledgment as a class:** the 22 stored selectors (`QSPISEL`, `SPI45SEL`, `FDCANSEL`, `USART234578SEL`, `I2C4SEL`, `SPI6SEL`, `ADCSEL`, `USBSEL`, `CECSEL` and the rest). ArduPilot writes nine of them non-zero, and the stock model drops them (1150 (v)'s tier 1). The guard stores and serves them. Their peripherals' kernel clocks are not modeled, and enabling those peripherals refuses (the `kernel-unmodeled` rows).

### 6.2 The rule and its kinds

- **`TVI-RCC-FIELD`** (`unmodeled`): the allow-list, and a write to an offset with no register (Table 84, p496–505).
- **`TVI-RCC-WIDTH`** (`unmodeled`): any access but an aligned 32-bit one. §8.7 states no width, and both firmwares use only 32-bit.
- **`TVI-RCC-READ`** (`unmodeled`): a read of an offset with no register.
- **`TVI-RCC-PLL`** (`predicted-fault`): a change to `PLLSRC`, `DIVMx` or `DIVxyEN` while the PLL it feeds is on, which silicon ignores (§2.4). Neither firmware does it.
- **`TVI-RCC-KERNEL`**: the query's own record, for the test here and for 1300a. It is `predicted-fault` when a modeled peripheral starts with no kernel clock, naming the selector, its source and the pages, and `unmodeled` for a peripheral the query does not cover.
- **Proposed, not in the WO: `PLLxON` set with no reference.** That is `PLLSRC` = `11`, its source off, or `DIVMx` = 0. The stock model then reports `PLLxRDY` = 1 (§3), and silicon would not lock. As `unmodeled`. Neither firmware does it.
- **No record field beyond 0100b's is needed.** Every rule fits `rule`, `kind`, `register`, `bit`, `condition`, `citation`, `state` and `access` (§7's stop condition is not met).

### 6.3 Reads (RUL-17)

From the copies: `PLLCKSELR`, `PLLCFGR`'s `DIVxyEN`, the four `*CCIP*R`, and the nine `*ENR` as written. Everything else comes from the stock model, which computes `RDY` from `ON`. The aliases at 0x130–0x17C read as 0x0D0–0x11C, so the 0x17C crash is gone (measured on the draft guard: the read returns `0x0421DEAA`, `APB4LPENR`'s reset). **A consumer-visible change:** the stock model's `*ENR` reads whether each registered peripheral is enabled on the bus, which is 1 from reset (§3), and the copy reads what was written. After the consumer's cold writes, the two should agree for every registered peripheral (reasoned, not measured). They differ for a bit with no registered peripheral, which the stock model reads 0. Aethelgard's boot block reads all nine `*ENR`.

### 6.4 The consumer side (Aethelgard, Phase 2), measured, not changed

1. **Its platform registers its own `spi1` and `spi2` on `rcc`** (`sentinel-02-own`'s `platform-booted.repl` L17, L25). These are the n3 trap's other form: each must name the guard's variable. The replay repoints them. **Three of the 15 consumer platforms carry an untyped `rcc:` update** (AET-WO-4240b's HSE): `hse-crystal-16` L161–162 `hseFrequency: 16000000`, and `hse-external-8` L161–162 and `sentinel-02-bench-hse8` L109–110 `hseFrequency: 8000000`. With the stock `rcc` cancelled, each binds to the cancelled variable with no error, and the guard keeps its default 8 MHz. **Measured:** in this replay, `hse-crystal-16`'s wrapper ran with 8 MHz, not 16. Its boot block still matched stock, so this window does not show the difference. The consumer must emit these properties on the guard's entry. The CPU chain (`stm32h7.repl`, `stm32h743.repl`, `stm32h753.repl`) has none. **The stock Nucleo board has one:** `platforms/boards/nucleo_h753zi.repl` (sha256 `979d7175…`) L31–32 is `rcc:` / `hseFrequency: 8_000_000`. That binds to the cancelled variable and never reaches the guard. The guard's default is also 8 MHz, so nothing changes today, but Tviburi's board description restates `hseFrequency: 8_000_000` on the guard rather than relying on that. A consumer that sets `hseFrequency` (Aethelgard's HSE fixtures run 16 MHz and 8 MHz) must set it on the guard's entry.
2. **Its access log of `sysbus.rcc`** (generated boots turn it on) loses register names: `rcc: WriteUInt32 to 0x54 (unknown)`.
3. **The stock RCC's own warnings lose their source.** On ArduPilot's boot, 2,403 `rcc: Unhandled write … Tags: …` lines become 2,369 sourceless lines (1150 (vii)'s price, counted).
4. **The console grows** with the overlay, from 8,312,549 to 9,568,018 B on ArduPilot. That growth is the SPI guard's tag warnings (7,130 → 16,616 lines), not the RCC's.

### 6.5 Fence: the runner

`scripts/tviburi-spike` passes only `spi1` to the check, and 0100b's check fails any record from a source it was not given. With the board's `rcc` as the guard, **every runner-based test would fail** on `rcc`'s witness. That needs `spi1 rcc` on the runner's check line, in `scripts/`, outside my fence (I hold one `prelude.resc` line). Or it waits for 1200, whose prelude changes too. Your call.

### 6.6 Bench

The scar row is the generated firmware's SPI1 on `per_ck` with PLL1 never started (§5). It is unproven either way, and the architect has asked Scotty for a Nucleo check (13:45Z).

## 7. The one-file overlay's cost on ArduPilot's full boot (WO §1.1)

The one file is the release constant (1150's template), 0100b's `Refusal.cs` with recorded mode, 0100b's `GuardedSTM32H7_SPI.cs` and the RCC wrapper, with the `using` lines hoisted (`tests/1300/onefile.sh`). `spi1`/`spi2` are guarded, and `rcc` is the wrapper, recorded, with its 58 registrations restated. The measure is the reaper's: the peak over 100 ms samples of Renode's summed VmRSS.

| Run | Peak KiB | Against 1,048,576 | Instructions |
|---|---|---|---|
| stock | 879,624; 862,784 | 82–84 % | 88,058,789 |
| one file: models + RCC wrapper + constant | **969,328; 967,864** | **92.3–92.4 %: under by 77–79 MiB** | 88,058,789 |
| one file without the RCC wrapper (SPI guard only) | 985,024 | 93.9 % | 88,058,789 |

The RCC wrapper adds nothing measurable beyond the one compilation. Host load was 2.0–2.2. The built guard (the allow-list's 682 rows and the mux model) is measured again at acceptance.

## 8. The rulings, and what the build does with each

| Ruled (architect) | In the build |
|---|---|
| 13:10Z: p371–372 is part of the rule | each mux's current input (`MuxState.Current`), switched only when both inputs run, kept across `machine Reset`; cases `stuck`, `switch`, `switched`, `resetfwd`; mutants C01–C03 |
| 13:10Z: the `RCC_C1_*` aliases are their base registers | `Base()`; cases `alias`, `alias17c`; mutant C13 |
| 13:45Z: the per_ck boards are not a stop | §5; case `gen-s03` records that SPI1 has no kernel clock there, without an RCC refusal |
| 14:35Z F1–F6, acknowledged by name | rows of class "acknowledged by name" whose `behaves as` ends `(A1)`…`(A6)`: **A1** = F1, **A2** = F2, **A3** = F3, **A4** = F4, **A5** = F5, **A6** = F6 (A6 carries the backlog row: CSleep is not modeled) |
| 2026-11-25T03:03Z (`TVI-WO-1300b` Q1): **A6** extended | A6 also acknowledges `RCC_AHB1LPENR.ADC12LPEN`, `RCC_AHB4LPENR.ADC3LPEN`, `RCC_APB1LLPENR.USART2LPEN` and `RCC_APB2LPENR.TIM1LPEN` written 0 (p477, p480, p485, p492), the armed Sentinel-03 run's, with F6's reason; recorded here by `TVI-WO-1300c` (granted 2026-11-26T04:03Z Q8 (ii)) so that their ledger `detail` names them |
| 14:35Z F7: the query covers USART2 and USART3 | `USART234578SEL` (p417) as a sixth mux; `usart2`/`usart3` in the query; case `usart2` |
| 14:35Z F8: `ADC3EN` `unmodeled` | the row allows 0 only; `d1-drv8876`'s generated firmware therefore refuses at `AHB4ENR = 0x01000023` |
| 14:35Z: the 22 non-query selectors stored and served | rows "modeled by the guard", any value; their peripherals' enables allow 0 only |
| 14:35Z: the kinds; `PLLxON` with no reference `unmodeled` | `TVI-RCC-FIELD`, `-WIDTH`, `-READ`, `-PLL-REF`, `-SW` (`unmodeled`); `TVI-RCC-PLL`, `TVI-RCC-KERNEL` (`predicted-fault`). `TVI-RCC-SW` was added in the build (§8.1) |
| 14:35Z: `hseFrequency` must reach the wrapper | the guard takes every property the stock RCC takes and passes it to the owned stock model; `tests/1300/hse16.repl` (16 MHz on `tviburiRcc`) reads 16 MHz back. `hse16-trap.repl` (the consumer's untyped `rcc:` form) still lands on the cancelled entry with no error: **the consumer requirement for Phase 2, RUL-09 (b): every RCC property goes on the guard's entry** |
| 14:35Z: the runner checks `spi1 rcc` | `scripts/tviburi-spike` |

### 8.1 Found in the build

- **`TVI-RCC-SW`** (`unmodeled`): `CFGR.SW` switched to a source that is not running. Silicon switches "only if the target clock source is ready" (§8.5.6 p350; `SW` p393). The stock model switches at once, and with PLL1 selected and `DIVM1` = 0 it divides by zero (`STM32H7_RCC.cs` L108–114). Neither firmware's sequence does it.
- **One register per offset:** the release's ledger requires it. 0x004 is `RCC_HSICFGR` (revision V; from memory, the revision of current H753 silicon). `RCC_ICSCR` (revision Y, what the stock model defines) is not a row; both firmwares write 0x004's shared reset value, `0x40000000`.
- **`RCC_CSICFGR`'s Reserved range is 23:10,** not the printed 23:8, which overlaps `CSICAL[9:0]` (p390).
