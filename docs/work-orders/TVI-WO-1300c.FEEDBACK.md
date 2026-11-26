# TVI-WO-1300c — FEEDBACK

**Builder:** Claude (Opus 5.5), Furnace, 2026-11-26. **Branch:** `wo/tvi-1300c-kernel-hz`, from `main` `189214b`.
**Measurement:** `TVI-WO-1300c.measure.md` (predictions `94520ea`, results `2a0d9b4`). **Ruling:** 2026-11-26T04:03Z, Q1–Q10, each as recommended, Q9 (a) granted.
**Evidence:** `~/.cache/tvi-1300c/` (the measurement's shadow and replays, the build's suites `build1/`, the identity replays `rep/branch`, the mutants `mut/`, the memory runs), kept until the architect says.

## 1. What was built

**One home.** The RCC guard holds the overlay's only PLL and `sys_ck` frequency rule:
- `KernelHz(peripheral)`, as before: the query's gate, then the source's frequency;
- `BusHz("sys_ck" | "rcc_hclk")`, new (Q5): the bus clock, with no query.

The USART guard asks `KernelHz`. The ADC guard asks `KernelHz` for `CKMODE` = 00 and `BusHz` otherwise. The flash guard asks `BusHz("rcc_hclk")` (`rcc_aclk`, p395; Q9 (a)). The I2C guard already asked `KernelHz`. Deleted:
- the USART guard's `KernelHz` body, `Vco`, `Div`, `pllUntrusted`, its `PLLCFGR` mirror, its `Written` subscription and its four PLL constants;
- the ADC guard's `SysHz`, `Vco`, `Div`, `pllUntrusted` and `PLLCFGR` mirror (its `Written` subscription stays, for `rateStale`);
- the flash guard's `Pll1`, `Div` and the PLL register list it filtered RCC writes by.

The guards' code: **+210, −315, net −105**. `rcc-onehome` proves that no model file but the RCC guard's reads an `RCC_PLL*` register or names a PLL rule's parts. Mutant `HZ16` plants such a read in the USART guard.

**The rule (`PllHz`), each "no rule" a reason with its page, never a number:**

| Rule | Ruled | Case(s) | Mutant(s) |
|---|---|---|---|
| ref 1–16 MHz, the VCO in its `VCOSEL` range (p404, p407, p410): kept | — | `usart-idlenorule` (re-planted, below), `i2c-nackclock` | `I259` |
| `RCC_PLLnDIVR` is the write the guard passed (silicon's value); "no rule" from the **next** write after one the stock model did not keep, and after a write made while the PLL ran (recorded mode, after `TVI-RCC-PLL`) | Q2 (B′) | `usart-hzwide` (72 MHz from `DIVN2` = 359), `usart-hzdivr`, `usart-hzpllon`; `i2c-nackpll` unchanged | `HZ06`–`HZ09` |
| `PLLxRGE`'s range holds the reference, edges inclusive (pp. 401–402, p347) | Q3 | `usart-hzrge` | `HZ01` |
| VCOH with a reference below 2 MHz (p346, p347) | Q1 (c) | `usart-hzvcoh` | `HZ02` |
| VCOL above 2 MHz with its VCO in VCOL's range: acknowledged by name (ChibiOS `hal_lld_type1.h` L1834–1837), bench row **R11** | Q1 (c) | `usart-hzvcol` (ArduPilot's PLL2, 72 MHz) | `HZ03` |
| `pll1_p_ck` with `DIVP1` = 0 or an even value (an odd divisor; p404, Figure 47 p346) | Q7 | `flash-divp1zero`, `flash-divp1odd`; `usart-idlenone` path 7 | `HZ04`, `HZ05` |
| `pll3_q_ck` on a USART | Q10 | `usart-hzpll3q` | `HZ10` |
| a PLL's `DIVM`, `PLLSRC`, `RGE` or `VCOSEL` written while it runs (p347, p398–399) | adversarial pass (§4, A1) | `usart-hzcfgon` | `HZ23`, `HZ24` |
| the dead `DIVN` ≥ 3 test (unreachable through the VCO range) | removed | — | — |

**The union (the sources a copy refused and another computed), each read through a guard that uses it:**

