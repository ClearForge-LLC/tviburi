# TVI-WO-1204 — (1) the teardown race, measured to its cause

**Builder:** Claude (Opus 5.5), Furnace, 2026-11-26. **Branch:** `wo/tvi-1204-records-harness`, from `main` `89301d0`.
**State:** WO §7's first stop: the cause and a proposed fix, for the architect's ruling. Nothing is built.
**Disclosed:** no predictions were committed before these runs. The cause was read from Renode's source first (§1), and the runs were designed to confirm or refute it (§2).

## 1. The cause, read from the source that runs

**The source:** renode-infrastructure as built into the pinned image (Aethelgard's `4400h` checkout at `~/.cache/aet-4400h/src/infra`, `src/Emulator/Main/…`). Line numbers are that tree's.

**The path a clean quit takes:**
1. **`Emulator.DisposeAll`** (`Emulator.cs` L98–106) calls `EmulationManager.Instance.Clear()`, then `Logger.Dispose()`.
2. **`EmulationManager.Clear`** (`EmulationManager.cs` L99–101) assigns `CurrentEmulation = GetNewEmulation()`. The setter (L297–300) disposes the *old* emulation first.
3. **`Emulation.Dispose`** (`Emulation.cs` L465–480) disposes the machines (L476), then `CurrentLogger.Dispose()` (L479).
   - **Disposing a machine disposes every peripheral.** Each guard's `Dispose` calls `Records.End()` (`model/Refusal.cs` L103–108), which logs its `end` record through `owner.Log(Info, …)`.
4. **With asynchronous logging, `Logger.Log` only enqueues** (`Logger.cs` L594–606: `entries.Add(entry)`). A background thread takes entries and writes them (L734–777).
   - Asynchronous logging is Renode's default: `use-synchronous-logging` defaults to `false` (L823). Tviburi's runner and tests run Renode with no `--config`, so they use it.
5. **`ActualLogger.Dispose`** (L527–534) calls `StopLoggingThread` (L861–876: `stopThread = true; cancellationToken.Cancel(); loggingThread.Join()`).
   - The thread's `entries.Take(cancellationToken.Token)` throws on cancel, and the loop breaks (L739–747) **with the queue undrained**.
   - Nothing flushes that logger again: step 1's `Logger.Dispose()` → `Flush()` flushes `CurrentEmulation`'s logger, which by then is the *new*, empty emulation's (L399–406).

**So:** every `end` record still queued when the old logger's thread is cancelled is never written, to any backend.
- How many depends on how far the thread has drained when step 3 reaches L479, which is a race with the host's scheduler.
- Under synchronous logging, `Log` writes before it returns (L596–602), and nothing can be lost.
- **Aethelgard's committed `renode.config` sets `use-synchronous-logging = true`** (`tests/1301/fixtures/aet-1c32c8ea-ardupilot/varlib/*/tool/renode.config`). So the consumer's own runs do not race. The race is Tviburi's: its runner and tests.

## 2. Measured

**What ran:** the runner's own `run.resc` (`scripts/tviburi-spike`, variant `modeled`), 50 clean quits per variant.
- The release and firmware are built once per variant from the tree.
- Each variant ran in one container under `~/bin/capped-run` (8G), `nice -n 10`, `--network none`.
- Per run, counted: the `end` and `witness` records in the log file and on the console, and Renode's exit.
- "Loaded" means 12 busy loops in the same container, beside the runs. Furnace had 20 cores and its own load from other builders.
- **Harness:** `~/.cache/tvi-1204/h/race.sh` and `all.sh`. **Raw data:** `~/.cache/tvi-1204/race/<variant>/race.tsv`, and each lost run's console and log as `lost-N.*`.

| Variant | Tree | Logging | Host load (1-min, mean) | Runs that lost ≥ 1 end record (log file / console) | End records per run, mean (of 31) |
|---|---|---|---|---|---|
| **A-idle** | `main` `89301d0` (release `292249cf…`) | async (default) | 12.2 | **3 / 3** of 50 | 30.1 |
| **A-load** | `main` | async (default) | 29.7 | **16 / 16** of 50 | 25.9 |
| **B-sync-load** | `main` | **synchronous** (`--config`: `use-synchronous-logging = true`) | 30.8 | **0 / 0** of 50 | 31.0 |
| **C-flush-load** | `main` + one line: `Logger.Flush()` after `Records.End()`'s `Emit` (scratch) | async (default) | 29.0 | **0 / 0** of 50 | 31.0 |

**What the losses look like** (A-load):
- **Every lost run loses a contiguous tail of the end records,** the guards disposed last, in the log file and on the console alike. Written ends per lost run: 1, 13, 3, 29, 0, 30, 27, 24, ….
- **The surviving end records are stamped within 1–10 ms of each other,** and nothing follows them. That is the logging thread cancelled partway through draining (§1 step 5), not a backend or a reader dropping lines.
- **Renode exits 0 in every run;** the runner and the check read what was written.
- **Run A-load 25 lost all 31.**

**Where it is lost, against the WO's four candidates:**
- **Renode's quit:** yes. The old emulation's logger is disposed with its queue undrained.
- **The logger's flush:** it never reaches that logger.
- **The runner's read:** no. The lines are absent from both files.
- **The console's buffering:** no. The console loses exactly the same records as the log file in every lost run.

**B and C are controls,** each under the heaviest load measured:
- **B (synchronous logging):** no loss, which confirms the cause.
- **C (a flush after each end record, still async):** no loss, which tests the proposed fix.

**B's first attempt is not counted:** all 50 runs exited 134. Its `--config` file sat on a read-only mount, and Renode writes a lock file beside its config. Re-run with the file copied to `/tmp`: the row above.

## 3. Proposed fix, for the ruling

**(a), recommended: the records' owner flushes after each `end` record.** In `Records.End()` (`model/Refusal.cs`), call `Logger.Flush()` right after the `end` record's `Emit`.
- **Why it works:** under asynchronous logging, `Flush` (L610–639) stops the thread, writes every queued entry, flushes the backends and restarts the thread. All of that happens before `Dispose` returns, so before step 3 reaches the logger's `Dispose`. Under synchronous logging it only flushes the backends.
- **Why at the cause and not a mitigation:**
  - The record is written before the logger can be torn down, whatever the scheduler does.
  - It is not a delay, a retry or a poll.
  - It does not depend on the consumer's configuration.
- **What it moves:** no record's bytes, and no order. The `end` record is the same line, written at the same point in the sequence.
- **What it costs:** one flush per guard at teardown, 31 for the board. Not measured separately.
- **Proof for (2):**
  - A-load's 50 loaded quits rerun on the branch: 0 lost, against 16 on `main`.
  - A planted case that runs a clean quit loaded and requires every end record.
  - A mutant that drops the flush, killed by that case. A single loaded quit loses with probability about 0.3 (16 of 50), so the case repeats the quit (about ten times) to kill it reliably.

**(b), the alternative a cautious reader might add: run Tviburi's runner and tests under synchronous logging,** as the consumer does.
- It fixes Tviburi's own runs, but not any other consumer that logs asynchronously. (a) fixes both.
- It also changes the runner's environment for every case. A log line logged from another thread could move relative to the console's own lines, and I have not measured whether any committed expectation reads that order.
- **Not recommended as the fix.** It is worth considering as a separate ruling, for the harness to match the consumer.

**After the ruling:** the build follows the WO's order, starting with (6)'s one guard list, which I land first and tell `tvi1308` about.
