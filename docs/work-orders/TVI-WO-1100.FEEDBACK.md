# TVI-WO-1100 — FEEDBACK

**Builder:** Claude (Opus 5.5), Furnace, 2026-11-23. **Branch:** `wo/tvi-1100-coverage-map` from `main` `963238f`.

**State:**
- Measured; stopped after WO §1.3 with the queue and a proposed cut (`spike/1100/queue.md`).
- Spike-only: no model code, and no change to Aethelgard.
- **The gates were not run.** No model, script or test changed, and the readers are scratch.
- The PR is left unmerged.

**Commits:**
- `2c5c1d8`: the rule and the predictions, before any run;
- `b11596b`: the harness, the readers and the first data;
- `24d7725`: `queue.md`, the join and the cut;
- then this FEEDBACK.

---

## 1. What was measured

- **13 boards in both views.** Two sets:
  - the committed `boards/` and every `examples/` specification `verify` accepts;
  - after the architect's ruling of 08:45Z, every committed motor specification.
- **The detail:**
  - which boards, and which are not covered and why: `queue.md` §1;
  - how both views were collected, and the controls that show the replay is the CLI's run: §2;
  - the ranked queue: §5, and `spike/1100/data/queue.tsv`, 722 rows, each citing the `ledger.rs` entry and the Renode declaration line;
  - the cut: §8.
- **The proposed cut:** the prefix through rank 172, the motor-output class and the kernel-clock selectors.
  - Within it, 118 rows are measured model gaps: 102 tags, plus 16 `CCxP`/`CCxNP` stored but never applied to an output.
  - The other 54 rows are stored fields the ledger cannot judge.
  - It is Scotty's to rule (`OQ-03`).

## 2. Consumer-side findings (Aethelgard). Named, not fixed

1. **The verdict's ledger rows carry no Renode citation.**
   - `LedgerRow` has no `source` (`ledger.rs` L129–158), and `register_rows` copies only `judge` and `note` (L2046–2060).
   - All 80 to 218 `ignored-by-model` rows per board reach the verdict uncited.
   - Architecture §2.0, "each `ignored-by-model` row cites the Renode source line that drops the field", is true of `table()`, not of what `verify` reports.
   - It matters for invariant 7: a consumer, or Tviburi, reading the ledger by hash gets no line to check. The citations were recovered here from `table()` (`data/table-entries.tsv`, 371 entries, spot-checked at L1053, L458, L594 and L1532).
2. **`SCB.CPACR.CP10`/`CP11` (`same-source`; "whether Renode's core faults a float … is not held") is modelled.**
   - The write lands in NVIC's own switch (`NVIC.cs` L610–620, the same lines in `066a7f13` and `cd4b002a`).
   - That code stores `CPACR` into the CPU and enables the FPU, and no warning is logged.
3. **`I2Cn.ICR.NACKCF` (`same-source`; "STM32F7_I2C.cs is not held: no reaction is claimed") is a tag (`STM32F7_I2C.cs` L278).**
   - The firmware's write-1-to-clear is dropped on all 11 boards that write it.
   - The row has a citable `ignored-by-model` class now.
4. **Five judged rows sit partly on bits the executed model cannot hold, or on bits it stores and never acts on.** None drops a value on these boards, because each written value fits. Each is a false pass waiting for a different value (WO §5, first case, latent):
   - `ADC3.CFGR.RES` (observed): RM0433's field is 3 bits (`[4:2]`), the model's 2 (`STM32_ADC_Common.cs` L551), and bit 4 is a tag;
   - `ADC3.PCSEL.PCSEL` (observed): bit 19 is undefined in the model (`PCSELn`, `ADCChannelCount` flags, L885);
   - `USARTn.TDR.TDR` (observed, 9 bits): the model's `TDR` is 8 bits (`STM32F7_USART.cs` L271–274), and bit 8 is a `RESERVED` tag, so 9-bit mode would drop it;
   - `TIM1.ARR.ARR` (independent-rule): the ledger gives 32 bits, from CMSIS's `TIM_TypeDef`. TIM1 is a 16-bit timer, and the model's 16 bits are right; TIM2's `ARR` is 32 in the model. **Cosmetic.**
   - `I2Cn.CR1.ERRIE` (independent-rule, no "also ignored-by-model" note): the model declares bit 7 `WithIgnoredBits` (`STM32F7_I2C.cs` L129), so the bit is stored and never acted on. It is written 0 on every board; an `ERRIE` = 1 firmware would get no error interrupt in the twin.
