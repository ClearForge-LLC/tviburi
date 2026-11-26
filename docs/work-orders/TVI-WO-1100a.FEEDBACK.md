# TVI-WO-1100a — FEEDBACK

**Builder:** Claude (Opus 5.5), Furnace, 2026-11-26. **Branch:** `wo/tvi-1100a-cut`, from `main` `39e51af`.
**Measurement:** `TVI-WO-1100a.measure.md` (predictions `2514129`, before any run). **The decision:** `TVI-WO-1100a.cut.md`. **Ruling:** none yet. This is WO §7's planned stop, after (4).
**Evidence:** `~/.cache/tvi-1100a/` (the release, its ledger and cases, the A0/A/B consoles in `arm/`, the route check), kept until the architect says.

## 1. What was done

- **The queue, re-joined:** `1100`'s 722 rows, `1150` (v)'s 122, and 91 armed-only rows (`4400h`'s armed queue, same readers). Each row is placed by instance, offset and bits; none is unplaced.
- **The joins:**
  - **reach,** against the armed run on `main`'s guarded twin (Aethelgard `4400h` `s03-arm-s` as `1305` captured it: arms at 9.003 s, armed from 9.332 s, no record from any guard);
  - **state,** against the ledger of `main`'s release `8e4a1f4b…`, judged on the types the run bound.
- **States:** guarded 610, acknowledged by name 253, inherited 72, not reached 0. Each row has its evidence (`spike/1100a/data/states.tsv`).
- **The cut, ranked:** two families are open on the four paths the armed run flies, **DMAMUX1** (10 channels) and **PWR** (5 registers). Recommended: those two slices. Cautious alternative: plus TIM5.
- **The audit:** clauses 2, 5 and 7 are met. Clauses 3, 4 and 6 are met in part, with named unproven cells, each a test fix. Clause 1 has two families open up to the cut.
- **Nothing built.** No model, guard, test, CI or steering document changed. The fence holds: `docs/work-orders/TVI-WO-1100a.*` and `spike/1100a/` only.

## 2. Acceptance (WO §2)

| Criterion | Where |
|---|---|
| Every queue row has a state with its evidence | `states.tsv`: 935 rows. Guarded: a case line, or a `Fields` line for a modeled field. Acknowledged: the `Fields` line and its ruling. Inherited: the ledger row and the bound type. No row is unreached; the 60 rows the armed run does not reach have their reach source named |
| The audit names the job or test for each clause, or says unproven | measure §4: CI run 45774342008 at `39e51af`, every case named, the unproven cells listed |
| The cut page fits on one page, readable cold | `TVI-WO-1100a.cut.md` |
| CI green (route and docs) | this PR's run. The route classes it `code` (`spike/`), so every shard runs; `route.sh origin/main` passed locally: inventory 0 failures |

## 3. Adversarial pass (WO §5), by a fresh subagent with no stake

**Demonstrated and fixed in this PR:**
- **D3-1, writes the first reader could not see.** 31 firmware writes refused at the bus by the RCC guard's clock lock (logged with no PC), and 3 CPU writes to no peripheral: DMAMUX2 `C7CR`, DBGMCU, the AXI GPV.
  - The first reader matched only CPU-tagged access lines, so "B's log is complete for writes" was false as written.
  - Fixed: the reader counts both, and outside-queue registers went from 240 to 253.
  - No row's state changed. Every refused write is 0 or all ones with the clock off, which silicon ignores too (p373).
  - **DMAMUX2 has no model at all;** it is now named in the cut's Phase 4 list.
- **D2-1, the guarded split was wrong.** The join called a `guard` row "modeled" before reading its allowed set. 99 such rows have restricted sets (GPIO `PUPDRn`, four RCC selectors, `FLASH_ACR.LATENCY`).
  - Corrected: modeled 315, own-instance case 130, sibling-instance case 165, not 414/108/87.
  - 77 of the 165 are GPIO ports B–F, judged by port A's sweep.
- **D2-2:** a whole-word row with a bit mask (`RCC.APB2LPENR.undefined bits 0x8180000`) is now judged by its own bits: acknowledged → guarded.
- **D3-2, D3-3:** DMA2 stream 1's `CR` is written by the armed CPU but refused at the lock (now reach `arm-locked`), and the stock cross-check's 11 unmatched rows are now named.
- **D1-1, D1-2, D1-4:**
  - clause 4 relabelled "partly" (its page match is not a CI check);
  - clause 3's removed-guard cell re-cited (`adv-removed` asserts the runner's exit only; `stock-forge` is the records check);
  - ADC's unswept rows corrected to 18, `ADCx_CCR.PRESC` included.

**Demonstrated, recorded, not fixed (outside the fence):**
- **D1-3:**
  - the I2C and ADC sweeps count records and distinct field names, but never match each to its write;
  - SPI's `MASKED` cases check the rule, not the kind.
