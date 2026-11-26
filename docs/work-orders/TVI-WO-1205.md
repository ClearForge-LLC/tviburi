# TVI-WO-1205 — Phase 1's gate, judged; and the consumer's checklist for Phase 2

**Repo:** `ClearForge-LLC/tviburi` · **Base:** `main`, named in the kickoff.
**Branch:** `wo/tvi-1205-gate-judged`.
**Author:** Claude (co-architect) · **Builder:** strong tier (the `1203`/`1204` builder, who wrote the gate proofs), with a full adversarial pass on the verdict.
**Phase:** 1, closing. Written 2026-11-26. **Cadence:** audit on `main` now; clause 1's final row once `TVI-WO-1308` (#64) merges; then a stop with the verdict for Scotty.

**Grounds:**
- **`OQ-03`, ruled by Scotty 2026-11-26: the recommended cut** (`TVI-WO-1100a.cut.md`; architecture `OQ-03`). Above the line are the four flight paths, with their DMA routes. DMAMUX1 and PWR, the last two families, are `1308`.
- **Phase 1's exit gate,** clauses 1–7 (roadmap). Clauses 1 and 6 read "a planted write", as amended and ratified on 2026-11-26.
- **`1100a`'s audit** (`TVI-WO-1100a.measure.md` §5) found clauses 3, 4 and 6 partly met; `1203` (#61) closed them in CI. `1204` (#63) placed the teardown race and made recorded mode match its contract.
- **Phase 2's work** (roadmap): Aethelgard's half needs a list of what its consumer must do. Today that list is scattered across about a dozen "For Aethelgard (Phase 2 seam)" rows.
- Northstar invariants 1, 2, 3, 5 and 8; `RUL-09`–`RUL-12`, `RUL-17`, `RUL-19`.

> **What this is:**
> - **(1) The gate, clause by clause, on `main`.** For each of clauses 1–7:
>   - the CI job, case and mutant that prove it, by name and with its last green run;
>   - what a skeptic would try to make it pass falsely, and the case that stops it;
>   - met, partly met with the gap named, or not met.
>   - Clause 1 is judged row by row against the cut: every path above the line is **guarded** (a guard refuses it by name) or **acknowledged** (a ledger row names it). Anything else is a gap, named.
> - **(2) Clause 1's last rows when `1308` merges:** re-run the armed run and the 16 replays on the merged `main`, and re-join the queue to the cut. That's the step that turns "partly met" into "met", or names what isn't.
> - **(3) The consumer's checklist for Phase 2:** one page, `docs/work-orders/TVI-WO-1205.consumer.md`. It gathers every "For Aethelgard (Phase 2 seam)" row in the roadmap and every consumer-facing finding in the work orders' FEEDBACK into one ordered list Aethelgard's work orders can be cut from: the class map, the `-> dmamux1@N` lines, the platform properties (revision, external levels, battery level, crystal), `[ERROR]`-line parsing, the DMA guard's names for streams 1–7, synchronous logging, and so on. Each item cites its source row, says what the consumer must do, and says how Tviburi's release lets it check.
> - **(4) The verdict:** a one-page `docs/work-orders/TVI-WO-1205.verdict.md` for Scotty, giving the gate met or not met with the evidence per clause, and the rows that carry over into Phase 2 or Phase 4.
>
> It is NOT:
> - a new guard or a rule change (a gap found is a row, or a stop if it is above the cut);
> - the release itself (Phase 2);
> - Aethelgard's work;
> - the steering documents (the verdict says what the roadmap should record; the architect's PR records it).

## 1. Build

1. **Audit (1) and write (3) on `main` now.** These don't depend on `1308`.
2. **When `1308` merges,** do (2) on the merged `main`.
3. **Write the verdict (4) and stop.** Merge `main` before READY.

## 2. Acceptance

- **Every clause** has its proving job, case and mutant named, with a run id. Clause 1 is judged row by row against the cut.
- **The armed run and the 16 replays on the post-`1308` `main`:** zero records, and the armed run arms.
- **The consumer checklist** cites every "For Aethelgard" row in the roadmap. A check shows that none is missed: grep the roadmap's rows against the checklist's citations.
- **The verdict** fits one page.

## 3. Fences

- **Fence:**
  - `docs/work-orders/TVI-WO-1205.*`;
  - the queue join's scripts and data under `spike/1205/`;
  - read-only runs of every suite.
- **Not:** every model, the release program, `tests/` (a gap is a row), the steering documents.
- **Running beside you:** `1308` (#64), finishing its review fixes.
- One PR, left unmerged. Write `~/.cache/tvi-1205/pr-title-body.md`.

## 5. Adversarial pass, full

- **A clause met on paper:** a job named that doesn't run what the clause says, or a case that passes for another reason (`1203`'s lesson).
- **A cut row counted as guarded** when only its sibling instance is guarded, or acknowledged by a ledger row that names a different field.
- **A consumer item missed,** or one that Tviburi's release gives the consumer no way to check.

## 7. Flag-and-stop conditions

- **A path above the cut** that is neither guarded nor acknowledged. Stop with the row.
- **A clause not met** that a test, not a guard, would close. Stop with the test you propose.
- **At the verdict.**
