# TVI-WO-1150 — FEEDBACK

**Builder:** Claude (Opus 5.5), Furnace, 2026-11-23. **Branch:** `wo/tvi-1150-seam-spike` from `main` `dc55f98`.

**State:**
- All seven items measured, (i)–(vi) and (vii), which the architect added. The results are in `spike/1150/results.md`, one section per item, each ending with its ruling.
- **Two of WO §7's stops fired,** and the architect ruled on each on the bus:
  - **(i):** no `.repl` form re-types a stock instance. Ruled at 11:00Z: continue, and add (vii).
  - **(vi):** the three-file overlay takes ArduPilot past 1 GiB. Ruled at 11:45Z: the overlay ships as one file; continue.
- Spike-only: nothing under `model/`, `scripts/`, `tests/` or `tools/` changed. Aethelgard is untouched.
- **The gates were not run.** The PR is left unmerged.

**Commits:**
- `f26ed6c`: questions and predictions, before any run;
- `7060f72`: (i), the stop;
- `ef87d9b`: (ii), the console fixtures (posted on the bus for `0100b`);
- `1903910`: (i) addendum, (iii), (iv), (vii);
- `57ba30c`: (vi) cost, the second stop;
- `d01547c`: (v), and (vi)'s reset and DMA;
- `169d1e7`: the reduction pass;
- then this FEEDBACK.

---

## 1. What was measured (headlines; the evidence is `results.md`)

1. **(i) Re-typing.**
   - **No form re-types.** Every typed redeclaration of `timer1` or `rcc` is E02, `local` included, and Renode exits 0.
   - **The name can be kept:** `@none`, then the guard under a new variable `as "timer1"`, or declared again by a second description. The first is a consumer trap: a later untyped `timer1:` update lands on the cancelled stock variable, silently.
   - **`STM32H7_RCC` is `sealed`.**
2. **(ii) Console.**
   - The records are identical in the console and the `logFile` of one run.
   - Under Aethelgard's configuration, the `logFile` framing is the virtual stamp and two spaces, not `RUL-11` (b)'s host stamp.
   - **`0100a`'s check reads neither.**
   - A `\r` can precede the `end` record; there is no interleave, and no folding.
   - Aethelgard's empty environment makes `0100a`'s guard refuse everything (A2, run).
3. **(iii) Release constant.**
   - A generated constant file outside the manifest works with `include @`.
   - The consumer's file hashes catch an edited model, and a witness-only consumer does not.
   - **With the compiler cache on, a `const` silently reports the previous release,** and `static readonly` crashes, which fails closed.
4. **(iv) Recorded mode.** The mode comes from the platform description. **`0100a`'s unchanged check passes a recorded run with no `would-refuse`,** because it never reads `mode`; the architect ruled that a defect, for `0100b`.
5. **(v) ArduPilot.**
   - 386,068 writes on `sentinel-02-own`, 5 s, with the control holding.
   - **DMA writes are logged under the PC of the CPU store that enabled the stream.**
   - 7,117 `would-refuse`, all A6's path.
   - The join puts the RCC kernel-clock selectors and sleep enables first among ArduPilot's dropped writes. GPIO routing is written but held. TIM1 and TIM2 are not reached in 5 s.
   - 122 ArduPilot-only rows.
6. **(vi) Cost and reset.**
   - Cost is per compilation: +45, +170 and +290 MiB for 1, 2 and 3 files. Three files are over 1 GiB in every run.
   - Across a reset: one witness, `seq` continues, the record stays, the guard's `IOLOCK` latch clears as silicon's does, one `end`.
   - A monitor-caused refusal has `pc: none`; a DMA-caused one has the enabling store's PC.
7. **(vii) RCC.**
   - (a) A wrapper plus 58 restated registrations works, and is not displaced.
   - (b) A guard beside a moved or narrowed stock RCC works, with a bypass address or an unguarded slice.
   - (c) A hook is displaced by one `SetHook`.

## 2. Consumer-side findings (Aethelgard). Named, not fixed