- **D2-4:** `SPI6SEL` is placed 29:28 (Renode's tag); RM0433 gives 30:28. State unaffected.
- **S2-3, a weakness of the evidence:** three `@reset` rows rest on a `read-only` row, whose evidence is about writes.

**Tried and not broken:**
- The reader matches all 4,683,319 CPU write lines: no collapsed, `\r`-broken or interleaved lines.
- TIM2, the SPI `SR` reads and SYSCFG are truly absent from the armed run.
- Every row's offset equals CMSIS's, and every `file:line` points at its `R(...)` row.
- For all 154 case-guarded rows, the case's offset and value set the row's own bits.
- No queue row lands on a `MASKED` field or TIM1's `DMAR`.
- The DMA, ADC and flash offset translations are correct.
- Every cited case is in CI's case list. Run 45774342008's aggregate confirms they ran: `970 expected (433 cases, 537 mutants), 970 ran across 10 shards, 0 failures` (read after the subagent's report).

## 4. Findings and rows (each for its owner; none fixed here)

1. **The release's class map guards instances Tviburi has never run guarded** (measure §5 F1).
   - `pins.tsv` gives no instance list for SPI or the timers, so the `MANIFEST` restates `spi4` and all 14 timers onto the guards, and the ledger lists them `guarded_by`. Every Tviburi replay guards only `timer1`/`timer8` (and `spi1`/`spi2`).
   - **Not invariant 2:** the ledger keeps them `inherited-unaudited`. But a consumer following the manifest would put ArduPilot's TIM5 under the TIM1/TIM8 allow-list, and its `ARR` = 0xFFFFFFFF would likely refuse (inferred, not run).
   - **A fix,** with a test that the manifest restates only instances a replay has run. Not one line: SPI's instances are consumer-declared.
2. **The gate-proofs slice** (measure §4), all fixes to tests:
   - **clause 3:** console-framed fixtures for a removed guard and a crash, and a runner case that kills the simulator;
   - **clause 4:** `stock-modf` asserts the after-marker; the page match moves into CI or is ruled a hand check;
   - **clause 6:** drive the I2C and ADC sweeps from the ledger, as the other five are, and match each record to its write.

   Whether "a planted firmware" (clauses 1 and 6) admits monitor writes is the architect's wording to rule.
3. **The armed run needs a declared battery level on `main`** (F4): 805,530 `TVI-ADC-LEVEL` records without it. With the argv's own 16 s `RunFor`, Renode aborted at 21.27 s, 6.65 GB resident. It is a consumer-property row (`1307` Q10) and an Aethelgard Phase 2 item.
4. **Ledger forms:**
   - `TIMx_SR` `BIF`/`B2IF` are `tag` with `allowed: any`;
   - six `stock-wrong`/`any` rows quote no ruling (`DMA_SxFCR.FTH`, TIM `UIF`/`CCxIF`);
   - 50 `stock-wrong` rows carry their acknowledgement in prose rather than `RUL-17`'s class (F2, F3).

   One form for "acknowledged" would let a reader count them.
5. **The access log names registers `unknown`** through the RCC and DMA wrappers (F5). `1305`'s nested-enum pattern was not carried there.
6. **Firmware writes outside CPU-tagged access lines** (F10): a consumer reading only `[cpu:]` lines misses writes refused at the clock lock and writes to unmodeled addresses (DMAMUX2 included).
7. **Not mine to fix, for the reduction at the next gate:** "which instance each guard's sweep writes" is stated in `spike/1100a/reader/join.py` (`TEST`) and `clause6.py` (`SWEPT`). Merging them would add a shared module.

## 5. Disclosures

- **One container ran outside `~/bin/capped-run`:** a one-line read-only `grep` of the stock `stm32h7.repl` in the pinned image, with `--network none`, about 2 s. Not repeated.
- **§0.6 was incomplete:** the window and the battery level, which `1305` and `1307` ran with, were added after A0 (R5). A0 is kept as evidence (F4).
- **Readings after the data,** R1–R6 (measure §1.3), are stated with their reasons. R1–R3 decide where 279 ledger rows fall. R6 (the USART roles, from Sentinel-03's hwdef) moves 149 rows and registers to the command link and no family across the line.
- **The 1.3 GB uncompressed console** sat in the session scratchpad on RAM-backed `/tmp` during the adversarial pass. Removed after it; the compressed copy is in `~/.cache/tvi-1100a/arm/`.

## 6. Reduction pass

Reduction pass (diff), run twice, before and after the adversarial corrections:
- −6 lines net in the readers;
- 1 duplicate removed: `locked` initialised once in `arm-writes.py`, not set and defaulted;
- 4 dead paths removed: an unused case lookup, an unused variable and an unreachable second `acknowledged` arm in `join.py`, and an unused `sys.path` line in `clause6.py`;
- outputs byte-identical after each;
- 0 protected kept;
- 1 finding (§4 item 7).

`pipeline.sh` was then run end to end on disk (a fresh release, the cases, runs A and B, every table). Every committed table regenerated byte-identical before R6's path change.

## 7. Gates

- **Predictions before runs:** `2514129`, before A0.
- **Containers:** one at a time, `~/bin/capped-run`, `nice -n 10`, `--network none`, `free -g` checked first. One exception (§5).
- **Route, locally** (`tests/ci/route.sh origin/main`), re-run with this file committed: the route's 11 tests pass; inventory 389 files, 86 documentation, 0 failures; `class=code`, `budget=no`. `main` had not moved from `39e51af`, so there was nothing to merge.
- **CI:** this PR's run, reported on the bus when it ends.
