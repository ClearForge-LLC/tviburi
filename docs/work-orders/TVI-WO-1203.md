# TVI-WO-1203 — Phase 1's gate proofs: the ledger's over-claim fixed, and clauses 3, 4 and 6 proven in CI

**Repo:** `ClearForge-LLC/tviburi` · **Base:** `main`, named in the kickoff.
**Branch:** `wo/tvi-1203-gate-proofs`.
**Author:** Claude (co-architect) · **Builder:** strong tier, with a targeted adversarial pass.
**Phase:** 1, a `12NN` slice (identity, the ledger, CI). Written 2026-11-26. **Cadence:** build, with one stop if a fix would move a committed record.

**Grounds:** `TVI-WO-1100a`'s audit (PR #59, `TVI-WO-1100a.cut.md` and `.measure.md`) at `main` `39e51af`, CI run 45774342008.
- **The ledger over-claims guarding,** which is invariant 2's falsifier. `pins.tsv` has no instance list for `SPI.STM32H7_SPI` or `Timers.STM32_Timer`, so the ledger says `spi4`, `timer2`–`7` and `12`–`17` are `guarded_by`, while the run binds them stock.
- **Clause 3,** "cells unproven": a removed guard and a crash in Aethelgard's console framing, and a crash at Tviburi's runner.
- **Clause 4,** partly met: the stock half of the `MODF` scar passes because the run has no witness, not because stock reached the end. The page match is checked by hand.
- **Clause 6,** 20 rows unproven: I2C `ISR.TXE`/`TXIS`, and ADC `SQx`, `AWD1CH`, `CCR.PRESC`.
- **Wording:** clauses 1 and 6 say "a planted firmware", but every sweep is monitor writes (`pc: none`).
- **Also:** the RCC and DMA guards, and the PWR stub, log their registers as `unknown` in the access log.
- Northstar invariants 2, 3, 5 and 8; `RUL-11`, `RUL-17`, `RUL-19`.

> **What this is:**
> - **(1) The class map tells the truth.**
>   - Every guarded class lists the instances it guards, and the ledger says `guarded_by` only for an instance the run binds to the guard.
>   - A check refuses the release if the ledger names a guard for an instance the platform binds stock: planted, with a mutant.
>   - The ledger rows that change are named, and the release's model hash moves once, named.
> - **(2) Clause 3's three cells, each a CI case:**
>   - a removed guard in Aethelgard's console framing;
>   - a crash in that framing;
>   - a crash at Tviburi's own runner.
>   - Each must be a non-pass at the runner and at the records-only consumer check.
> - **(3) Clause 4, proven by what it claims:**
>   - the stock run reaches its end, by a witness the case reads (the end marker `0xAF7E0002` that `1100a` measured);
>   - the guarded run refuses;
>   - the condition text is checked against the held RM0433 page by a test, not by hand.
> - **(4) Clause 6's 20 rows,** each with a refuse case. Where a row is reachable only by a monitor write, say so in the case's name.
> - **(5) The access log names every guarded register.** No `unknown` for the RCC and DMA guards; the PWR stub's names come with `1308`'s guard, so leave it to `1308`.
> - **(6) For the architect** (wording only, not built): propose a reading of clauses 1 and 6's "a planted firmware" that matches what the sweeps do. A steering-doc change is the architect's PR, so put it in FEEDBACK.
>
> It is NOT:
> - a new guard;
> - the `OQ-03` cut;
> - any change to a guard's rules beyond (5)'s log names;
> - the steering documents.

## 1. Build

1. **Build (1) first,** since `1308` adds instances through it. Then (2)–(5). Stop only if (1) would move a committed record other than the ledger's own rows and the model hash.
2. Merge `main` before READY.

## 2. Acceptance

- **The over-claim check** refuses a planted false `guarded_by`, and `main`'s ledger passes after (1).
- **Every clause-3 cell, the clause-4 halves and the 20 clause-6 rows** are CI cases, run in a shard and named in the aggregate.
- **The 14 boots, the fixture and the armed run:** no new record, and the armed run arms.
- **CI is green, with the budget judged.** Re-weigh your rows from this run.

## 3. Fences

- **Fence:**
  - the release program, the class map and `pins.tsv`;
  - the ledger generator and its checks;
  - the RCC and DMA guards' access-log names only;
  - `tests/` cases for clauses 3, 4 and 6;
  - your own rows in `tests/ci/*.tsv` and `tests/mutants.tsv`;
  - `docs/work-orders/TVI-WO-1203.*`.
- **Not:**
  - `model/GuardedSTM32H7_DMAMUX.cs` and `model/GuardedSTM32H7_PWR.cs` (`1308`, beside you);
  - every guard's rules;
  - `model/Refusal.cs`'s grammar;
  - the steering documents.
- **Running beside you:** `1308`. Land (1) early and say so on the bus, so `1308` can merge `main` after it.
- One PR, left unmerged. Write `~/.cache/tvi-1203/pr-title-body.md`.

## 5. Adversarial pass, targeted

- **A case that passes for another reason,** as clause 4's stock half did.
- **A `guarded_by` still true for an instance bound stock,** by a class name the check doesn't parse.
- **A crash case that times out instead of crashing,** read as a pass of the case.

## 7. Flag-and-stop conditions

- **(1) moves a committed record** other than the ledger's rows and the model hash.
- **A clause-6 row cannot be refused** without changing a guard's rule. Stop with the row and the page.
