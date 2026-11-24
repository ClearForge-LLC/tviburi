# TVI-WO-1202a — A sixth shard: CI back under 20 minutes on the slowest runner, with every case and mutant still run once

**Repo:** `ClearForge-LLC/tviburi` · **Base:** `main`, named in the kickoff.
**Branch:** `wo/tvi-1202a-sixth-shard`.
**Author:** Claude (co-architect) · **Builder:** standard tier, with a targeted adversarial pass.
**Phase:** 1, a refinement of `TVI-WO-1202`. Written 2026-11-24. **Cadence:** a build, with one stop only if a flag-and-stop condition fires.

**Grounds:**
- **`TVI-WO-1202`** (merged, #33): five shards, weighed by measured time, with an aggregate that proves every case and mutant ran exactly once. Its FEEDBACK §6 row: "Wall time is set by the slowest runner (0.85–1.29 × plan across CPU models): room for about two more guards before a 6th shard."
- **The two guards have landed** (roadmap, Phase 1 rows):
  - `1304` (#38): its run took 23.5 min against 14.7 min for the same tree one run earlier;
  - `1305` (#40, run 45481212731): shard 1 took 21.1 min, and the others 13.8–16.4.
- **Scotty, 2026-11-24:** keep CI as lean as possible, within reason.
- `RUL-14`, `RUL-15`.

> **What this is:**
> - **(1) Re-weigh** `tests/ci/times.tsv` from CI's own timings on `main` since `1305` merged, every case and every mutant, and not from Furnace's timings.
> - **(2) Six shards.** The plan's longest shard, multiplied by the slowest measured runner factor (1.29), is under 20 minutes. If six shards cannot meet that, say what would: a seventh shard, or a case that dominates.
> - **(3) The aggregate counts six.** A missing, skipped or cancelled shard fails it, as with five.
> - **(4) Measured on this PR:** its own `ci` wall time, every shard's time, and the plan's prediction for each, side by side.
>
> It is NOT:
> - dropping, selecting away or weakening any case, mutant or check;
> - any change to `model/`, `firmware/`, a case, a mutant, or the memory budget's judgement;
> - a self-hosted runner, or Quench. Tviburi is public, so its CI stays on GitHub's runners.

## 1. Build

1. Re-weigh (1), then plan (2). Commit `times.tsv` and the plan's predicted per-shard times first.
2. Build (2)–(3). Push. Record (4) in `docs/work-orders/TVI-WO-1202a.FEEDBACK.md`.

## 2. Acceptance

- Every case and mutant on `main` runs exactly once across the six shards; the aggregate proves it.
- A planted plan that drops one case, or runs one twice, fails the aggregate.
- This PR's `ci` takes under 20 minutes of wall time. If the runner was slow, say so with its factor, and show the plan meets 20 minutes at 1.29.
- A docs-only PR still routes to the fast check.
- CI is green.

## 3. Fences

- **Fence:** `.github/workflows/`, `tests/ci/`, `tests/spike.sh` and `tests/mutants.sh` (the selection and sharding harness only), and `docs/work-orders/TVI-WO-1202a.*`.
- **Not:** `model/`, `firmware/`, any case or mutant, `tools/tviburi-budget/`, or the steering documents.
- **Running beside you:** `TVI-WO-1305a` adds cases and mutants. Whichever merges second merges `main` and re-weighs its new rows.
- One PR, left unmerged. Write `~/.cache/tvi-1202a/pr-title-body.md`.

## 5. Adversarial pass, targeted

- **A case that drops out of the plan:** renamed, newly added with no time, or missing from `times.tsv`.
- **A shard whose failure reads as a pass:** skipped, cancelled, or with no record.
- **A plan that meets 20 minutes only on a fast runner.**

## 7. Flag-and-stop conditions

- **Six shards cannot meet 20 minutes at the slowest runner factor.** Stop with the numbers.
- **A case passes alone and fails in a shard, or the reverse.** Stop with the case.
