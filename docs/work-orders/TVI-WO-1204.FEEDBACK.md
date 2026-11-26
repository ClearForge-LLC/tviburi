# TVI-WO-1204 — FEEDBACK

**Builder:** Claude (Opus 5.5), Furnace, 2026-11-26. **Branch:** `wo/tvi-1204-records-harness`, from `main` `89301d0`.
**Rulings followed:**
- **11:26Z:** (1) accepted; build (a); (b) is a row.
- **12:21Z:** P1–P3 accepted; (4) keeps INFO; (5) is a row.
- **P2's mechanism:** see §1 (3) below.

**Evidence:** `~/.cache/tvi-1204/`: the race harness `h/`, the measure runs `race/`, the proof runs `proof/` and `two/`, the CI logs `ci/`, the cases' outputs `suite/`. Kept until the architect says.

## 1. What was built

### (1) The teardown race, measured to its cause

**Where it is written:** `TVI-WO-1204.measure.md`. Stopped there for the ruling.

**The cause:** at a clean quit, Renode disposes the old emulation's machines, then that emulation's logger.
- Under asynchronous logging (Renode's default, and Tviburi's runner and tests), the logger's writer thread is cancelled with its queue undrained.
- So a queued `end` record is never written.
- Aethelgard's committed `renode.config` is synchronous, so the consumer never raced.

### (2) The race, fixed at its cause

**The fix:** `Records.End()` calls `Logger.Flush()` right after it emits the `end` record (`model/Refusal.cs`). Each record is written before the guard's `Dispose` returns.

**The proof:** `tests/1204/teardown.sh`, the runner's own `run.resc`, 12 busy loops in the container, host load about 24.

| Tree | Clean quits | Lost an end record |
|---|---|---|
| `main` (release `292249cf…`) | 50 | **11** |
| branch (`dea1144`) | 50 | **0** |
| `main`, two machines (62 end records a run) | 20 | **8** |
| branch, two machines | 20 | **0** |

- **The measure file's A-load row** was 16 of 50 on `main`, at a heavier host load.
- **CI case `teardown`:** 15 clean quits, each needing every guard's witness and end record, in the log file and on the console. The first is the runner's own run, unloaded; it builds the firmware, check and release. The other 14 run beside 12 busy loops (3 per core on a 4-core CI runner).
- **Mutant `TD01-endFlush`** drops the flush; `teardown` kills it.

**The adversarial checks the ruling named:**
- **Several guards ending in one dispose, each flushing; nothing is reordered.**
  - The branch's last loaded quit was compared line by line with variant B's (synchronous logging, `main`), with stamps, hashes and virtual times masked.
  - Every line is the same and in the same order: 31 witnesses, then 31 end records in dispose order (`rcc`, the GPIO ports, the timers, the DMAs, flash, the USARTs, the I2Cs, the ADCs, `spi1`).
  - **One line differs:** Renode's own `[INFO] machine-0: Disposed.`, after the last end record, is missing from the flush run. That is not a record, and nothing reads it (§5, F1).
- **The flush reaching the old emulation's logger when more than one machine disposes:**
  - Two machines on the Tviburi board, 20 loaded quits each side.
  - `main` lost an end record in 8; the branch lost none, and every run had all 62 end records.

**The rows that name the race are closed by this fix,** each taken and fixed by `1204` (`TVI-WO-1204.measure.md`). The roadmap rows themselves are the architect's to close.
- **`1301a`:** "loses an end record in about 2 of 6 runs on a loaded Furnace; it fails closed, and CI reruns are the remedy until it is placed" (FEEDBACK item 6).
- **`1300b`:** "raced 3 of 14 on the branch against 0 of 14 on `main`, all under host contention" (§5.1).
- **`1203`, §5 item 5:** `crash-runner`'s first form lost 29 of 31 end records at a clean quit.
- **`tvi1308`'s 1305 losses today:** five `main` runs of `tests/1305/usart.sh` lost 0, 6, 11, 0 and 11 cases, every failure "no end record", at load 10–30 (bus, 11:36Z).

**(b), synchronous logging in Tviburi's harness, is a row, not built (§5, R1).**

### (3) Recorded mode, as its contract says

