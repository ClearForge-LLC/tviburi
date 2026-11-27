# TVI-WO-1205 — FEEDBACK

**Builder:** Claude (Opus 5.5), Furnace, 2026-11-26. **Branch:** `wo/tvi-1205-gate-judged`, from `main` `63fe557`, with `main` `5d4a61a` (#64, `1308`), `cd235c7` (#67) and `af1e15b` (#70) merged.

**Rulings followed** (architect, 2026-11-26):
- **17:02Z:** TIMx_SR read; grants 1 (its rows' text) and 2 (clause 6's five mutants).
- **17:35Z:** A10.
- **18:02Z:** step (2) on post-`1308` `main`.
- **18:38Z:** DX34 and PW39; reading R3 in the architect's wording.
- **18:56Z:** per-case mutants; point 1 put to Scotty.
- **19:10Z:** `adv-removed`, by its two independent lines; the four removed copies accepted.
- **22:53Z, Scotty's ruling on 18:49Z point 1, relayed:** (b). `inherited-unaudited` is not acknowledged for the gate; `RUL-17` amended in docs PR #70. Clause 1 is **not met**; `TVI-WO-1206` reads the 129 items and re-judges it.

**Addendum (`TVI-WO-1206`, 2026-11-27): clause 1 is re-judged by a per-field measure.** 1100a's join, used here, counted only queue rows and registers with no row. A field written at a register that has another row was counted nowhere, so the 129 undercounted: 174 more fields were open above the cut (`spike/1206/gap.py`). `TVI-WO-1206` read all 295. Its per-field measure, now the gate's, finds 0 open of 1,982 fields above the cut. Two items are with the architect: SPI `TSIZE` and DMAMUX2 (`TVI-WO-1206.FEEDBACK.md` §7).

**The deliverables:**
- `TVI-WO-1205.gate.md`: clauses 1–7, clause by clause.
- `TVI-WO-1205.consumer.md`: the checklist.
- `TVI-WO-1205.verdict.md`: the one page for Scotty.

**Evidence:**
- `spike/1205/`: the join's scripts and data, by run label.
- `~/.cache/tvi-1205/`: the runs, the replays and the scratch mutants. Kept until the architect says.

## 1. What was built

**(1) The gate on `main`, clause by clause** (`gate.md` §2–§7).
- **Clauses 2–7:** each has its CI case and mutant named, with what a skeptic would try and the case that stops it.
- **The judged run:** CI 45858432173 on `63fe557`, 1,000 items, 0 failures.

**(2) Clause 1 on post-`1308` `main`** (`gate.md` §1).
- **The method:** `1100a`'s join, re-run by `spike/1205/harness/pipeline.sh` on `main` `63fe557` and again after `1308`.
- **The replays:** the 14 boots, the fixture and the armed run give 0 records.
- **The join after `1308`:** 935 rows: 612 guarded, 254 acknowledged, 69 inherited, 0 findings.
- **Above the cut:** 653 rows (412 guarded, 241 acknowledged) and 97 armed registers outside rows (71, 26). Nothing is open in `1100a`'s sense.
- **Clause 1, as Scotty ruled (relayed 22:53Z):** not met. 105 rows and 24 registers hold `inherited-unaudited` fields, which no longer count as acknowledged (`gate.md` §1.4).
- **PWR** is judged on the replay's restated platform: Aethelgard 4400h's Sentinel-03, PWR restated with revision V by `tests/1301/replay-1301.sh`.

**(3) The consumer's checklist,** 37 items in six groups plus five carried over.
- Each cites its source and says how the release lets the consumer check it, or "no check" (a row).
- `spike/1205/coverage.sh docs/roadmap.md docs/work-orders/TVI-WO-1205.consumer.md spike/1205/data/consumer-not.tsv`: all 55 roadmap lines naming the consumer are cited or excused, with 0 uncovered.

**(4) The verdict:** one page.

**Edits outside the WO's fence, each granted:**
- **`model/GuardedSTM32_Timer.cs`'s `TIMx_SR` rows,** and their generator `tests/1302/fields.py` (17:02Z, grant 1). The `regen` case holds the two byte for byte, so the text is changed in the generator. No rule and no behaviour changed.
- **`docs/work-orders/TVI-WO-0100b.rm0433.md` §4, row A10** (17:35Z): `CC1OF`–`CC4OF`.
- **`tests/mutants.tsv` and `tests/ci/times.tsv`** (17:02Z, 18:38Z, 18:56Z), 21 mutants:
  - TM34, FL42, US63, I265, AD48 (17:02Z);
  - DX34, PW39 (18:38Z);
  - C36, G25, DM72, I266, AD49, and the per-cell check-tool copies R20–R22, R24–R26, R28, R32, R33 (18:56Z).

## 2. Acceptance

- **Every clause has its job, case and mutant named, with a run id:** `gate.md`, run 45858432173.
  - **This PR's CI on `33dd82e`** (45881160764), the post-`1308` tree: green.
    - **Aggregate:** 1,166 items (518 cases, 648 mutants) across 11 shards, 0 failures. So every one of the 21 new mutants was killed.
    - **Budget:** medians `main` 958,948 KiB, branch 943,560 KiB (−15.0 MiB).
  - **Its first run** (45880658289, `28b5f99`) failed at the route. The inventory refused `spike/1205/consumer-coverage.sh` for naming documentation. It is now `spike/1205/coverage.sh`, which takes the paths as arguments (`33dd82e`).
  - **The 18:56Z mutants, locally:** 19 built, 14 killed by their one cell or sweep.
    - **Removed, because their cells rest on other lines:** R27 and R29–R31. `forge` and `adv-spray` rest on the source filter (R26, R28), `stock-forge` on witness-required (R25).
    - **`adv-removed`:** R23 tried two lines and survived each. The cell rests on two independent lines in two files: the check's witness and end requirement, and the runner's after-marker. Each has its own mutant (R06/R07; S05 by `nomark`). Ruled at 19:10Z: `adv-removed` is held by two independent lines (the check's R06/R07 and the runner's S05), each mutant-proven; by design, no single mutant passes it.
- **The armed run and the 16 replays on post-`1308` `main`:** 0 records, and the armed run arms (9.332 s, 34 witnesses, 34 ends).
- **The checklist's coverage check:** 0 uncovered of 55.
- **The verdict:** one page.

## 3. Adversarial pass (WO §5)

**Who:** a fresh reviewer (Opus) reading the deliverables, the data and the code; nothing run. I checked each finding against the files.

**Fixed:**
- **A1. The counts merged rows and registers.** 412 and 241 are rows; the 97 armed registers outside rows (71 guarded, 26 acknowledged) are counted apart.
- **A2. "A named case and a mutant that case kills" was not true case by case.**
  - Ten clause-3, 5 and 7 cells were named by no mutant.
  - Five sweeps' mutants also listed a hand probe.
  - The 18:56Z mutants close both.
- **A3. The verdict claimed CI for a tree no CI had run** (post-`1308`), and cited this FEEDBACK before it existed.
- **A4. The "69 inherited" list was wrong.** It is `timer2` 54 (generated boards only), `timer5` 5, `bdma` 5, `nvic` 4 and `dwt` 1. RTC, watchdog and RNG are among the 82 inherited registers outside rows.
- **A5. Six ADC registers counted guarded while the join said "NO clause-6 case".**
  - The join now reads `adc-fieldsmonwrite` and `i2c-isrmonwrite`.
  - It now fails a guarded row with no case, instead of passing it. Re-run: 0 findings.
- **A6. The sibling caveat** missed DMA streams 1–7 (on stream 0's sweep) and `dma2` (on `dma1`'s), 9 registers. The DMAMUX cite was stale (L375; it is L391).
- **A7. The `TIMx_SR` text went beyond its evidence.**
  - BIF/B2IF also set by BG/B2G, whatever BKE/BK2E (p1567).
  - SBIF's sources are on p1566.
  - COMIF's COM event is by p1592 and p1598; TIF's TRG event and TG by pp1597–1598.
  - TIM1's accesses, reads included, are now committed (`tim1-accesses.tsv`).
- **A8. The checklist missed:**
  - R:172's sourceless RCC warnings and `AHB2ENR` (B9);
  - 1306's stale Aethelgard ledger rows and unsourced lines (F5, F6).
  - C6 contradicted B8 after `1308`; A4's 0.94 GiB is 0.90; F lacked a check column.

**Raised and ruled: the unaudited acknowledgements (18:49Z point 1).** 105 rows and 24 registers above the cut are acknowledged only through RUL-17's "inherited, unaudited (acknowledged by name)" class. The ruled cut counted that class as acknowledged; the 17:02Z reading does not. Scotty ruled (b) (relayed 22:53Z): they don't count, so clause 1 is not met. When filling the line I recounted from `main-5d4a61a`'s data: 105 and 24 reproduce. I also checked the passing side against amended `RUL-17`: every acknowledged item holds an `acknowledged` or `stock-wrong` field, except `PWR_CR3` (by R3) (`gate.md` §1.4).

**Withstood:**
- **Every case and mutant the gate names exists,** and its tests column matches.
- **The new mutant rows:** 5 fields, ASCII, old text once.
- **Bindings and overlaps:** all 412 guarded rows are bound to a `Guarded*` type, and every guarded or acknowledged row's fields overlap its bits.
- **The totals reproduce.**
- **A10 and the stock-model line cites are accurate.**
- **The roadmap citations resolve.**

## 4. Reduction pass

**Diff-sized.**
- **Removed:** `spike/1205/harness/inner.sh`, a copy of `1100a`'s with only its comment changed. The harness mounts `1100a`'s own; a re-run still arms at 9.332 s with 0 records.
- **Kept, each a copy with a needed change:** `pipeline.sh` (run labels, the two new sweeps), `replay-armed.sh` (cargo at 6 jobs) and `join.py` (four changes, each commented).
- **Kept as a summary by design:** the verdict restates the gate, as the WO asks for both.

Reduction pass (diff): −32 net · 1 duplicate removed · 0 dead paths removed · 0 protected kept · 2 findings below

## 5. Findings and rows

- **R1. `join.py`'s R3 regex** matches any "ruled <timestamp>" text. Today it moves one row (`PWR_CR3.Res6`). A later ledger row that quotes a ruling while describing a refusal could be promoted; the rule applies only where a row would otherwise count for nothing.
- **R2. `spike/1205/coverage.sh` matches by line.** A roadmap line carrying several obligations passes with one citation (R:172 did, A8).
- **R3. The checklist cites roadmap lines by number.** A roadmap edit shifts them: #67 and #70 shifted them, and each time they were remapped here by exact text (every cited line's text identical before and after).
- **R4. CC5IF/CC6IF on silicon** (setting at a compare with CCR5/CCR6 at 0) is inferred from p1597, not run.
- **R5. 1305a FEEDBACK's "hseFrequency defaults to 8 MHz"** is stale: superseded by 1300b (4). For the architect's docs PR.
- **R6. Clause 4's page check** runs in CI against the committed excerpt. Only a Furnace run checks the excerpt against the held manual (`1203` §5).
- **R7. Clause 6's sweeps prove the guard enforces its table,** not that the table is right. Each row's rightness is its RM0433 citation, held by review.
- **R8. For the architect: the roadmap's `1206` row and `TVI-WO-1206` say the 129 "rest only on" unaudited fields.** That holds for 104 rows. `I2C1.ICR`'s reserved row also holds a reserved field. 23 of the 24 registers hold other fields too, RCC `ResetStatus` an `(A<n>)` one (`gate.md` §1.4). The count is unchanged; the wording is in protected documents.

## 6. Gates

- **WO §7's stops, each taken and ruled:**
  - TIMx_SR (17:02Z);
  - CCxOF's form (17:35Z);
  - DMAMUX1's and PWR's sweep mutants (18:38Z);
  - the unaudited acknowledgements and the per-case mutants (18:56Z; point 1 ruled (b) by Scotty, relayed 22:53Z).
- **The verdict is the WO's last stop.**
- **Not merged by me.**