5. **The generated DShot is a fixed duty, not frames, and uses no DMA.**
   - `Tim1::start_outputs` writes PWM mode 1 once, `CCRx` 0x28 of `ARR` 0x6A, and "the result has no method".
   - No board's ledger has a DMA, `DIER.CCxDE`, `DCR` or `DMAR` row, and no replay writes a DMA controller.
   - Architecture §2.0's "DShot frames unwitnessable" stays true: there are no frames to witness yet.
6. **The image pin in the kickoff path was stale.**
   - `~/aethelgard` is on `wo/aet-1202-bus-ceiling` with `74f69bd3…`. Main `f42a0c88` pins `e5fc7ddb…`, which was used, as the architect ruled.
   - Main is about to re-pin to `1e0ab105…` (PR #355, the same Renode build). Noted, not chased.

## 3. Findings about Tviburi's steering documents. For the architect; the documents are unchanged

1. **Architecture §2.0, "timer `MOE` and `CCxP` tags".**
   - `MOE` is a tag (`STM32_Timer.cs` L385).
   - `CCxP` is a stored field (L307–321) that the output path never reads. Only input capture reads it (L665, L855).
   - Both are silent passes, by different mechanisms.
2. **`RUL-04` and `RUL-13`'s "DShot DMA" and BOR have no consumer row today** (finding 2.5; `queue.md` §7).
   - BOR's only row is `FLASH.OPTSR_CUR.BOR_LEV@reset`, `silicon-only`, and the firmware writes no BOR field.
   - The roadmap's `13NN` expectation, "DMA for DShot … and BOR", should wait for a consumer row.
3. **The power class is empty.**
   - `PWR.CR3`/`D3CR` are `independent-rule` rows held by Aethelgard's own PWR script (the board `.repl`, `pwr: script`), not by a stock model.
   - So a guarded PWR model would sit beside a consumer-authored one. That seam is not in architecture §4.
4. **"Every committed board" needed a ruling.** The ruling's own examples did not hold on `main`:
   - the `examples/sentinel-02*` specs have no motor outputs;
   - the four-port `fc-base` fixture does not exist yet (`AET-WO-4408`).

## 4. Process and tooling

- **The consumer could be collected without changing Aethelgard.**
  - `verify` needs `aethelgard-bridge` beside the CLI. CI builds it separately (`cargo build --manifest-path bridge/Cargo.toml`), and `doctor` named it.
  - Per board, `verify` took 2.5–12 minutes. The HSE fixtures and `sentinel-03` were slowest.
- **First capture attempt failed; nothing was left behind.**
  - A wrapper at `/opt/renode/renode` in the throwaway container saw the `--version` probe but never the sandboxed boot.
  - Replaced by a watcher reading `/proc/<pid>/cmdline`, which needs no change to the image.
- **Mistakes caught in my own readers before they reached the queue, all disclosed in `queue.md` §3:**
  - R1: boards counted across classes;
  - R2: read-only status bits counted as drops;
  - R3: drops judged only on the last write. It missed `ADC_CR.ADCAL`, and the 13-board check of the models' own warnings exposed it.
  - Also: a switch-handled offset taken for an unimplemented register (`CPACR`), and a test file taken for `STM32H7_RCC.cs`.
- **`RUL-14`:** the scratch readers are awk, jq and POSIX shell, and the map is an IronPython monitor script (`regmap.py`, the one language Renode runs at the monitor). None is a program larger than a thin harness, and nothing is built. If a reader outlives the spike (for `1200`'s ledger checker), it should be Rust.
- **Disk:**
  - `~/.cache/tvi-1100` held about 130 MB: the captures, the work directory, and the Renode sources at two commits.
  - The worktree's untracked `target/` held the CLI build.
  - Both are deleted at close (§7).
- **The advisor was unavailable once, at the cut.** The cut was then checked by a sensitivity run (`queue.md` §8) and by the independent review (§6).

## 5. The adversarial pass (WO §5)

- **A judged field the stock model drops:** none dropped on these boards once R2 and R3 were applied. Four latent rows (finding 2.4), and one row miscredited the other way (2.2).
- **A write the access log misses:**
  - On every board, the writes without CPU context equal the number of `sysbus Write*` monitor commands (17 to 35). So no DMA-driven or other non-CPU write is hiding in the judge's bucket.
  - No DMA controller is written.
  - Logging perturbs nothing: the sentinel block is identical between the pure and the logged replay on all 13 boards.
  - **Residual risk, named:** writes after the 100 ms boot window (the 16 `R-window` I2C2 rows are the visible case), and writes silicon would make that the firmware never issues.
- **A rank that hides a flight-critical path:**
  - R1 fixed the one found (`MOE` behind a PA0 `OTYPER` row).
  - Three remain low by the rule, and all are named for the cut:
    - the HSE/PLL rows (ranks 174–215, 2 boards; dropped wherever used), now the alternative cut in `queue.md` §8;
    - the I2C open-drain `GPIOB.OTYPER.OT6`/`OT7` (sensor class, dropped on 11 boards);
    - `USARTn.CR1.FIFOEN` (the rest, ranks 457–531, dropped on 7, 4 and 4 boards). It lands in `RESERVED[29+3]`, and the rule has no class for the link.
- **An independent re-derivation by a fresh reviewer:** see §6.

## 6. Independent review

A fresh subagent (Sonnet) re-derived the report from `data/` and the sources, without my conclusions.

**It confirmed:**
- every number in `queue.md` §5 and §8;
- the `STM32_Timer.cs` lines;
- RM0433 p. 1614;
- five random source citations;
- that no judged row without an "also ignored-by-model" note is dropped.

**It found five issues. All were taken, and the cut stands:**

| # | Finding | Effect | Taken as |
|---|---|---|---|
| 1 | §8 said only `CCxS` gates the outputs; `CCxE` does too (L847, L861) | wording | corrected |
| 2 | "From rank 173 every row is on 3 boards or fewer" holds within the clocks class only | a claim | corrected |
| 3 | HSE/PLL rows are dropped wherever used and hidden by board count | judgment, could move the cut | the alternative cut in `queue.md` §8; question 8.2 |
| 4 | the `ADC3_COMMON.CCR` rows were a join miss: CMSIS writes those offsets as "ADC1/3 base address + 0x308" | a claim in §6 | reader fixed (R4); the queue is byte-identical |
| 5 | `FIFOEN` is hidden low; the warning control is blind to behavioural ignores | judgment and coverage | `FIFOEN` named in §5. The consoles were then searched: no behavioural ignore was logged on any board |

The review also surfaced the `WithIgnoredBits` case (`ERRIE`, finding 2.4), now classified.

## 7. Close

- **Reduction pass (diff): −16 net.**
  - 1 duplicate removed: `data/boards-motors.tsv`, which repeated `boards.tsv`.
  - 3 dead paths removed: `replay.sh`'s unused `logged` mode, `view2-join.awk`'s unread `lpc`/`fseq`, and `rank.awk`'s unread `dshot` with its `pipeline.sh` column.
  - 0 protected kept; 0 findings.
  - Proof: the regenerated `queue.tsv` and `joined.tsv` are byte-identical, and one board replayed with the reduced `replay.sh` reproduces its boot blocks, register map and writes exactly.
- **The gates were not run.** The change is spike-only, and no model, script or test changed.
- **Aethelgard is untouched.** `git -C ~/tvi-1100-aet status --porcelain` is empty at `f42a0c88`, and the push URL is disabled.
- **Scratch removed at close:** the worktree's `target/` and `~/.cache/tvi-1100`'s work directory, replays, cargo home and Renode sources. The captures (`cap`, `cap2`, about 35 MB) are kept until the PR is ruled, so `pipeline.sh` can regenerate the queue.

## 8. Questions for the architect and Scotty

1. **`OQ-03`:** the prefix through 172, with the 118 measured gaps as the refusal set and the 54 stored `same-source` rows sent to `OQ-04`/`13NN` (proposed). Or the whole prefix as the refusal set.
2. **Should the 14 HSI non-motor test fixtures be measured?** They can only raise board counts on HSI RCC rows already in the cut, or on peripheral-enable rows now at 3 boards or fewer (`queue.md` §8). About 1–2 hours of `verify`.
3. **Should `sentinel-02-bench-hse8` (32G) be run?** It would make the PLL rows 3 boards. They stay outside the proposed cut either way, but it is the one committed example that would make the alternative cut's HSE rows measured on a committed board.
4. **The alternative cut:** add the 10 HSE/PLL rows that are dropped wherever used, ranks 174–215 (`queue.md` §8), making the refusal set 128 rows.