**The contract is written once, in `model/Refusal.cs`'s header.** The nine guards' headers cite it.
- **Enforcing:** the first rule that fires refuses the access; no later rule is evaluated for it.
- **Recorded:** every rule is evaluated for the access, in the guard's order, and each rule that fires records a `would-refuse`. `Records.Evaluates(fired)` is the one predicate.
- **Recorded:** the access goes on to the stock model and to the guard's copies as if no rule had fired.
- **After a would-refuse,** what follows is the stock model's behaviour, not a guard's promise. A recorded run is never a pass. `1306a` F10's two edges are named there as examples.

**P1, every rule evaluated (recorded only).**
- **Code:** the SPI and RCC guards, the two that stopped at the first rule, now evaluate every rule through `Records.Evaluates`. Enforcing still short-circuits, and its paths are unchanged rule for rule.
  - **SPI:** the access chain (LOAD, ENABLE, WIDTH), the order rule, the write rules (IOLOCK, CFG2-SPE, CFG1-SPE, I2SMOD, FIELD, the frame size, the truth table, KERNEL), and the read rules (READ, IOLOCK, MODF).
  - **RCC:** the write chain (HSE, PLL, PLL-REF, SW, FIELD, CLOCK).
  - **The grain is the rule:** each rule ID is evaluated. Within one rule, its first failing condition records.
  - **Found in the adversarial pass:** the SPI's inner write chain was still first-only at first (§3). The new `recorded` check, SPE on the reset SPI recording `TVI-SPI-MODF` and `TVI-SPI-KERNEL` at the one write, pins it, and mutant `RM05-spiKernelEvery` is killed by it.
- **The one committed expectation that moves, as ruled:** `tests/spike.sh recorded`, from "one record per access" to "every rule per access". The 32-bit TXDR write `0x41`, with 8-bit frames, records both `TVI-SPI-WIDTH` and `TVI-SPI-ORDER`. The check now pins each record's offset.
- **Planted:** `tests/1300/rcc.sh recordedtwo`. One `RCC_CR` write, `0x00010003`, on a recorded board with no crystal, records `TVI-RCC-HSE` then `TVI-RCC-FIELD` (`HSIKERON` = 1) at that write.
- **Mutants:**
  - `RM01-evaluatesAll`: `Evaluates` short-circuits in recorded mode; killed by `recorded` and `rcc-recordedtwo`.
  - `RM02-spiOrderWord`: SPI's 32-bit order rule short-circuits; killed by `recorded`. Its first form, on the 8-bit chain, survived: the case's TXDR write is 32-bit, so that mutant changed nothing the case runs. Re-anchored.
  - `RM03-rccField`: RCC's field rule short-circuits; killed by `rcc-recordedtwo`.

**P2, a refused write is forwarded, and the HSEON case does not throw.** Ruled B at 12:51Z.
- **What I proposed at 12:21Z, and why it failed:** I proposed masking HSEON. Before building it I ran it, which I had not done when I proposed it.
  - The throw comes from the forwarded `SW = HSE` write, not from HSEON: `Frequency must be greater than 0`, through `Pass` → the stock CFGR callback → `PropagateClock` → `LimitTimer.set_Frequency`.
  - The stock model's `sys_ck = HSE` reads its constructor's `hseFrequency` and never checks HSEON (`STM32H7_RCC.cs` L1522–1529).
  - So masking HSEON alone would not stop the throw. I stopped and gave A and B.
- **What was built (B):** in recorded mode with no crystal declared, the owned stock RCC runs on a placeholder 8 MHz (its own default) so a run can continue past `TVI-RCC-HSE`. No guard reads it.
  - **Enforcing** still gives the stock model 0.
  - **Named once each:** `Refusal.cs`'s contract, the RCC guard's header and constructor, and the `HSEON` ledger row (`tests/1300/allowlist.py`, regenerated).
  - **This is the one place a guard changes what its stock model runs on in recorded mode.**
- **Planted:** `tests/1300/rcc.sh hsesw`, the same monitor script in both modes: no crystal, `HSEON`, then `SW = HSE`.
  - **Recorded:** `TVI-RCC-HSE` is recorded, then CFGR reads `0x12` (SWS = HSE), the marker, rcc's end; never a pass; nothing throws.
  - **Enforcing:** `TVI-RCC-HSE` refuses first, and nothing throws.