| Source | Case | Mutant |
|---|---|---|
| the CSI as a kernel clock | `usart-hzcsi` | `HZ15` |
| the CSI as `sys_ck` | `usart-hzsyscsi` | `HZ12` |
| the HSE as `sys_ck` | `usart-hzsyshse` | `HZ13` |
| the CSI as a PLL reference | `usart-hzcsiref` | — |
| `rcc_hclk` for the flash guard and the ADC's `adc_hclk` | `flash-prescaler`, `flash-d1cpre`, `adc-rccchange` | `HZ11`, `HZ20`, `HZ22` |
| the gate: no frequency for a stopped clock (Q4) | `adc-kernel` (`f_ADC none`) | — |
| a port outside the query (Q6) | `usart-hzport` | — |
| out-of-range PLLs for the flash guard (Q9) | `rcc-gen-hc16` | — |

**`per_ck`** has no case of its own. The ADC asks only after the query says its clock runs; there the settled mux and `CKPERSEL` agree, and they differ only on the ungated uses, which `adc-kernel` pins (§4, A4).

**(4) The ledger.** The RCC rows that feed the rule are restated in `tests/1300/allowlist.py`'s words, each with its page:
- `HSIDIV`, `SWS`, `D1CPRE`, `HPRE`, `D2PPRE1`/`2`;
- `DIVMx` and `PLLSRC`;
- the twelve `PLLxDIVR` fields;
- `PLLxRGE`, `PLLxVCOSEL` and `PLLxFRACEN`.

The last three were tags reading 0, acknowledged as `A1`, "no PLL frequency is modeled beyond PLL1's integer one", which is no longer true. They are now the guard's copy, **served on read** (`CopyMask`), as `RUL-17` asks of a field the guard keeps and uses (`usart-hzvcol` reads `PLLCFGR` back as written, `HZ14`). No row cites `A1` any more.

The four `LPEN` rows' `detail` now resolves to a §8 that names them: one line in `TVI-WO-1300.rm0433.md` §8 A6 (Q8 (ii)).

**Named changes to committed cases (all in the ruling):**
- `usart-idlekernel`: `PLL2RGE` = 11 set before PLL2 starts. Its time is unchanged (Q3).
- `usart-idlenorule`: re-planted from `csi_ker_ck` (which now has a frequency) onto a 6 MHz VCO. Its `bit` text moves (Q6).
- `usart-idlenone`: four records where there were seven, each with the RCC guard's reason, plus the flash guard's two at the PLL1 paths (Q2, Q6, Q9).
- `adc-kernel`: also asserts `f_ADC none` (Q4).
- `flash-prescaler`, `flash-d1cpre`: the note names the fields (`HPRE = 1000`, `D1CPRE = 0000`), since the flash guard no longer decodes them.
- The 16 `1304` cases on `pll()`: `PLL1RGE` = 11 in the one helper. Their times are unchanged (Q9).
- `rcc-gen-hc16`: the flash guard's record, the right answer for a planted case (hse-crystal-16's sequence on this board's 8 MHz HSE: a 128 MHz VCO). Q9; bus note 04:1xZ.
- `rcc-ledger`: R1–R11.

## 2. Acceptance

| Clause | Result |
|---|---|
| Every committed case passes; any change named in the ruling | every suite on the build (`build1/`): rcc 74, usart 49 (+`hzcsiref`, `hzcfgon` later), i2c 47, adc 46, flash 39, gpio 51, timer 37, dma 57, `spike.sh` 76. Five failures, all the teardown race (no end record at load 12–19): `adc-revy`, `-overrun`, `-rccreset`, `gpio-otyper`; each passed on rerun. The changes are §1's, each ruled |
| The 14 generated boots, the fixture and the armed run: boot blocks byte-identical, no new records, the armed run arms | **line-for-line identical to `main`** on all 16 (every console line but the model hash, `PerformanceInMips` excluded; `h/ident.py`); the armed run arms at 9.332 s on both |
| A planted firmware for each source a copy answered differently, and for each (3) rule, reads the one answer | §1's tables |
| No guard but the RCC guard reads an `RCC_PLL*` register or holds a PLL rule | `rcc-onehome`; mutant `HZ16` |
| The budget | §7 |
| CI green; every new case and mutant in a shard | §7 |

## 3. The replays

The measurement's shadow showed every frequency identical across copies on all 16 (measure §2.2). On the branch (commit `2289921`, before the adversarial fix, which no replay reaches), every console is identical to `main`'s (`ident-branch.txt`). That includes the flash guard's new trigger, a change of the AXI clock instead of a write to one of seven registers. Rerun on the final head: §7.

