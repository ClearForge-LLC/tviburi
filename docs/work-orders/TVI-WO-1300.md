# TVI-WO-1300 — The RCC guard: kernel clocks and enables, through a wrapper over the sealed stock RCC

**Repo:** `ClearForge-LLC/tviburi` · **Base:** `main`, named in the kickoff.
**Branch:** `wo/tvi-1300-rcc-guard`.
**Author:** Claude (co-architect) · **Builder:** strong tier, with a full adversarial pass.
**Phase:** 1, the first `13NN`. **Cadence:** one measurement, a stop, then the build.

**Grounds:**
- **Northstar** invariants 1, 2, 5, 8; §6 ("so slow or heavy").
- **Architecture:** `RUL-07` and `RUL-09` as amended from `1150`, which give the wrapper, one file, and cancel and restate; `RUL-04`, `RUL-13`, `RUL-16`, `RUL-17`, `RUL-18`, `RUL-19`; `RUL-10` and `RUL-11` as amended; `OQ-03`.
- **Roadmap:** Phase 1, the `1300` row; gate clauses 1, 3 and 6.
- **Evidence:** `spike/1150/results.md` (i) and its addendum, (v), (vi) cost, (vii); `spike/1150/retype/SpikeRcc.cs`, `rcc/restate.awk`, `retype/forms/vii-a*`; the `rcc` rows of `spike/1150/data/writes-ap.tsv` and `spike/1100/data/writes.tsv` (`fw` and `judge`); `queue-ap.tsv` tiers 1 and 4a; `TVI-WO-1150.FEEDBACK.md` §2 and §8.2.

