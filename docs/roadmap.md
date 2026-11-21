# Tviburi — roadmap

**Status:** draft for ratification, 2026-11-21. **Supersedes nothing.** Read after `architecture.md`.
**Author:** Grok (external) · **Gate:** Scotty · **Builder:** a separate build session.

Work-order prefix: `TVI`. Numbering follows the scheme in the repo-genesis skill.

---

## Phase 0 — documentary backbone

**Exit gate:** the four steering documents exist on `main` and have been ratified or explicitly amended.

This phase is the present commit.

## Phase 1 — refusal contract on a forked STM32H7

**Depends on:** Phase 0.

**Exit gate:** a forked STM32H7 platform in which at least the three named peripheral families (advanced-control timers, DMA memory-to-peripheral/circular, brown-out/voltage detector) refuse by name on their currently unmodeled paths; the gap ledger for that model version is emitted and its hash is stable across two runs; a known silicon scar (SPI1 `MODF` or equivalent) is re-checked and the twin now refuses where Renode passed.

Work orders in this phase are expected to be small and sequential. The first one stands up the fork and the refusal runtime; subsequent ones add the three peripheral families.

## Phase 2 — consumer integration

**Depends on:** Phase 1 exit gate.

**Exit gate:** Aethelgard (or a minimal stand-in) can select the Tviburi backend, run a campaign, and record the gap-ledger hash next to the trace. No model code has moved into Aethelgard.

## Phase 3 — optional

Re-expression of stable models for easier audit, additional vendors, or tighter timing. Not planned until Phase 2 has been used.

## Reconciliation

| Planned | Built | Note |
|---|---|---|
| — | — | No build yet |

## Amendments

| Date | Section | Change | Rationale |
|---|---|---|---|
| 2026-11-21 | — | Initial version | Seed |

## Provenance

Written 2026-11-21 by Grok. The phase boundaries are the ones named in the 2026-11-21 conversation.
