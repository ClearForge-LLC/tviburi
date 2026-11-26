# TVI-WO-1100a — The queue re-measured on the armed run, the `OQ-03` cut proposed from data, and Phase 1's gate audited

**Repo:** `ClearForge-LLC/tviburi` · **Base:** `main`, named in the kickoff.
**Branch:** `wo/tvi-1100a-cut`.
**Author:** Claude (co-architect) · **Builder:** strong tier, with a targeted adversarial pass on the audit.
**Phase:** 1, a refinement of `1100`. Written 2026-11-26. **Cadence:** a measurement and an audit, then a stop for Scotty's ruling on the cut. Nothing is built before it.

**Grounds:**
- **Architecture `OQ-03`:** "Minimum viable set of timer/DMA/BOR flags that must refuse before the first silicon re-check … To be answered from data", and not ruled. The armed window now exists: Aethelgard `AET-WO-4400h` (#393), then `4400i` and `4400j`, which arm ArduPilot inside the CLI's own bounds.
- **Roadmap, Phase 1 exit gate clause 1:** "every path in the queue (`1100`, joined with `1150` (v)) up to the cut ruled under `OQ-03` refuses by name". Clauses 2–7 are each checkable in CI.
- **The roadmap's critical path:** "Scotty rules the `OQ-03` cut … a longer or armed window comes first".
- **Since `1100`:** the guards `1300`–`1307`, with `1300a`–`1300c`, `1303a`, `1305a` and `1306a`, have taken many queue rows. Nobody has re-joined the queue to what is guarded now.
- `RUL-13`, `RUL-17`, `RUL-18`; northstar invariants 1, 5 and 8.

> **What this is:**
> - **(1) The queue, re-joined, with predictions committed before the runs:**
>   - `1100`'s queue (722 rows over the generated boards) and `1150` (v)'s ArduPilot rows;
>   - joined with ArduPilot's **armed** run (the run `1307` and `1300c` replay: `ACCEPT_USART_DMA="usart1:41"`), and with any row that run reaches that the 5 s window did not.
>   - Each row's state on `main` today:
>     - **guarded** (which guard, which case refuses it);
>     - **acknowledged by name** (where);
>     - **inherited and unaudited**;
>     - **not reached** by any committed firmware.
> - **(2) The cut, proposed from data, ranked by what flies first:**
>   - every row ArduPilot's armed run reaches on a motor, a sensor bus, the command link or the clock tree;
>   - each with the cost to guard it (the family's guard, an existing one or a new one);
>   - a line below which the rest waits for Phase 4's bench.
>   - Recommend one cut and say why. Give the alternative a cautious reader would pick, with its cost.
> - **(3) Phase 1's exit gate, audited clause by clause.** For each of clauses 2–7: met or not, by which CI job, case or test, at `main`'s head. For clause 1: met up to the proposed cut, or the list of rows still open below it.
> - **(4) Scotty's decision, in one page** (`docs/work-orders/TVI-WO-1100a.cut.md`):
>   - the proposed cut;
>   - what it leaves for Phase 4;
>   - the work orders it implies, sized as slices: one per peripheral family still open, not one per row;
>   - the gate's remaining clauses.
>
> It is NOT:
> - any change to a model, a guard, a test, CI or the steering documents (`docs/work-orders/TVI-WO-1100a.*` and `spike/1100a/` only);
> - the cut's ruling, which is Scotty's;
> - Aethelgard's Phase 2 work.

## 1. Build

1. **Measure (1),** predictions first, from committed replays and the armed run in the pinned image, one container at a time.
2. **Write (2)–(4).** **Stop** on the bus with the cut page and the audit table. Merge `main` before READY. The PR carries the record only.

## 2. Acceptance

- Every queue row has a state with its evidence: a case id, a guard file and line, or the log line that shows it unreached.
- The audit names, for every exit clause, the job or test that proves it, or says it is unproven.
- The cut page fits on one page and is readable by someone who was not in the room.
- CI is green (route and docs).

## 3. Fences

- **Fence:** `spike/1100a/` and `docs/work-orders/TVI-WO-1100a.*`.
- **Not:** every other file.
- One PR, left unmerged. Write `~/.cache/tvi-1100a/pr-title-body.md`.

## 5. Adversarial pass, targeted, on the audit

- **A clause marked met on a test that does not exercise it,** or on a run that passed for another reason.
- **A row marked guarded whose case refuses a neighbour,** not the row itself.
- **A row marked not reached that the armed run does reach,** for example one hidden by the log's summarising or a recorded mode.

## 7. Flag-and-stop conditions

- **After (4),** with the cut page (the planned stop).
- **The armed run no longer arms on `main`.** Stop with the run.
- **A clause of 2–7 is unmet on `main`.** Name it in the audit, and say whether it is a work order or a fix.
