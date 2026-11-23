# CLAUDE.md — how to work in this repository

Read, in this order, before changing anything:

1. `README.md` — what this is and whether it works yet.
2. `docs/northstar.md` — why it exists, and invariants 1 through 8. Cite them by number; never restate them.
3. `docs/architecture.md` — what the consumer does today (§2), the rulings `RUL-01` onward with their reasons, and the open questions.
4. `docs/roadmap.md` — the order things get built in, and each phase's exit gate.
5. Your work order, `docs/work-orders/TVI-WO-PSNN[a].md`. It cites the slice of the above it touches.

Rules that hold in every work order:

- C# for models, and Rust for programs; shell only for thin harnesses (`RUL-14`).
- Every run uses the consumer's pinned Renode build, in the pinned tools image, with no network (`RUL-09`, `RUL-15`).
- Never use an environment variable or a file name as the model's identity (`RUL-10`).
- A refusal is a record on the logger channel, never on a firmware-writable stream (`RUL-11`).
- Every modeled behaviour cites RM0433 by revision and page. Anything from memory is marked "from memory".
- The steering documents, `README.md` and `docs/reviews/**` are protected. Findings about them go in your FEEDBACK, never in a feature branch.
- Close every work order with the adversarial pass, then the reduction pass, then `docs/work-orders/<id>.FEEDBACK.md`, then one PR, left unmerged. The builder never merges.
- On Furnace: no `sudo`, no system packages, no credentials anywhere. Run containers through `~/bin/capped-run`, one at a time. Push with `gh-write`.

This file points; the documents above define.