1. **The `as "<stock name>"` trap** ((i) addendum). Its emitter writes untyped updates for 14 timers and 8 USARTs. With a cancelled-and-restated instance, they silently configure the cancelled variable.
2. **The bus locks an unclocked peripheral after the cold writes** ((vi)): "Tried to write a locked peripheral", at WARNING. A monitor write made after a brown-out rendering, before the firmware re-enables the clock, never reaches the model. Aethelgard's filter keeps these lines, but nothing judges them for harness writes.
3. **A DMA write carries a CPU PC** ((v), (vi)). Any rule that reads "`pc` is a CPU address" as "the CPU wrote this register" is wrong for DMA targets. Response §4.7's discriminator holds only for "no CPU executing".
4. **The stock ArduPilot console is 8,312,549 B against an 8 MiB budget.** Recorded mode adds 5.9 MB of records, which the filter would mostly drop (read, not run through the CLI).
5. **A stock ArduPilot boot can exceed 1 GiB under host load:** 1,379,148 KiB at 26.4 s wall, load 16 (`AET-WO-4400f` F1 again). The bound's margin is load-dependent, overlay or not.
6. **`I2C1.CR1.ERRIE` = 1 is written by ArduPilot and ignored by the model** (`1100` finding 2.4, now taken by firmware that flies).

## 3. Findings about Tviburi's steering documents. For the architect; the documents are unchanged

1. **`RUL-11` (b)** describes the `logFile` framing as a host stamp. Under the consumer's configuration it is virtual, with two spaces ((ii) 2).
2. **`RUL-07`** cannot hold for the RCC, because it is sealed ((i), (vii)). The architect amends it from (vii).
3. **`RUL-09`:** a class map serves the instances the consumer declares or restates, not a stock file's ((i)). One file per release ((vi)), as ruled.
4. **`RUL-10`:** a constant in a separate file is two compilations. Under one file, the consumer checks the shipped file against the concatenation of the manifest's files and the filled template ((vi) note; not prototyped). The compiler cache off is a consumer requirement, as ruled.
5. **Response §4.7 ("`pc: none` is a harness access")** needs "and a real PC is not proof of a CPU write" ((vi)).
6. **Northstar §6 ("so slow or heavy"):** the overlay's memory cost is the binding constraint on ArduPilot, not its wall time.

## 4. Process and tooling

- **Bus and rulings.**
  - The two stops were posted to `architect.md`, and the rulings came back at 11:00Z and 11:45Z.
  - One question to the operator through the question dialog was interrupted; the ruling came on the bus instead.
- **The mint for the fixture push failed** at 11:02Z (Cloudflare 502 on the S25 proxy). As the kickoff says, I did not loop, and `0100b` was told to fetch the fixture commit locally. One retry at the PR (§7).
- **Mistakes caught before they reached a result, all disclosed:**
  - The case runner fed Renode the case list on stdin, so only the first case ran. Fixed with `< /dev/null`.
  - `consumer-check.sh` split the manifest's file list on newlines inside a `case` pattern, so every file read as "not in the manifest". Fixed before any result used it.
  - The per-mode cost files overwrote repeats. The full list was rewritten from the printed outputs (`cost-runs.txt`).
  - The first (vii) read of candidate (a) missed its write lines because of my own `awk` filter. The writes were there.
  - The first held-core probes in (vi) never reached the guard (the bus lock). They were rerun with the clock enabled, and both runs are kept.
