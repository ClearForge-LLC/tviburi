# TVI-WO-1100 — The coverage map: what Aethelgard's firmware writes that stock Renode drops, ordered by what flies first

**Repo:** `ClearForge-LLC/tviburi` · **Base:** `main`, named in the kickoff.
**Branch:** `wo/tvi-1100-coverage-map`.
**Author:** Claude (co-architect) · **Builder:** standard tier, with a targeted adversarial pass.
**Phase:** 1. **Cadence:** a measurement that stops. No model code.

**Grounds:**
- **Northstar** invariants 1 and 2.
- **Architecture:** `RUL-04` (amended), `RUL-13`, `OQ-03`; §2.0, what the consumer does today.
- **Roadmap:** Phase 1, the `1100` row.

> **What this is:** the ordered work queue for Phase 1's guarded paths, measured from the consumer rather than guessed.
> - **(1) The consumer's view.** In a read-only clone of `ClearForge-LLC/aethelgard` at its `main`, build the CLI. For every committed board under `boards/`, collect the witness ledger that `aethelgard verify` reports (`crates/aethelgard-cli/src/ledger.rs`). List every row classed `ignored-by-model` or `same-source`, with its board, register, field, value and the Renode source line the row cites.
> - **(2) The simulator's view.** Run each committed board's generated firmware on the pinned Renode build, with access logging on every peripheral the firmware touches, the way Aethelgard's `simulate` runs it. Record every write that lands on:
>   - a tag;
>   - an unimplemented register;
>   - a field the stock model discards.
>   Name the model class and the source line for each.
> - **(3) The join,** keyed by peripheral class, register and field, with a column for each view. A row in one view and not the other is a finding: name why.
> - **(4) The order.** Rank the joined rows by what flies first, and write the rule before ranking: motor output (timers, DShot DMA), then clocks (`RCC` kernel-clock selectors), then power (BOR, PVD), then sensors' buses, then the rest. Within a class, a row on more boards ranks higher.
> - **(5) A proposed cut for `OQ-03`:** the prefix of the queue that Phase 1's gate should require, with the reason for where it falls. Then stop.
>
> It is NOT:
> - any guarded model;
> - any change to Aethelgard;
> - any write to Aethelgard's repository.

## 1. Measure

1. **Commit the ranking rule and predictions** in `spike/1100/` before any run: which families you expect at the top, and roughly how many rows.
2. **Run (1) and (2).** Every container goes through `~/bin/capped-run`, one at a time, `nice -n 10`, `--network none` after any build. Aethelgard's worktree is yours alone: clone it to `~/tvi-1100-aet`, and never touch another worktree.
3. **The join, the order and the cut** go in `spike/1100/queue.md`. A scratch reader may live under `spike/1100/`, with no build file committed.

## 2. Acceptance

- **Every committed board is covered,** in both views, or named as not covered with the reason.
- **Every row cites its source:** the ledger row for (1), and the Renode source line or the access-log line for (2).
- **The cut has a reason.**
- **No model code changes.** Spike-only: the gates are not run. Say so.

## 3. Fences

- **Fence:** `spike/1100/` and `docs/work-orders/TVI-WO-1100.FEEDBACK.md`.
- **Not:** `model/`, `scripts/`, `tests/`, the steering documents, or Aethelgard.
- **Running beside you:** `TVI-WO-0100a`. Your fences do not overlap.
- One PR, spike-only, left unmerged. Write `~/.cache/tvi-1100/pr-title-body.md`.

## 5. Adversarial pass, targeted

- **A field the ledger classes as judged that the stock model still drops.** That is a consumer-side finding. Name it in FEEDBACK; do not fix it.
- **A write the access log misses,** for example a DMA-driven write rather than a CPU store.
- **A rank that hides a flight-critical path** low in the queue.

## 7. Flag-and-stop conditions

- **Stop after §1.3** with the queue and the proposed cut.
- **The consumer's CLI will not build, or its ledger cannot be collected** without changing Aethelgard. Stop and name what is needed.
