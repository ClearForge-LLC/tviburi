# Tviburi — roadmap

**Status:** ratified by Scotty on 2026-11-23, by the merge of the rework (PR #5) (see Amendments); first drafted 2026-11-21. **Supersedes nothing.** Read after `architecture.md`.
**Authors:** Grok (seed, 2026-11-21); Claude, co-architect (rework, 2026-11-23) · **Gate:** Scotty · **Builder:** a separate build session.

**What to build next** is answered from this document alone: the first work order in the earliest phase whose dependencies are met and whose gate has not passed. There is no other task list.

---

## Numbering

- **The prefix is `TVI`** (`RUL-06`).
- **Ids follow the repo-genesis scheme `<PREFIX>-WO-PSNN[a]`,** where P is the phase, S the subsection, NN the order within it, and a letter marks an inserted refinement. The skill defines the scheme; it is not redefined here.
- **Legacy rule:** `TVI-WO-0100` was numbered before this scheme was adopted. Under the scheme it would read as phase 0, but it is Phase 1 work. **It keeps its id, and so does anything that refines it (`TVI-WO-0100a`). Never renumber an id that has been used in a commit, branch, PR or review.** New Phase 1 work orders start at `TVI-WO-1100`.

## Phase 0 — documentary backbone

**Goal:** steering documents that a builder with no memory of the planning conversation can work from.

**Exit gate:** the four steering documents and a `CLAUDE.md` pointer exist on `main`, and the steering-document rework PR (#5) is merged by Scotty. That merge is the ratification.

**Work:** the rework PR. No work order.

## Phase 1 — the refusal contract on the STM32H7

**Depends on:** Phase 0.

**Goal:** guarded models that refuse by name on the paths the consumer's firmware actually uses, with a hashed ledger and fail-closed records, proven in CI.

**Work orders, in order:**

| Id | What | Cadence |
|---|---|---|
| `TVI-WO-0100` | Stand up the overlay and the refusal runtime, proven on SPI1 `MODF`. **Built twice and judged; the blind build is PR #4** | done; merged via `0100a` |
| `TVI-WO-0100a` | Bring PR #4 to mergeable, with the verdict's seven fixes. **R8:** IOLOCK refuses. **R9:** fail closed per `RUL-12`. Then construction-time records, the harnesses, the stale quotes, the README, and the MODF truth table checked against RM0433 Rev 9 by page. Add CI per `RUL-15` | done; merged as PR #6 |
| `TVI-WO-1100` | **The coverage map** (`RUL-13`, `OQ-03`). From Aethelgard's witness ledger for every committed board, list each `ignored-by-model` and `same-source` field. Run the committed boards' generated firmware on the stock build with access logging, and record which stock models drop which writes. Join the two, ordered by what flies first. Output: an ordered queue of guarded paths, each cited to the row it answers | done; merged as PR #8. Its proposed `OQ-03` cut is not ruled: it waits for `1150` (v) |
| `TVI-WO-1150` | **The seam spike** (review A1–A3, A10, A11; `RUL-09`, `RUL-10`, `RUL-11`, `RUL-13`, `RUL-19`). Measure: (i) re-typing stock instances (`timer1`, `rcc`) in the pinned build; (ii) a console capture under Aethelgard's Renode configuration, committed as the check's fixture; (iii) whether a compiled-in release constant is feasible; (iv) a recorded-mode prototype; (v) ArduPilot's access log on the Sentinel-02 platform, joined into `1100`'s queue format; (vi) the overlay's compile time and memory per boot, and the records across `machine Reset` | a measurement that stops |
| `TVI-WO-0100b` | **Guard fixes on SPI1** (review A6, A7, A8, A12, B1; `RUL-17`, `RUL-18`). The mode-fault pass for `SSM` = 0 with `SSOE` = 1; the `IOLOCK` latch and the D1 history corrected; a `kind` on every refusal; the expected version and unnamed sources in the check; SPI's allow-list, measured against the generated firmware and ArduPilot before it is built. Runs beside `1150`; the check's console grammar waits for `1150` (ii) | one measurement, a stop, then a build |
| `TVI-WO-1200` | **Identity and the ledger.** The overlay manifest and model hash computed from compiled bytes (`RUL-10`); the ledger emitted as canonical JSONL (`RUL-03`), listing every inherited model as unaudited (northstar invariant 2); load witnesses and records on the logger channel (`RUL-11`). **After `1150`:** it builds `RUL-10` as amended, on what `1150` (iii) measured | a build |
| `TVI-WO-13NN` | **The guarded paths, in `RUL-13`'s amended order,** over `1100`'s queue joined with `1150` (v): the RCC enables and kernel-clock selectors first; then GPIO alternate-function routing, from DS12117's table; then the advanced-timer `MOE`/`CCxP` paths and DMA. BOR and DShot DMA wait for a consumer row. One work order per peripheral family. Each states its read rule: which reads it serves from its copies (`RUL-17`), and which it leaves to the stock model (review A11) | builds, one at a time |

**Exit gate.** Each clause is checkable in CI on a pull request, in the pinned image:
1. Every path in the queue (`1100`, joined with `1150` (v)) up to the cut ruled under `OQ-03` refuses by name with a complete record (northstar invariants 1 and 5). A planted firmware that hits each path shows it.
2. Two runs of the same overlay build emit byte-identical ledgers, and changing any overlay file changes the model hash (invariant 3).
3. Removing a guarded model, crashing the simulator, or hitting a refusal each produces a non-pass at Tviburi's runner **and** from a consumer-side check that reads only the records (invariant 8), in both of `RUL-11`'s framings, the committed console fixture included.
4. The SPI1 `MODF` scar firmware refuses where stock Renode passes, and the condition text matches RM0433 Rev 9 by page.
5. A firmware line that imitates a record is not accepted as one (`RUL-11`).
6. For each guarded peripheral, a planted firmware that gives each field outside its allow-list a value the model does not implement refuses with `kind: unmodeled` (`RUL-17`).
7. A recorded-mode run is not a pass at the reference check, whatever else its log holds (`RUL-19`).

## Phase 2 — consumer integration

**Depends on:** Phase 1's exit gate. (`OQ-05`, the licence, is closed.)

**Goal:** Aethelgard's verdicts run on the guarded models, with a refusal that blocks a gate by name.

**Work.** Tviburi's half is a first release, the `RUL-09` artifact. Aethelgard's half is its own work orders, in its own roadmap:
- pin the artifact by sha256 in its tools image;
- apply the manifest's class map in its platform emitter, and for stock instances whatever `TVI-WO-1150` (i) measured (`RUL-09`);
- hash the overlay files it includes against the manifest, and refuse a run with no load witness, a wrong release constant, or a recorded-mode witness (`RUL-10`, `RUL-19`);
- map a refusal record to a named gate failure;
- record the model hash and the ledger hash in its trace header and verdict;
- **a monitor write is a harness access:** a record with `pc: "none"` is one, and the consumer may exclude a refusal its own harness caused only by naming that access, never by a blanket rule (review A11). `TVI-WO-1150` (vi) checks that no other access, a DMA write for one, also shows `none`;
- confirm by a run that a witness line survives its console filter's budget (review A4).

**Decided at this phase:** whether Aethelgard's own sensor models (`models/renode/BMP581.cs`, `ICM42688P.cs`) come under the refusal contract (review B4). Until then they stay outside it, and clause 5 below is about Tviburi's code only.

**Exit gate.** On a committed Aethelgard board:
1. `simulate` and `campaign` run with the pinned overlay.
2. A planted firmware that hits a guarded path fails the gate with the refusal's rule name.
3. Removing the overlay makes the run refuse.
4. The trace header carries both hashes.
5. No Tviburi model code lives in Aethelgard's repository. *(Amended: was "No model code"; Aethelgard's own sensor models are B4's decision, above.)*

## Phase 3 — optional

- Re-expressing stable models for easier audit;
- a second vendor (Aethelgard's `RUL-08` names the RP2350);
- tighter timing.

**Exit gate:** none yet. This phase is not scoped; Phase 4's first run is the spike that shapes it, and its gate is written then.

Not planned until Phase 4 has run once. *(Amended 2026-11-23: was "until Phase 2 has been used".)*

## Phase 4 — the scar loop

**Depends on:** Phase 2, and `OQ-04`. Phase numbers name phases, not order: this phase runs before Phase 3 is planned.

**Goal:** every silicon disagreement found on the bench becomes a permanent regression case, and a cited Tviburi model can turn a consumer's unjudged ledger row into a judged one.

**Shape:** not scoped to work orders yet. The spike that shapes it is the first bench scar after Phase 2 (Aethelgard's bench sessions): carry it into a guarded model, and record what the round trip took.

**Deferred into this phase:** modelling the fault itself where the manual is clear, in place of refusing (review B2): for example `SSI` = 0 setting `MODF` and clearing `SPE` and `MASTER`, as silicon does. Only behind `RUL-18`'s `predicted-fault` rule, a page and a bench row, which is why it waits for the bench.

## Phase 5 — a register-truth layer across vendors and simulators

**Depends on:** a trigger, not a date. Either a target Renode cannot host is scheduled (for example RP2350, ESP32-C6, a C2000 or a dsPIC: no Renode core or SoC base exists for them today), or a consumer needs a refusal to hold the same way in two simulators.

**Goal:** the refusal table of `RUL-16`, generated from each vendor's register files, with adapters into Renode and into a second simulator, and a coverage report per target. It is not a new emulator.

**Exit gate:** none yet; it is written when the phase is scoped.

**Shape:** not scoped yet. The spike that shapes it is the first target past the trigger. When it starts, decide whether it needs its own repository, and if so, name it then (Scotty, 2026-11-23: "if our Renode-like extension or a total rework project needs its own repo home ... we will come up with an appropriate name and create that repo"). The research behind the trigger and the order of vendors is `docs/reviews/2026-11-23-chip-families-research.md`.

## Critical path

- **Where risk concentrates:**
  - `RUL-11`'s channel: if a firmware can forge a load witness, invariant 8 fails silently;
  - the RM0433 citations: a wrong truth table makes a refusal or a pass wrong with full confidence.
  - Both are tested first, in `0100a` and `1200`.
- **What runs in parallel:**
  - `1100` (a measurement) beside `0100a`;
  - `1150` (a measurement) beside `0100b`. Their fences do not overlap; `0100b`'s console grammar waits for `1150` (ii), and `1200` waits for all of `1150`;
  - after `1200`, the `13NN` guarded-path work orders are disjoint by peripheral family, and can run two at a time once CI exists.
- **Human track:**
  - Scotty rules the `OQ-03` cut, after `1150` (v) has joined ArduPilot's log to `1100`'s queue;
  - Scotty rules the replacement for northstar invariant 6 (the review response, "For Scotty");
  - Phase 4's input is a bench session at the Nucleo, which only Scotty can run;
  - Aethelgard's Phase 2 work orders queue behind its own package queue.

## Reconciliation

| Planned | Built | Note |
|---|---|---|
| `TVI-WO-0100`, one build | Built twice: PR #2 (Grok's team) and the blind build, PR #4. Judged blind, 27/40 and 37/40; the blind build is kept (Scotty, 2026-11-23) | The comparison record is `docs/reviews/2026-11-22-*`. PR #2's strengths (every SPI1 access refuses; a non-zero exit) are taken into `0100a` as R8 and R9 |
| `TVI-WO-1100`, the queue from the generated firmware's ledger, motor timers first | Built (PR #8): 722 rows over 13 boards, Sentinel-03 included, and a proposed cut | The review (A10) showed that what flies is ArduPilot, which has no ledger. `RUL-13` amended; `1150` (v) adds ArduPilot; the cut is not ruled |

## Amendments

| Date | Section | Change | Rationale |
|---|---|---|---|
| 2026-11-21 | — | Initial version | Seed |
| 2026-11-23 | all | Rework (repo-genesis): numbering and the legacy rule; Phase 0's gate made checkable; Phase 1 scoped to work orders, its gate split into checkable clauses; Phase 2's gate made concrete on Aethelgard's side; the scar loop added as Phase 4, which runs before Phase 3 (optional) is planned; critical path and reconciliation added | Scotty, 2026-11-23. The scar loop takes the next free number rather than renumbering the seed's Phase 3, because phase numbers are never reused |
| 2026-11-23 | Phase 5 (new) | A register-truth layer across vendors and simulators, started by a trigger, with its own repository decided when it starts | The chip research of 2026-11-23 and Scotty's instruction the same day |
| 2026-11-23 | Phase 1, Phase 2, Phase 4, critical path, reconciliation | Rulings on the Mythos 5.2 review: `TVI-WO-1150` (the seam spike) and `TVI-WO-0100b` (SPI guard fixes) added; `1200` after `1150`; `13NN` in `RUL-13`'s amended order; gate clauses 1 and 3 amended, 6 and 7 added; Phase 2's work and gate clause 5 amended, B4 decided at Phase 2; B2 deferred to Phase 4; `0100a` and `1100` marked done | `docs/reviews/2026-11-23-mythos-5-2-tviburi.response.md` |

## Provenance

- **Seed:** written 2026-11-21 by Grok. The phase boundaries are the ones named in the 2026-11-21 conversation.
- **Rework:** 2026-11-23, by Claude as co-architect. It rests on:
  - `architecture.md` §2;
  - the blind verdict (`docs/reviews/2026-11-22-*`);
  - Scotty's instruction of 2026-11-23 to build Tviburi so that Aethelgard can consume it.