- **Mutant `RM04-hsePlaceholder`:** gives the stock model 0 in recorded mode; killed by `hsesw`.
- **Adversarial, as ruled: does any guard read the stock model's HSE frequency? No.**
  - The RCC guard reaches `inner` only through `ReadDoubleWord` (16 sites), `WriteDoubleWord` (2), `Register`, `Unregister` and `Reset`. Those are register bits; no frequency property.
  - No guard reads a `.Frequency`, the systick, or any stock clock property.
  - The SPI, ADC, flash and I2C guards reach the RCC only through the guard's own `Holds`, `QueryKernelClock` and `KernelHz`, which read `HseFrequency`. Nothing outside the RCC guard reads `HseFrequency` directly.

**P3, the contract line, and 1306a F10's edges as named examples.** I2C's flow does not change.
- **Pinned:** `tests/1306/i2c.sh recnack`, the first edge. On a recorded board, a START to the BME280 inside a NACK's pending automatic STOP records `TVI-I2C-TRANSFER` and goes on. The NACK's `NACKF` and `STOPF` still land, inside the new transfer.
- **Measured:** ISR reads `0x33`, which is TXE and TXIS from the new transfer plus the old NACK's NACKF and STOPF.
- **Mutant `I264-recNackStopf`:** drops the NACK's STOPF; killed by `recnack`.
- **The second edge** (an uncleared NACK STOPF keeping controller mode) is named in the contract, not pinned. No measured firmware does either edge.

### (4) The records' log level: INFO, kept

**Why it stays:** moving the witness and end records to Error changes their console bytes (`[INFO]` → `[ERROR]`, one byte longer, everything after shifted). The records-only check reads those bytes: `tools/tviburi-check` `split_line` parses the `[LEVEL] ` token and accepts any of the five levels. So this was WO §7's stop, and it was ruled at 12:21Z: keep INFO.
- **The architect amends `RUL-12`'s text** in the next docs PR: "witness and end at INFO; refusal records at Error, which no level suppresses; a raised log level hides a witness and so fails closed (`adv-loglevel`)".
- **A Phase 2 row (R2):** confirm that Aethelgard's parser does not treat Renode's `[ERROR]` lines as a failure on their own, so a refusal is read as a refusal record.

### (5) Floating inputs in the witness: a row (R3)

**Why the witness can't carry them:** it is emitted at registration, before any pin is read.
- **The end record could, but not here:** adding a field moves an end record's bytes on the console (WO §7), and it is the record grammar, outside this WO's fence.
- **What a consumer sees today:** the witness's `external`, the levels declared.

### (6) The harness

**One guard list** (`adeafd7`, pushed first and told to `tvi1308` at 11:17Z):
- **`scripts/guards` declares the list.** The runner and every harness read it, and the 16 copies across nine files are gone.
- **Why it is declared, not derived from the board:** a list derived from the platform under test drops a removed guard. Measured: in that form, `removed-console` stopped failing.
- **`tests/spike.sh guards` holds it to the committed board,** through `tests/1204/board-guards.awk`:
  - it equals the board's 31 guarded instances;
  - a planted guard on the board (`spi2`) that the list misses fails;
  - no line elsewhere in `scripts/`, `tests/` or `.github/` may name ten or more guards. On `main`'s tree that rule finds all 11 files that held a copy.
- **Mutant `G17-namesLine`** is re-anchored to the list.

**The fixture boot's witnesses name every restated instance** (`927f6e9`):
- **`tests/1301/witnessed.sh`** needs a witness, by name, for every instance the release's MANIFEST restates or retypes. Its parser was read against a 1304 fixture console.
- **The budget job runs it** on each branch boot of the consumer's fixture, and fails the verdict if any boot lacks one.
- **`tests/spike.sh witnessed`** plants consoles made from the tree's own MANIFEST:
  - all present passes;
  - `timer8` missing fails, naming it;
  - the same count of witnesses under other names fails;
  - a planted restatement (`timer5`) with no witness fails, naming it.

**Each guard's duplicate `restate` regeneration is deleted** (`aefbabb`), in favour of `tests/spike.sh manifest`:
- DMA's `restate` case is gone.
- The flash, USART, I2C and ADC `regen` cases keep only their Fields regeneration.
- Mutants `DM34`, `FL32`, `US28`, `I234` and `AD43` now name `manifest`, and each is killed by it.

