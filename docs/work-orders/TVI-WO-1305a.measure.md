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