> **What this is:** the first guarded path in `RUL-13`'s order, the RCC's peripheral enables and kernel-clock selectors. Its scar class has already been seen on silicon: SPI1 was enabled with no kernel clock, and the twin passed (`RUL-04`).
> - **(1) The wrapper** (`RUL-07`; `1150` (vii) candidate (a)). `Tviburi.GuardedSTM32H7_RCC` owns an unregistered stock `STM32H7_RCC`, built with the stock entry's constructor arguments. It forwards every access, registration, unregistration and reset to it. It implements all three bus access widths itself: `SpikeRcc` took double words only, with translations, which is not `RUL-07`'s shape. Its records carry its own name.
> - **(2) The platform** (`RUL-09` (b)). Tviburi's board description cancels `rcc`, declares the guard at `0x58024400` `as "rcc"`, restates all 58 stock registrations onto it, and points `spi1`'s registration at it. Generate the restatement from the pinned stock `stm32h7.repl`, as `restate.awk` does, and commit it with its generator and the stock file's sha256. List any untyped `rcc:` update in the consumer's captured `platform-booted.repl` (the `n3` trap).
> - **(3) The allow-list** (`RUL-17`, `RUL-16`). Classify every field of every RCC register that either firmware, or the consumer's cold writes, gives a value. Classify it as `0100b` does SPI's: modeled by the guard; implemented by the stock model (inherited, unaudited); or a tag or dropped bits, with the value the model behaves as. Keep the list as data keyed by RM0433's register and field names, with a page for each row. The registers measured in the evidence:
>   - `RCC_CR` (the `HSI`, `CSI`, `HSE` and `PLLx` `ON`/`RDY` bits), `RCC_ICSCR`, `RCC_CFGR`, `RCC_D1CFGR`, `RCC_D2CFGR`, `RCC_D3CFGR`;
>   - `RCC_PLLCKSELR`; `RCC_PLLCFGR` (`DIVxyEN`, `PLLxRGE`, `PLLxVCOSEL`, `PLLxFRACEN`); `RCC_PLLxDIVR`; `RCC_PLLxFRACR`;
>   - `RCC_D1CCIPR` (`CKPERSEL`, `QSPISEL`), `RCC_D2CCIP1R`, `RCC_D2CCIP2R`, `RCC_D3CCIPR`;
>   - `RCC_CSR`, `RCC_RSR`, every `*RSTR`;
>   - the `*ENR`: AHB1, AHB2 and AHB4, and APB1L, APB2 and APB4, with AHB3, APB3 and APB1H from the cold writes;
>   - the `*LPENR`.
> - **(4) The kernel-clock rule, `predicted-fault`** (`RUL-18`). This rule covers the `SPI123SEL` scar. A peripheral whose bus clock is enabled has no kernel clock when its selector names a source that is not running: an oscillator that is off, a PLL that is off or not ready, a PLL output whose `DIVxyEN` is clear, or `per_ck` whose own source (`CKPERSEL`) is off.
>
>   Read RM0433 Rev 9 for each selector's source table, the PLL output enables, and `RCC_CR`'s ready bits, and cite each by page. Cover the kernel-clocked peripherals that either firmware enables: SPI1, SPI2, I2C1, I2C2, USART1 and RNG, from the `*ENR` values in the evidence. Any other kernel-clocked peripheral enabled is `unmodeled`. **Where the refusal lands (ruled by the architect, 2026-11-23):** enabling a bus clock before its kernel source runs is not a fault, and the RCC guard never refuses it. The `predicted-fault` belongs at the peripheral's own start (SPI: `SPE` or `CSTART` set; I2C: `PE`; USART: `UE`; RNG: `RNGEN`) while its kernel source is not running. This work order builds the RCC side: the guard answers the query "is peripheral P's kernel clock running now", by its tracked state and with the page cited. Wiring that query into a peripheral's start is that peripheral's guard's work: SPI1's follows `0100b` as `TVI-WO-1300a`. A planted firmware test here exercises the query directly.
> - **(5) Every other refusal is `unmodeled`:** a field given a value outside the allow-list. Use `0100b`'s records contract unchanged.
>
> It is NOT: GPIO, timers or DMA (later `13NN`); the identity, the manifest or the release tooling (`1200`); the check or any record field (`0100b`'s); recorded mode in `model/`; any change to Aethelgard.

## 1. Build

1. **Measure first, both firmwares.**
   - **The generated firmware:** with `1100`'s harness.
   - **ArduPilot** (`a799dfb6…` on `sentinel-02-own`): with `1150`'s `replay-ap.sh`, and the restatement applied to the replayed platform.
   - Each runs with the guard in recorded mode, as `1150` (iv) prototyped it, in a scratch copy under `~/.cache/tvi-1300`. Set the model directory as `1150` (ii)'s `modeldir` runs did, because the identity is `1200`'s.
   - **Commit the field table** in `docs/work-orders/TVI-WO-1300.rm0433.md`. For each row, give its page and the values both firmwares and the cold writes give it. For each kernel-clocked peripheral, give the order in which each firmware enables it, starts its source, and starts it.
   - **Measure the one-file overlay's memory** on ArduPilot's full boot, as `1150` (vi) did: the models and the constant concatenated, with the `using` lines hoisted. **Stop** with §7's lists.
2. **Build (1)–(5)** to the acknowledged table. Each refusal comes with a planted firmware in `firmware/`, and each test with a mutant it kills.
3. **Merge `main` before READY,** after `0100b`'s records contract has merged.

## 2. Acceptance

- **ArduPilot's full 5 s boot, recorded, with the guard,** gives no `would-refuse` under `rcc` outside the acknowledged table. Its peak RSS, as the reaper measures it, is under 1,048,576 KiB in the one-file shape. Give the runs and the margin.
- **The generated firmware's RCC sequence passes,** enforcing.
- **The scar:**
  - a planted firmware that enables SPI1 with `SPI123SEL` on a source that is not running refuses with `kind: predicted-fault`, naming the selector, the source and the pages;
  - the same firmware with a running source passes;
  - the bench row is named in FEEDBACK (`RUL-18`).
- **Every field outside the allow-list:** a planted write of a value the model does not implement refuses with `kind: unmodeled` (gate clause 6).
- **The 58 registrations resolve on the guard.** `TIM1RST` (RM0433 Rev 9 §8.7.35 p442) and `SPI1RST` reset their peripheral through it, and an unclocked peripheral still locks. A hook and access logging set on the guard do not displace it.
- **CI is green on the PR.**

## 3. Fences

- **Fence:**
  - `model/`: a new `GuardedSTM32H7_RCC.cs`, the generated restatement, and the board description's `rcc` and `spi1` entries;
  - `scripts/prelude.resc`: one `include` line for the new file;
  - `tests/`, and `firmware/` (planted firmware only);
  - `docs/work-orders/TVI-WO-1300.*`.
- **Also in the fence:** one `include` line in `scripts/prelude.resc` for the RCC guard's file, granted by the architect.
- **Not:** `model/Refusal.cs`, `model/GuardedSTM32H7_SPI.cs` or `tools/`, which are `0100b`'s (a new record field is a stop); the steering documents, `docs/reviews/`, `spike/`, or Aethelgard.
- **Running beside you:** `TVI-WO-0100b`. §1.1 starts now. The build waits for `0100b`'s records contract (the `kind`, the check's console grammar, and `mode`), and uses it unchanged.
- One PR, left unmerged. Write `~/.cache/tvi-1300/pr-title-body.md`.

## 5. Adversarial pass, full

- **A field outside the allow-list that still passes:** a sub-word write, or a read-modify-write that carries a tag's read-0 back over a selector the firmware set before (`D2CCIP1R`, `D2CCIP2R`).
- **A kernel clock reported present when silicon has none:** the stock model's `PLLxRDY` set without `PLLxON`; or a source turned off, an output disabled, or a selector changed while its peripheral runs.
- **A path to the stock RCC that bypasses the guard:** a second address, a registration left on the cancelled variable, or a monitor command.
- **Harness writes:** the consumer's cold writes after a brown-out rendering (`pc: "none"`) pass through the same rule.
- **A record under `rcc` whose text the firmware controls.** The owned stock RCC's lines are now sourceless; show that none takes the guard's name.

## 7. Flag-and-stop conditions

- **After §1.1,** with the field table and the fields to acknowledge: every field outside the allow-list that either firmware or the cold writes give a value the model does not implement. The architect acknowledges each by name, or rules it refused.
- **Either firmware's boot would refuse on a path RM0433 Rev 9 says is valid.** Stop with the order and the page. (Enabling a bus clock before its source runs is ruled valid; see (4).)
- **The one-file overlay's peak exceeds 1,048,576 KiB** on ArduPilot's full boot. Stop: northstar §6.
- **RM0433 Rev 9 contradicts a `kind`,** or the rule needs a record field that `0100b` does not have. Stop with the page or the field.