**`times.tsv` re-weighed** from `main`'s last CI run (45804127687, #61's merge) by 1202a's gap method, at the 6975P-C's speed. The `compare.py` and `reweigh.py` scripts are in `~/.cache/tvi-1204/ci/bin/`.
- **Before:** the old weights read the run's seven 6975P-C shards at 1.022–1.097 of plan, 2–10 % slow.
- **After, in-sample:** 0.960–1.014, every shard within 4.0 %.
- **New items are estimated:** `teardown` 250 s and `TD01-endFlush` 260 s from Furnace runs; the rest are small or at their kind's median.
- **Out of sample, this PR's own CI:** §2.

## 2. Acceptance

- **`tests/spike.sh`, all cases,** passes on the P1 tree (ALL PASS). It passes again on the reduction tree, capped at 6 cargo jobs (13:53Z), with `teardown` rerun on `bf839d8` after the fix in §4: 0 of 15 lost.
- **`tests/1300/rcc.sh`, all cases,** passes on the reduction tree: 76 ok, 0 FAIL (`recordedtwo` and `hsesw` included).
- **The cases (3) adds or moves, on HEAD:** `rcc` `hsesw`, `hse-none`, `hse-zero`, `recorded`, `recordedfield`, `recordedtwo`, `fields`; `i2c` `recnack`, `nacktransfer`, `recorded`; `spike` `recorded`, `manifest`. All ok.
- **The 14 generated boots and ArduPilot's fixture,** replayed by the committed `tests/1301/replay-1301.sh` with every guard recorded, on `main` and the branch, capped. Harness: `~/.cache/tvi-1204/h/loop.sh`; outputs: `rep/`.
  - **Records:** 0 on every one, both trees.
  - **Witnesses:** the same counts on both trees: 30, 31 or 32 per board, and 32 on the fixture.
  - **Console:** every boot block's hash is identical, `main` against the branch.
  - **Moved:** no record on the consumer's console. P1 adds none here, and no board runs the RCC with no crystal.
  - **The fixture's peak,** one boot under host load: 1,250,180 KiB on the branch against 1,047,340 KiB on `main`. One sample; the budget's 11-boot medians judge it (below).
