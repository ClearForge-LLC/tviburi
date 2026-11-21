# Tviburi — architecture

**Status:** draft for ratification, 2026-11-21. **Supersedes nothing.** Read after `northstar.md`.
**Author:** Grok (external) · **Gate:** Scotty · **Builder:** a separate build session.

---

## 1. What it is

A forked and annotated Renode platform (starting with STM32H7) whose unmodeled paths refuse by name, plus a versioned gap-ledger artifact that downstream tools record by hash.

The ownership boundary is deliberate: Tviburi owns the models and the statement of what they do not yet model. Aethelgard (or any other consumer) owns the specification-to-board-and-firmware path and the gates that judge the pair. The interface is the same one Aethelgard already uses with Renode: a sandboxed external tool that produces a replayable trace.

## 2. Ground truth measured

At the time of writing there is no Tviburi code. The measurements that constrain the design are the ones Aethelgard already published:

- Renode 1.17.0 is instruction-level with 1 ns virtual resolution, not cycle-accurate.
- SPI1 on the NUCLEO-H753ZI raised `MODF` and cleared `MASTER` on the first `SPE`; Renode did not model it.
- I2C clock-synchronization thresholds and several error flags are incomplete.
- Landlock + seccomp sandboxing around the tool is already solved in Aethelgard and can be reused.

These are recorded so the first work orders do not re-measure them.

## 3. Rulings

| ID | Ruling | Reason |
|---|---|---|
| RUL-01 | Start from a Renode fork of the STM32H7 platform, not a clean-room simulator. | The CPU, bus, and virtual-time engine are the expensive parts. The gaps are in fidelity and honesty, not in the existence of a simulator. |
| RUL-02 | Unmodeled paths refuse; they do not pass. | This is northstar invariant 1. Silent success is the defect the project exists to remove. |
| RUL-03 | The gap ledger is emitted by the twin as a versioned artifact (CBOR or JSONL). Consumers store the hash. | The ledger is a property of the model version. Copying the list into every campaign would drift. |
| RUL-04 | First peripherals for explicit refusal work: advanced-control timers, DMA (memory-to-peripheral and circular), brown-out / voltage detector. | These cover the majority of safety-critical "twin passed, silicon would have faulted" surface once simple polled SPI/I2C is left behind. |
| RUL-05 | Stay in Renode's C# model shape for the first usable version. | Re-expressing models in another language is a later auditability step, not a prerequisite for the refusal contract. |
| RUL-06 | Work-order prefix is `TVI`. | Distinct from Aethelgard's `AET`. Ids must not collide across the two repos. |

## 4. Seams

- **Model vs consumer.** Tviburi does not import Aethelgard crates. Aethelgard does not import Tviburi models. The only shared artifact is the trace format plus the gap-ledger hash.
- **Renode host vs model.** The refusal logic lives in the peripheral models and a thin runtime check. It does not require changes to Renode's CPU or bus.
- **Gap ledger vs trace.** The ledger is emitted once per model build. The trace is emitted per run. They are separate files.

## 5. Open questions

| ID | Question | Decider | Blocks |
|---|---|---|---|
| OQ-01 | Exact on-disk format of the gap ledger (CBOR vs JSONL, field names). | Scotty | First implementation work order |
| OQ-02 | Whether the first fork vendors Renode or uses it as a submodule. | Scotty | Repository layout |
| OQ-03 | Minimum viable set of timer/DMA/BOR flags that must refuse before the first silicon re-check. | Scotty + builder | Exit gate of the first platform phase |

## 6. Amendments

| Date | Section | Change | Rationale |
|---|---|---|---|
| 2026-11-21 | — | Initial version | Seed |

## 7. Provenance

Written 2026-11-21 by Grok. Rests on the 2026-11-21 conversation and on Aethelgard's published measurements of Renode. No Tviburi code existed.
