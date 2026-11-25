# TVI-WO-1305a — measurement

**Builder:** Claude (Opus 5.5), Furnace, 2026-11-24. **Branch:** `wo/tvi-1305a-usart-followups`, from `main` `d9bb3da`.
**State:** predictions (§0), committed before any run or log read of this work order.
**RM0433:** Rev 9, `~/.cache/aet-held/rm0433-rev9.txt`, pages by `tests/1300/rmpage.sh`.

## 0. Predictions, committed before any run

**Seen before writing them** (so these are not blind on those points):
- `TVI-WO-1305.measure.md` §2.1 and FEEDBACK §6 Q2: `usart2` is started with `BRR` = 0 one write before `BRR` `0` → `0x271` with `UE` = 1 at 0.2018 s; ArduPilot's RC-protocol probing cycles `usart2` every second; "nothing is injected on `usart2`" in the armed run (§5 Q2 (c));
- ArduPilot's fixture platform declares `frequency: 72000000` on every USART, and its `BRR` values are 115,200 … 2,000,000 baud at 72 MHz;
- the 14 generated boots' `BRR` values (`0x22C`, `0x457`, `0x98`), which are 115,200, 57,600 and about 420,000 baud at 64 MHz;
- the RCC guard's `QueryKernelClock`: it answers the source and whether it runs, never a frequency. `rcc_pclk1`/`rcc_pclk2` always run. No PLL2/PLL3 frequency is modeled (roadmap, the RCC rows). The flash guard computes `rcc_aclk` from the RCC guard's registers (`HSIDIV`, `SW`, PLL1, `D1CPRE`, `HPRE`).

Not read before these predictions: any line of `1305`'s armed-run console (`~/.cache/tvi-1305/armed/run*/`), its injection script, or any RCC write of ArduPilot's.

### (1) `BRR` below 16 at a start, in ArduPilot's armed run (ELF `dbf598d8…`)