- **The armed run** (`1305`'s capture of `4400h` `s03-arm-s`, the 1100a harness with cargo capped). Both trees arm from 9.332 s, 32 witnesses and 32 ends, 0 records.
- **The other harnesses' cases that run the RCC, SPI or their own guard recorded:** `usart` `rxoff`, `hzpllon`, `hzport`, `idlenone`, `idlehse`, `recorded`; `timer`, `dma`, `flash`, `adc` and `gpio` `recorded` and their siblings. These also prove each harness's `GUARDS=` line: 14 ok, 0 FAIL.
- **Mutants:** every mutant this WO adds or re-anchors is killed (listed in §1 and §3).
  - Added: `TD01`, `RM01`–`RM05`, `I264`.
  - Re-anchored: `G17`, `DM34`, `FL32`, `US28`, `I234`, `AD43`, `M09`, `M12`, `M26`, `M31`, `T05`, `T06`, `RB06`.
- **CI on the PR:** the route, ten shards, the budget (with the witness check), and the aggregate: written here after the PR's first CI run, with the plan against CI per shard.

## 3. Adversarial pass

**Who:** a fresh subagent, reading the diff, Renode's logger source and the stock RCC; nothing run. I checked each finding against the files.

**Found and fixed:**
- **A1. Five mutant rows had gone stale.** `M26`, `M31`, `T05`, `T06` and `RB06`: their old text was removed by this WO's rewrite (`mutants.sh` requires exactly one match). Re-anchored with the same intent. After the next fix, `M09` and `M12` were re-anchored too. Each is killed.
- **A2. The SPI's inner write chain still stopped at the first rule,** against the contract. `RefusedWrite` and `EnablePasses` now evaluate every rule in recorded mode; they are unchanged in enforcing. Pinned by the new `recorded` check, with mutant `RM05`.

**Withstood:**
- **Enforcing is unchanged.** Every rewritten SPI and RCC chain evaluates the same rules, in the same order, with the same returns: `Evaluates(fired)` is `!fired` there.
- **Recorded side effects.** The rules now reached after an earlier one fired only record and read state. The SPI's history, IOLOCK and master state change only after the checks, as before.
  - The RCC's `RefusedClockLoss` mutates and restores its copies in a `finally`.
  - **Residual, a row (R5):** that rule throwing on a state an earlier rule refused is untested beyond `recordedtwo` and the full `rcc.sh`.
- **`Logger.Flush` from `Dispose`.**
  - It flushes the dying emulation's logger: the `CurrentEmulation` setter disposes the old emulation before reassigning.
  - It drains in order with one writer, so nothing is reordered (measured, §1 (2)), and it has no lock cycle with the machine lock.
  - 31 flushes are 31 join and restart cycles.
- **The placeholder.**
  - The stock RCC uses `hseFrequency` only for `ref1_ck` and the HSE clock (`STM32H7_RCC.cs` L114, L1529), so no register value depends on it.
  - It is passed only when `HseFrequency` is null and the mode is `recorded`. A declared 0 is null. A bad mode string is a load failure, which is never `Recorded`.
- **The harness:**
  - `witnessed.sh`'s framing matches `split_line`;
  - `teardown.sh` cannot pass vacuously: a missing log gives an empty count;
  - `$(/repo/scripts/guards)` is evaluated in the container wherever it sits in a container script.
- **From §1 (2):** the record order, and the two-machine dispose.

## 4. Reduction pass

**Who:** a fresh subagent, read-only over the diff. I applied the in-fence cuts after checking them, then reran what they touch.
- **The rerun caught a bug in my first form of the `teardown.sh` cut.** It wrote the runner's output into `/out` before the runner ran, so the runner refused (it needs `/out` empty) and the case failed closed, 15 of 15.
  - Fixed in `bf839d8`: the output now goes to the container's `/tmp`. The same commit fixed the load at 12 busy loops; the default had been 3 × nproc, 60 on Furnace.
  - Rerun: 0 of 15 lost, and `TD01` killed.
- **Applied:**
  - `teardown.sh` runs the runner once (quit 1 of 15) instead of copying its firmware build, check build, release build and `run.resc`.
  - `scripts/guards`' dead argument is gone from its 8 call sites, and the harnesses' "from the board" comments are corrected (the list is declared).
  - The RCC constructor's and the placeholder constant's comments, `hsesw`'s comment, and `board-guards.awk`'s header no longer restate the contract or `declared_guarded`.
- **Declined:**
  - Moving the seven harnesses' `GUARDS=` call into their containers: each line calls the one list rather than copying it, and renumbering seven container scripts' arguments is risk without a removed copy.
  - Folding `ci-memory.sh`'s `witnessed.txt` into a pipe: the file is the boot's evidence.
- **Findings, out of fence:**
  - **R6.** The four guards DMA, I2C, USART and ADC spell `Records.Evaluates` inline, as `!refused || records.Recorded`, 21 times.
  - **R7.** `board-guards.awk` and the release's `declared_guarded` parse the same `.repl` grammar, over different file sets.
  - **R8.** `witnessed.sh`, `tviburi-check`'s `split_line` and `spike.sh`'s `aet_framed` each read RUL-11's framing.

Reduction pass (diff): −23 net · 9 duplicates removed · 1 dead paths removed · 0 protected kept · 3 findings below

## 5. Findings and rows

- **F1. Renode's own `machine-0: Disposed.` line can still be lost** at a clean quit. It follows the last guard's flush. Nothing reads it; recorded here so nobody mistakes it for a regression.
- **R1, (b): whether Tviburi's harness should run under the consumer's synchronous logging.** Measured: Aethelgard's committed `renode.config` is synchronous, so the consumer never raced (variant B, 0 of 50). Not built.
- **R2, the Phase 2 seam:** confirm that Aethelgard's parser does not treat Renode's `[ERROR]` lines as a failure on their own, so a refusal is read as a refusal record.
- **R3, (5):** the witness cannot list the floating inputs read. It needs a grammar WO that adds a field to `end`, and Aethelgard's read of it.
- **R4, `1306a` F10's second edge** (an uncleared NACK STOPF keeping controller mode): named in the contract, not pinned.
- **R5:** recorded-mode rules reached after an earlier rule fired, on states that rule refused, are tested only by the cases above (§3).
- **R6–R8:** the reduction findings (§4).
- **For the architect's docs PR:**
  - `RUL-12`'s level text (ruled 12:21Z);
  - the race rows (§1 (2)), closed in the roadmap.

## 6. Gates

- **WO §7's stops, each taken and ruled:** (1)'s cause (11:26Z); (3)'s committed expectation and (4)'s console line (12:21Z); P2's mechanism, which I measured as insufficient before building it (12:51Z).
- **Not merged by me.**