## 4. Adversarial pass

| # | Finding | Class | Disposition |
|---|---|---|---|
| A1 | **A frequency where silicon has none.** p347 forbids changing `PLLxRGE` and `PLLxVCOSEL` once the PLL is on. `TVI-RCC-PLL` judges only `DIVxyEN` and the dividers, so such a `PLLCFGR` write passed, and the copy (now the rule's input) took a value silicon ignores. `DIVM`/`PLLSRC` changes are refused, but recorded mode passed them on the same way | silent wrong frequency | **Fixed:** that PLL answers "no rule" until a machine reset, as Q2's PLL-on write; `PLLSRC` marks every PLL (p399). Case `usart-hzcfgon`; mutants `HZ23`, `HZ24`. Whether `TVI-RCC-PLL` should refuse the `RGE`/`VCOSEL` change itself is a row (§5) |
| A2 | **The gate on the USART's recorded start.** After `TVI-USART-KERNEL` (no running clock), the receiver start asked the USART copy, which computed a number. It now asks `KernelHz`, which says the clock does not run, so a recorded run gains a `TVI-USART-IDLE` there | behaviour change, recorded mode only, after a would-refuse | Accepted with Q4's reasoning. No replay reaches it (identical) |
| A3 | **The flash guard's trigger.** It judged `FLASH_ACR` after writes to a list of seven RCC registers, PLL offsets included. It now judges after any RCC write that changes the AXI clock or how it is derived (`Clock`'s value and note). A voltage-scale change between two RCC writes is now judged at the next RCC write of any kind, not the next write to a clock register | earlier judgment in one ordering | Named. No replay or case moves |
| A4 | **`per_ck` without a case.** The ADC copy read `CKPERSEL` as written; the RCC guard uses the settled mux. The ADC asks only after the query says its clock runs, and there the two agree. They differ only on the ungated uses (`State()`), which `adc-kernel` pins | test gap, argued | No case |
| A5 | **The unit.** The USART copy truncated at each integer step; `KernelHz` is double. Every committed value is integral, so every time is unchanged (measure §2.2). For a non-integral clock (an HSE ÷ 3) the frame time can now differ by a nanosecond's rounding, and the double is the exact rational | precision | Named |
| A6 | **DS12117's wide VCO ceiling** is 836 MHz in Table 39 and 960 MHz in Table 136 (pages from the text layer). `PllHz` takes RM0433's 960 | revision split | Row (§5) |
| A7 | **`DIVR1` = 0 "Not allowed"** (p403) has no rule: no query mux takes `pll1_r_ck` | unreachable | Row if a mux ever does |

## 5. Rows and findings

- **Taken from the roadmap:** "the PLL/VCO and `sys_ck` derivation now lives in the USART and ADC guards" (one home, the flash guard's copy included); `PLL2DIVR`/`PLL3DIVR`'s frequency half (Q2); the four `LPEN` rows' `detail` (Q8 (ii)); `1306a` F4–F6.
- **Rows left:**
  - `PLLnDIVR`'s read half: the stock model still narrows `DIVN2`/`DIVQ2` and `DIVN3`/`DIVQ3` on read (Q2 (C), not this WO).
  - `TVI-RCC-PLL` refusing a `PLLxRGE`/`VCOSEL` change under a running PLL (A1).
  - DS12117's 836 MHz ceiling on one revision (A6).
  - Bench row R11 (VCOL above 2 MHz), not run.
- **Two mechanisms that could be one** (from the reduction pass): the ADC guard (at platform load) and the flash guard (at machine start) each find the RCC guard to subscribe to `Written`. They are not merged, because their timing differs.

## 6. Reduction pass

Reduction pass (diff): **−105 net in the guards** · 4 duplicates removed (the USART, ADC and flash guards' PLL arithmetic, the RCC guard's old `divn` copy of the range) · 1 dead path removed (`DIVN` ≥ 3) · 0 protected kept · 2 findings (§5's two lookups; the `PLLSRC`/`DIVM`-under-a-running-PLL test in both `RefusedPll` and `Pass`, which recorded mode keeps distinct). Run by a fresh subagent over the diff, its proposals checked by me; its one removal (`BusHz`'s name check) declined, because it fails closed on a wrong name.

## 7. Gates

- **Mutants:** (pending)
- **Memory:** (pending)
- **CI:** (pending)
