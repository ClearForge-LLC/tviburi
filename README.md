# Tviburi

A digital twin for commercial microcontrollers that refuses to claim it has simulated anything it has not actually modeled.

**Status:** Phase 1 spike. SPI1 `MODF` refuses by name on a thin NUCLEO-H753ZI overlay. Run `renode/run-proof.sh` (Renode 1.17.0). See `FEEDBACK.md`.

## Where to go

| I want to know | Read |
|---|---|
| Why this exists and what must stay true | [`docs/northstar.md`](docs/northstar.md) |
| What it is and how it is shaped | [`docs/architecture.md`](docs/architecture.md) |
| What order it gets built in, and what "done" means | [`docs/roadmap.md`](docs/roadmap.md) |

## Relationship to Aethelgard

Aethelgard consumes Tviburi the same way it consumes Renode today. Tviburi owns the models and the gap contract. Aethelgard owns the specification-to-board-and-firmware path and the gates. The only shared artifacts are the trace and the hash of the gap ledger.

---

This README points. It does not define. The invariants, the rulings, and the phases each have exactly one home.
