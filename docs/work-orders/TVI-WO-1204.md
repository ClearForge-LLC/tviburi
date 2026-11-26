# TVI-WO-1204 — The records' owner and the harness: the teardown race placed, recorded mode made to match its contract, one guard list, a witness per restated instance

**Repo:** `ClearForge-LLC/tviburi` · **Base:** `main`, named in the kickoff.
**Branch:** `wo/tvi-1204-records-harness`.
**Author:** Claude (co-architect) · **Builder:** strong tier (the `1203` builder, who knows the release, the harness and the gate cases), with a full adversarial pass.
**Phase:** 1, a `12NN` slice (identity, the ledger, CI). Written 2026-11-26. **Cadence:** measure (1) and stop; then build, with a stop if a change would move a record's bytes on the console.

**Grounds.** Rows in `docs/roadmap.md` Phase 1, cited by their text:
- **The teardown race:**
  - "loses an end record in about 2 of 6 runs on a loaded Furnace (`1301a`); it fails closed, and CI reruns are the remedy until it is placed";
  - "raced 3 of 14 on the branch against 0 of 14 on `main`, all under host contention" (`1300b`);
  - `1203` FEEDBACK §5 item 5: `crash-runner`'s first form lost 29 of 31 end records at a clean quit.
- **The records' owner (`model/Refusal.cs`):**
  - "recorded mode evaluates every rule for an access … while the comment says 'evaluates no further rule'. The code or the comment moves";
  - "In recorded mode, a refused `HSEON` still reaches the stock model, and `sys_ck` on the HSE then throws at 0 Hz … Recorded mode's contract decides whether a refused write is forwarded" (`1300b`);
  - "Two recorded-mode edges (F10)" (`1306a`);
  - "The records' log level: the witness and end records log at INFO, against `RUL-12`'s 'Error level'" (`1300a` Q4);
  - "the witness cannot yet list the floating pins read: that needs `Refusal.cs`" (`1301a` (d)).
- **The harness:**
  - "`tests/spike.sh` holds the guard-name list in nine copies (`1301`); one list";
  - `1203` FEEDBACK §5 item 3: "No CI check asserts a witness per restated instance";
  - `1203` FEEDBACK §5 item 2: each guard's own `restate` case duplicates `manifest`'s regeneration;
  - "CI's shard plan under-reads by about 15 %" (`1300c`).
- Northstar invariants 1, 5 and 8; `RUL-11`, `RUL-12`, `RUL-19`.

> **What this is:**
> - **(1) Measure the teardown race first.**
>   - Where the end record is lost: the logger's flush, Renode's quit, the runner's read, or the console's buffering.
>   - How often: on `main`, at a clean quit, on an idle host and under load.
>   - Which instants race, from stamps.
>   - **Stop** with the cause and a proposed fix.
> - **(2) Fix the race at its cause,** so that an end record is never lost at a clean quit, and prove it. A loss stays fail-closed; the goal is that a clean run never fails.
> - **(3) Recorded mode as its contract says.** Decide, and say why, for each:
>   - every rule evaluated, or the first only (code and comment agree after);
>   - a refused write forwarded to the stock model or not, so that the `HSEON` case does not throw;
>   - `1306a` F10's two edges.
>   - Each decision gets a planted case and a mutant. Write the contract down in `Refusal.cs`'s header, once.
> - **(4) The records' log level:** INFO or Error, per `RUL-12`, with the consumer's read named. If moving it changes a byte Aethelgard's records-only check reads, **stop** with the line; don't move it.
> - **(5) The witness lists the floating inputs read** (`1301a` (d)), or the row says why not.
> - **(6) The harness:**
>   - one guard-name list in `tests/spike.sh`;
>   - a check that the fixture boot's witnesses name every restated instance;
>   - each guard's duplicate `restate` regeneration deleted in favour of `manifest`'s;
>   - `times.tsv` re-weighed from CI's own timings, so the plan reads within 5 %.
> - **Land the one list first** and say so on the bus: `1308` adds a guard beside you.
>
> It is NOT:
> - a guard's rule;
> - the record grammar (`RUL-11`): a record's fields and order stay as they are;
> - the `OQ-03` cut;
> - `model/GuardedSTM32H7_DMAMUX.cs` or `model/GuardedSTM32H7_PWR.cs` (`1308`).

## 1. Build

1. **Measure (1).** Stop with the cause.
2. **Build (2)–(6)** to the ruling, the one list first. Stop if a change would move a record's bytes on the console (a log level, a field), with the line.
3. Merge `main` before READY.

## 2. Acceptance

- **The race:** 50 clean quits on `main`'s cases, on a loaded host, lose no end record. Before the fix, the same count shows the loss rate. Both numbers go in FEEDBACK.
- **Recorded mode:** each decision has a planted case and a mutant; the `HSEON` case does not throw.
- **The 14 boots, the fixture and the armed run:** no new record, and the armed run arms.
- **The harness:** one list; the per-instance witness check fails when a planted restatement witnesses nothing; the plan within 5 % of CI.
- **CI is green, with the budget judged.**

## 3. Fences

- **Fence:**
  - `model/Refusal.cs` (the records' owner), except the grammar;
  - the runner and the harnesses (`tests/spike.sh`, `tests/*/` runner code), `tests/ci/*`;
  - each guard's `restate` case, for the deletion only;
  - `docs/work-orders/TVI-WO-1204.*`.
- **Not:**
  - every guard's rules;
  - the release program, except what (6) needs;
  - `1308`'s two models;
  - the steering documents.
- **Running beside you:** `1308`. Land the one list early and say so on the bus.
- One PR, left unmerged. Write `~/.cache/tvi-1204/pr-title-body.md`.

## 5. Adversarial pass, full

- **A race hidden, not fixed:** a longer sleep, a retry or a poll that makes the loss rarer, not impossible.
- **A recorded-mode run that passes,** by any path, against clause 7.
- **A witness check that counts, not names:** 30 witnesses that aren't the 30 restated instances.
- **One list that a guard can still miss:** a case or a CI row that reads its own copy.

## 7. Flag-and-stop conditions

- **After (1),** with the cause.
- **A record's bytes on the console would move.**
- **Recorded mode's decision would change a committed expectation.** Stop with the case.