- **Order:** (vi)'s cost runs were made before (v)'s access log, because they share its replay. That is said in `results.md`.
- **`RUL-14`:** the scratch readers are Rust (`reader/records.rs`, built with `rustc` alone, reusing the check's `json.rs` by `#[path]`), `awk`/`gawk`, POSIX shell, and an IronPython monitor script (`retype/checks.py`). No build file is committed.
- **Disk:** about 1.2 GB under `~/.cache/tvi-1150`, plus the CLI build in the worktree's untracked `target-aet/` (§7).

## 5. The adversarial pass (WO §5), targeted

- **A re-typed instance that loads but is not the guard.** For every form that loads with a spike class (`n1`, `n3`, `n5`, `vii` (a) and (b)), a CPU write and a monitor write were shown to reach the spike's own write path (`SPIKE` lines with the PC). The (i) control `c0` shows the classes load and construct. On ArduPilot, the type-string change on `spi1` and `spi2` was proven by both guards' witnesses and 7,117 records from the guards. The timer forms were proven by the spike timer's write log, not by a refusal: no timer guard exists.
- **A console line the grammar accepts whose source is not the guarded peripheral, or a record split by the console.**
  - The analyzer's forged lines are rejected by anchoring the source after the level. A substring reader would accept 3 forged lines.
  - Multi-line continuations are rejected.
  - No split was seen in 15 consoles.
  - The residue is consumer-side: the monitor and Aethelgard's own hooks can log under a guarded name.
  - No firmware route was found.
- **A release constant that matches while a model file differs, or a recorded run any reader could take as a pass.** Case B (an edited model) and case E (a stale manifest) both pass a witness-only consumer. Recorded `modeled` passes `0100a`'s check. Each is caught by the prototyped consumer check.
- **An ArduPilot write the access log misses.**
  - **DMA writes are not missed, but misattributed** to the enabling CPU store's PC. Checked for `spi2`'s `TXDR` (ArduPilot) and `SPI1_CFG2` (`fw/dma.rs`) only.
  - Writes made before logging is enabled are the harness's cold writes, not ArduPilot's.
  - A write hook installed before logging was checked not to displace it: `AETHELGARD-CFG2` still fired 4,740 times, and `spi2` `CFG2` was logged 4,707 times.
  - Nothing runs after the window.

## 6. Independent review

A fresh subagent was given `results.md` and the evidence paths, not my conclusions about them. It recomputed the claims that carry a ruling from the raw outputs, read-only.
- **Confirmed:**
  - every (vi) cost figure, and the over/under statements;
  - the (v) tier table, the 122 ArduPilot-only rows, "52 of 172 (6/46/120)", 386,068 writes, and the 25,073 `TXDR` writes with their two PCs, matched to `dma1` `0x58`;
  - (ii)'s identity of framings, the two-space `logFile` stamp, and the `\r` before `end`;
  - (iii)'s case table, rerun with `consumer-check.sh`;
  - (iv)'s four check verdicts, rerun;
  - every (i) and (vii) form's result;
  - the (vi) reset table.
- **Wrong, and corrected:**
  - **7,115 `would-refuse` → 7,117.** The per-value breakdown was right, and only the total was mis-added.
  - **"418 `\r` characters" → 831 characters, on 418 lines.**
  - **The status-register count:** my 58 left out `FLASH`, the review's 63 left out SPI's `IFCR` (16 rows), and the stated list had omitted `IFCR`. The full count is **79**, now listed by register. That disagreement was a finding in itself.
- **Weak, and marked:** (vii) (b2)'s "the stock RCC answers `0x00`–`0x4F` unguarded" is inferred from the narrowed registration; it was not probed.
- **Holes it named, now stated:**
  - `n1`, `n3` and `n5` were proven by the spike timer's own write log, not by a guard's refusal; there is no timer guard.
  - The DMA check covers `spi2`'s `TXDR` on ArduPilot and `SPI1_CFG2` in (vi), not DMA writes to other peripherals.
  - "14 timers and 8 USARTs" is now counted in the captured platform: 14 `timerN:` and 8 USART/UART update entries.
  - The 26.4 s stock outlier is under the 30 s bound.

## 7. Close

- **Reduction pass (diff): −51,867 net · 0 duplicates removed · 0 dead paths removed · 0 protected kept · 1 finding below.**
  - `data/writes-ap.tsv` went from 52,129 per-value rows to 262 per-register rows. Every count, value and PC that `results.md` cites is kept (it sums to 386,068; `dma1` `0x58` lists the `TXDR` PCs). The full log regenerates from `harness/replay-ap.sh 2 logged`.
  - **Finding:** `overlay/Records1150.cs` keeps an unused `Sha256` helper and three unused `using` lines from `0100a`'s `Refusal.cs`. They are not removed, because those bytes are release A (`a4a12a17…`), which every (iii), (iv) and (vi) run cites. `0100b` or `1200` should drop them in the real runtime.
- **The gates were not run.** The change is spike-only, and no model, script, test or tool changed.
- **Aethelgard is untouched.** `git -C ~/tvi-1150-aet status --porcelain` is empty at `7dec1d7b`, and the push URL is disabled.
- **Scratch:** `~/.cache/tvi-1150` (the captures, replays and cases) is kept until the PR is ruled, so every excerpt can be regenerated. The CLI build in `target-aet/` is removed at close.

## 8. Questions for the architect and Scotty

1. **`OQ-03`** (Scotty's): `data/queue-ap.tsv`'s order puts 17 RCC rows ArduPilot writes and Renode drops first. `1100`'s 120 motor-timer rows are not reached by ArduPilot in 5 s. Should a longer or armed ArduPilot window be measured before the cut? It needs Aethelgard's foreign window, or an armed scenario.
2. **(vii):** (a) the wrapper with 58 restated registrations; or (b1) the guard beside a moved RCC, with its bypass address. The architect's choice, for `13NN`'s RCC guard.
3. **DMA attribution:** should a guard record that a write arrived inside another peripheral's write (for example, from the stack, or from the bus's current initiator)? That needs a probe in the pinned build, which was not made.
4. **Recorded mode's volume** against Aethelgard's 8 MiB budget: should a recorded run emit each distinct `would-refuse` once, with a count, rather than once per access?
