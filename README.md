# Tviburi

A digital twin for commercial microcontrollers that refuses to claim it has simulated anything it has not actually modeled.

**Status:** Phase 0: the steering documents are being ratified. One spike is built and under review (`TVI-WO-0100`, PR #4); nothing is released, and no consumer uses Tviburi yet.

## Where to go

| I want to know | Read |
|---|---|
| Why this exists and what must stay true | [`docs/northstar.md`](docs/northstar.md) |
| What it is and how it is shaped | [`docs/architecture.md`](docs/architecture.md) |
| What order it gets built in, and what "done" means | [`docs/roadmap.md`](docs/roadmap.md) |
| How a build session works here | [`CLAUDE.md`](CLAUDE.md) |
| Past reviews and the blind comparison | [`docs/reviews/`](docs/reviews/) |

## Relationship to Aethelgard

Aethelgard is the first consumer. It will load Tviburi's guarded models into the Renode it already runs, pinned by hash, and fail a gate on any refusal. Tviburi owns the models and the gap ledger. Aethelgard owns the specification-to-board-and-firmware path, its trace, and the gates. What crosses between them is in [`docs/architecture.md`](docs/architecture.md) §1 and §4.

---

This README points. It does not define. The invariants, the rulings, and the phases each have exactly one home.