- **P1.** Exactly one start (`CR1` written with `UE` and `TE` or `RE`, from a state with neither) on any port has `BRR` < 16 in force: `usart2`, at about 0.2017 s, with `BRR` = 0. `usart1` and `usart3` never start with `BRR` < 16.
- **P2.** The next write to `usart2` after that start is `BRR` = `0x271`, with `UE` = 1 (ChibiOS's `usart_init` on an enabled port). Reads of `ISR` may fall between; nothing else is written.
- **P3.** No byte moves on `usart2` while the twin's `BRR` < 16: no `TDR` write (by the CPU or a DMA stream), no `RDR` read, and no byte injected.
- **P4.** **The silicon window is wider.** If silicon ignores `BRR` written with `UE` = 1 (pp. 2082, as `U1` asks), `usart2` holds `BRR` = 0 on silicon until the next `BRR` written with `UE` = 0. I predict that write follows ChibiOS's first `sdStop` (`CR1` = 0) on `usart2`, near 1.2 s, and that no byte moves on `usart2` in that wider window either (no `TDR` write, no `RDR` read, nothing injected).
- **P5.** No `usart2` DMA stream (requests 43, 44) moves a byte in either window.

**If P3 or P5 fails, §7's stop fires** (a byte moving under `BRR` < 16). If only P4's wider window carries a byte, that is reported as a finding against `U1`'s reading, not as the stop: the WO's question is about the twin's `BRR`.

### (3) The kernel clock behind `IDLE`

- **P6.** ArduPilot selects a PLL output for its USART kernel clocks (`USART16SEL`, `USART234578SEL` = `pll3_q_ck` or `pll2_q_ck`, at 72 MHz). Low confidence, from memory of ChibiOS's H7 configuration. **If so,** the RCC guard models no PLL2/PLL3 frequency, so "no rule, `unmodeled`" would refuse the armed run's first received frame: §7's second stop (the armed run no longer arms) would fire, from the WO's own rule. I would report it before building (3).
- **P7.** The 14 generated firmwares leave the USART selectors at reset (`rcc_pclk1`/`rcc_pclk2`), with `sys_ck` on the HSI at 64 MHz and every prescaler at 1, so their kernel clock is 64 MHz against the stock 200 MHz (`usart1`) or 125 MHz.

## 1. (1) measured: no byte moves under `BRR` < 16

**Material** (read-only): `1305`'s armed run on its final tree, `~/.cache/tvi-1305/armed/run2/` (release `sha256:5039d0bb…`; `usart1`–`usart3` and `dma1` access-logged; 14 s), and `run1` (release `977ee87a…`, the same tree before its ledger commits). The injection script, `inj/inject.resc`: 837 `WriteChar`s, every one to `usart1`, from 6 s. The tracker reads the access log, keeps each port's `BRR` twice (the twin's, which takes every write; and silicon's per p2082, where a write with `UE` = 1 is ignored and a `CR1` write clearing `UE` is judged as with `UE` = 0), and lists every start (`UE` with `TE` or `RE` from neither) and every `TDR` write or `RDR` read while either `BRR` < 16. A byte a DMA stream moves appears in the access log as the port's `TDR` write or `RDR` read (`1303`).

**The answer: no byte moves, transmitted or received, while `BRR` < 16, in either reading.** Both runs give the same result.

| Prediction | Measured |
|---|---|
| P1: one start with `BRR` < 16 | **two**, both `usart2`, both `BRR` = 0, both at 0.201709480 s: ChibiOS's `usart_init` (`CR1` `0x12D` at PC `0x08105B88`), then `sdStop` (`CR1`, `CR2`, `CR3` = 0), then `usart_init` again. 35 starts in the run: 33 on `usart2`, one each on `usart1` and `usart3`, which start with `BRR` = `0x271` |
| P2: the next write is `BRR` = `0x271` | the next write **after the second start** is `BRR` = `0x271` with `UE` = 1, at 0.2018 s. Between them: reads of `CR2`, `CR3`, `CR1` (×1), `CR2`, `CR3`, and on `dma1` stream 1 `S1CR` read and written 0 (`EN` = 0), `LIFCR`, `S1FCR`, and `S1PAR` = `0x40004424` (`usart2`'s `RDR`) |
| P3: no byte in the twin's window | **right:** no `TDR` write, no `RDR` read, nothing injected. `CR3` = `0x1` in both starts (`EIE`; no `DMAR`, no `DMAT`), and stream 1 stays disabled |
| P4: the silicon window ends near 1.2 s, with no byte | **right:** silicon's `BRR` stays 0 from 0.2017 s until `BRR` = `0x271` is written with `UE` = 0 at 1.20383 s (PC `0x08105B6E`); no byte in it |
| P5: no `usart2` DMA move | **right**: `usart2` has no `TDR` write and no `RDR` read **in the whole 14 s run**; `usart1` has 2,908 `TDR` writes and `usart3` 430 |

**The measured pattern,** as the guard acknowledges it: on one port, a start with `BRR` = 0 and `CR3` without `DMAR`/`DMAT` is followed, before any byte moves, by a `BRR` write of 16 or more, with or without a stop and a second such start in between. Nothing is transmitted, received or read from `RDR` until that write.

**For `U1` (a finding, not the stop):** RM0433 p2035 also says "The baud counters are updated to the new value in the baud registers after a write operation to USART_BRR. Hence the baud rate register value should not be changed during communication." Beside p2082's "can only be written when the USART is disabled (UE = 0)", that is a second reading of a `BRR` write with `UE` = 1. Bench rows `U1` and `U2` decide between them. Under p2082's reading, `usart2` runs on silicon with `BRR` = 0 for a full second (0.2017–1.2038 s), and nothing moves in that second either.

## 2. (3) measured: the kernel clocks behind each firmware's ports

| Firmware | Selectors | Source | Its frequency | Declared `frequency` |
|---|---|---|---|---|
| ArduPilot, armed (`dbf598d8…`) | `USART16SEL` = 001, `USART234578SEL` = 001 (the RCC guard's record state, from 0.97 ms) | `pll2_q_ck` | from `PLLCKSELR` = `0x00408040` (`PLLSRC` HSI, `DIVM2` = 8) and `PLL2DIVR` | 72 MHz |
| the 14 generated | reset (000): Aethelgard's `reset_clock` requires `D2CCIP2R & 0x303F` = 0, `HSIDIV` = 0, `SWS` = 0 and every prescaler at reset (`sentinel-03/gen/board/src/board.rs`) | `rcc_pclk1`/`rcc_pclk2` from `sys_ck` = HSI | 64 MHz | 64 MHz |
| **corrected 2026-11-25 (the identity replay, FEEDBACK §3.1): `hse-crystal-16`, `hse-external-8`** | reset (000) | `rcc_pclk1`/`rcc_pclk2` from `sys_ck` = **PLL1's P output from the HSE** (`hse-crystal-16`: `PLLCKSELR` `0x52`, `DIVM1` 5; `PLL1DIVR` `0x0103064F`, ×80, /4; `CFGR.SW` = PLL1; `hseFrequency: 16000000` declared) | 64 MHz | 64 MHz |

- **P6: right** (`pll2_q_ck`). The RCC guard's query answers that it runs, and no frequency.
- **P7: right for 12 boards on the source and the 64 MHz, wrong for two:** `hse-crystal-16` and `hse-external-8` run `sys_ck` from PLL1 on the HSE. I had read only `sentinel-03`'s source; the identity replay showed it. Wrong on the stock value too: the generated platforms declare 64 MHz, not 200 or 125 MHz.
- **The stock model narrows `PLL2DIVR`** (`STM32H7_RCC.cs` L373–386 in the Renode tree `1305` read): `DIVN2` 8 bits and `DIVQ2` 6 bits, against 9 and 7 on silicon (p407). A read-back of `PLL2DIVR` through the RCC guard returns the stock model's bits (the guard keeps no served copy of it), so a written `DIVN2` ≥ 256 or `DIVQ2` ≥ 64 would read back wrong, silently.
