# Tviburi

A digital twin for commercial microcontrollers that refuses to claim it has simulated anything it has not actually modeled.

**Status:** Phase 1. One guarded peripheral, SPI1 on the STM32H7, refuses its unmodeled paths and passes unmodified ArduPilot's SPI start (`TVI-WO-0100a`, `0100b`, merged). The RCC guard is being built (`TVI-WO-1300`). Nothing is released yet (`TVI-WO-1200` builds the release). No consumer uses Tviburi yet. The supported entry points are Tviburi's runner (`scripts/tviburi-spike`) and the records contract: a load witness, then an end record, with no refusal, on Renode's log under the guarded peripheral's name. A consumer checks it with `tools/tviburi-check`. Renode's own exit status is not a verdict.

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

Licensed under either of MIT ([`LICENSE-MIT`](LICENSE-MIT)) or Apache-2.0 ([`LICENSE-APACHE`](LICENSE-APACHE)), at your option.
