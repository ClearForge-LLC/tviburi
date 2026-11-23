# TVI-WO-1300a — A clock that reaches the peripheral: enables off at reset, SPI1's start asks for its kernel clock, and a clock lost while it runs

**Repo:** `ClearForge-LLC/tviburi` · **Base:** `main`, named in the kickoff.
**Branch:** `wo/tvi-1300a-clock-reaches-peripheral`.
**Author:** Claude (co-architect) · **Builder:** strong tier, with a full adversarial pass.
**Phase:** 1, a refinement of `1300`. **Cadence:** a short measurement, a stop, then the build.

**Grounds:**
- **Northstar** invariants 1, 2, 5, 8.
- **Architecture:** `RUL-07`, `RUL-09` (b), `RUL-11`, `RUL-12`, `RUL-16`, `RUL-17`, `RUL-18`, `RUL-19`.
- **Roadmap:** Phase 1, the `1300a` row (amended 2026-11-23); gate clauses 1 and 6.
- **`TVI-WO-1300`** (#16, `a534bb7`): the query "is P's kernel clock running now" (`model/GuardedSTM32H7_RCC.cs`); its FEEDBACK §5.4 (the `*ENR` false pass; a clock lost after a start), §5.5 (the teardown flake) and §7 findings 1 and 2; the architect's rulings of 16:10Z on the bus.
- **`TVI-WO-0100b`** FEEDBACK §2 item 11: a `CFG2` write with `SPE` = 1 of the value `CFG2` already holds is a false refusal.

> **What this is:** the second half of the scar class `1300` opened. `1300` made the RCC answer the question; this makes the peripheral ask it, and closes the inherited path by which a peripheral works with no clock at all.
> - **(1) Enables off at reset** (invariant 1; RM0433 Rev 9 p373: "a write access will be ignored"). The guard's `*ENR` copies start at 0, as silicon. A peripheral whose bus clock is not enabled is locked: an access to it refuses `predicted-fault`, naming the enable bit and the page. Measure first which peripherals the stock model can lock, and how (`1300` measured `*ENR` reading the bus-enable state, 1 from reset). Fix Tviburi's own callers that relied on the stock default: the SPI tests, `conditions.resc`, and `1200`'s generated SPI sweep (`tools/`) enable SPI1 first.
> - **(2) SPI1's start asks** (`RUL-18`, ruled in `1300` (4)). At `SPE` or `CSTART` set, the SPI guard asks the RCC guard's query. No running kernel source is a `predicted-fault` at SPI1, naming the selector, the source and the pages, never at the RCC. The query is reached through the guard instance by name, never through a static or a global.
> - **(3) A clock lost while the peripheral runs.** Turning a source off, clearing a PLL output's `DIVxyEN`, or changing the selector while a peripheral that uses it runs (SPI1 with `SPE` = 1) refuses `predicted-fault` at the RCC write, by the same query re-asked for every running consumer. Recorded mode logs it as `would-refuse`. Cite each page; where RM0433 says the hardware blocks the change (a source that "cannot be cleared"), model the block instead of a refusal, and say which.
> - **(4) The same-value `CFG2` write** with `SPE` = 1 passes, as `CFG1`'s equal value does (`0100b` §2 item 11). A different value still refuses.
> - **(5) The PLL1-source clear, by the bench.** RM0433 Rev 9 pp. 384–386 and §8.5.2 (pp. 340–342) disagree on whether HSI, CSI or HSE can be cleared while it feeds PLL1 and PLL1 is not the system clock. Keep `1300`'s conservative handling (no `predicted-fault` rests on either reading), and add a `model/bench-checks.tsv` row: on the Nucleo, enable PLL1 from HSI without selecting it for the system clock, clear `HSION`, and read whether `HSIRDY` drops.
> - **(6) `1300`'s measurement, reproducible from a clean checkout.** Commit the allow-list generator's two input tables (RM0433's field list and the stock model's kinds, about 1,400 lines) so the rows regenerate, with a test that they do. Make the §1.1 harness (`tests/1300/replay-1300.sh`'s `trace`, `cost` and `cost-spi` modes, and the prototype files, about 410 lines) run from a clean checkout and a named Aethelgard commit, or delete it; Aethelgard's `AET-WO-4240e` will want to re-run the RCC query over its regenerated firmware, so prefer runnable.
> - **(7) The teardown flake** (`1300` §5.5): one run's `spi1` end record lost after `quit`, failing closed. Measure its rate over at least 50 runs of the two-end-record cases. If the cause is in Tviburi's `scripts/`, fix it; if it is Renode's, say so with the evidence, and the runner retries nothing silently.
> - **Each refusal comes with a planted firmware in `firmware/`, and each test with a mutant it kills.**
>
> It is NOT:
> - any other peripheral's start: I2C, USART and RNG ask when their own guards exist;
> - CSleep (`*LPEN` across `WFI`), `D1CKRDY`/`D2CKRDY`, `RCC_CSICFGR`, PLL2/3 frequencies, or the `HSEBYP` order (`1300` §5.4): rows, not this work order;
> - any change to Aethelgard.

## 1. Build

1. **Measure first.** Commit predictions in `docs/work-orders/TVI-WO-1300a.measure.md`, then record:
   - the stock model's behaviour for an access to a peripheral whose enable is 0, per bus, and how the guard can lock it;
   - every Tviburi test, script and generator that drives a peripheral it never enabled;
   - for (3), each source, output and selector change RM0433 blocks in hardware, against those it allows, with pages;
   - the flake's rate.
   **Stop** with them.
2. **Build (1)–(7)** to the ruling.
3. **Re-run ArduPilot's full boot, recorded:** 0 `would-refuse` under `rcc` and `spi1`, and the one-file peak under 1,048,576 KiB, with the margin. **Merge `main` before READY.**

## 2. Acceptance

- **An access to an unenabled peripheral refuses** `predicted-fault`, naming the enable bit; the same firmware with the enable passes.
- **SPI1 started with no kernel clock refuses at SPI1,** naming the selector, the source and the pages; with a running source it passes.
- **A source turned off under a running SPI1 refuses** at the RCC write (or is blocked, where RM0433 says so).
- **A same-value `CFG2` write with `SPE` = 1 passes;** a different value refuses.
- **The allow-list regenerates** from a clean checkout, byte for byte.
- **ArduPilot's boot and the consumer's generated firmware** pass as `1300` measured them, except where this work order's rules predict a fault; any such is a stop.
- **CI is green on the PR.**

## 3. Fences

- **Fence:**
  - `model/GuardedSTM32H7_RCC.cs`, `model/GuardedSTM32H7_SPI.cs`, their data, and `model/bench-checks.tsv` (new rows);
  - `tests/`, `firmware/` (planted firmware), `scripts/` (the flake fix only);
  - `tools/`: the SPI sweep generator's enable, nothing else;
  - `docs/work-orders/TVI-WO-1300a.*`.
- **Not:** `model/Refusal.cs` and the record grammar (a new field is a stop); the release program beyond the generator change; the steering documents, `docs/reviews/`, or Aethelgard.
- **Running beside you:** `TVI-WO-1301` (GPIO). You share `tests/spike.sh`, `model/bench-checks.tsv` and the board description: add, never rewrite, and merge `main` before READY.
- One PR, left unmerged. Write `~/.cache/tvi-1300a/pr-title-body.md`.

## 5. Adversarial pass, full

- **A peripheral that still works unenabled:** a second address, an alias (`RCC_C1_*`), a DMA access, or a reset that restores the stock default.
- **A start that skips the question:** `CSTART` without `SPE`, a `CR1` write that sets both, a sub-word write, or the query reached through a stale instance after `machine Reset`.
- **A clock lost by a path (3) misses:** `CKPERSEL` changed under `per_ck`, `PLL1ON` cleared, `DIVQ1EN` cleared, `PLLSRC` changed.
- **A false refusal on a valid boot order:** enable, then source, then start; the source changed while the peripheral is stopped.

## 7. Flag-and-stop conditions

- **After §1.1,** with the measurement.
- **Either firmware's boot would refuse** under a rule here. Stop with the order and the page.
- **The one-file peak exceeds 1,048,576 KiB.** Stop.
- **A rule needs a record field** `0100b`'s contract does not have. Stop.
