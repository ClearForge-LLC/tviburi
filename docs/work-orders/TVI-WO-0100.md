# TVI-WO-0100 — stand up the Renode fork of STM32H7 and the minimal refusal runtime

**Repo:** ClearForge-LLC/tviburi · **Base:** main (currently `04cdc510`; verify live HEAD).
**Branch:** wo/tvi-0100-renode-fork-refusal.
**Author:** Grok (co-architect) · **Builder:** Build bot (or as delegated by Chief of Staff).
**Phase:** 1 · **Phase exit gate:** forked STM32H7 platform in which the three named peripheral families refuse by name on unmodeled paths; gap ledger emitted and hash-stable; known silicon scar re-checked and now refuses.
**Grounds:** northstar invariants 1, 5; architecture RUL-01, RUL-02, RUL-04, RUL-05; roadmap Phase 1.

> **What this is:** the first buildable unit. It stands up a Renode fork (or vendored platform) of the STM32H7, adds the thinnest refusal runtime that turns an unmodeled path into a named refusal, and proves it on one concrete path. It is not the three peripheral families, not the gap ledger format finalization, and not any consumer integration.

**Cadence:** spike-first (then STOP). One PR, left unmerged for review.

## 0. Spike first — the questions the spike must answer

1. Can we obtain a clean, buildable Renode platform description for the STM32H7 (NUCLEO-H753ZI or equivalent) without pulling the entire Renode tree into this repo?
2. Where is the narrowest place to insert a refusal check so that an unimplemented flag or side-effect returns a named error instead of success, without rewriting peripheral models?
3. What is the smallest concrete unmodeled path (preferably the already-scarred SPI1 `MODF` or a trivial equivalent) that we can make refuse, and what does the refusal message contain?
4. Does the resulting trace or log still record the access that was refused?

Spike maps the seams, produces a running proof of the refusal on one path, and stops. No further peripheral work until the spike is ratified.

## 1. Scope — numbered, specific

1. Repository layout that can host a Renode platform file and a small set of model overrides or a refusal shim.
2. A refusal runtime (or model-level check) that, on an unmodeled path, emits a refusal containing at least: peripheral name, register or bit, condition, model version / hash.
3. One demonstrated refusal on a known incomplete path (SPI `MODF` or a deliberately stubbed flag).
4. A short note in the PR (or FEEDBACK) recording the exact insertion point and why it was chosen.
5. The branch builds and the refusal can be triggered from a minimal Renode script or test.

## 2. Invariants — restated by number

- **1** — An unmodeled path refuses by name. Silent success on an unimplemented flag is a defect.
- **5** — A refusal names the peripheral, the register or bit, the condition, and the model version.

## 3. Tests / acceptance — what must be proven, not asserted

- A minimal Renode invocation that hits the chosen unmodeled path exits with a named refusal (not success).
- The refusal text contains the four required fields.
- The same invocation on a modeled path (if any exist in the platform) still succeeds.
- The platform file and refusal shim are present in the tree and build/reproducible from a clean checkout.

## 4. Scope fence — what is NOT in this work order

- Implementing the three high-leverage peripheral families (timers, DMA, BOR). Those are subsequent work orders.
- Finalizing the gap-ledger on-disk format (OQ-01). A stub or TODO is acceptable.
- Any Aethelgard integration or trace-format changes.
- Cycle-accurate timing, additional vendors, or re-expression of models in another language.
- Vendoring the entire Renode source tree if a platform-file-only or submodule approach works.

## 5. Adversarial pass — try to break it before calling it done

- Feed a path the model claims to support and confirm it does not refuse.
- Remove or rename the refusal shim and confirm the run now fails closed rather than silently succeeding.
- Confirm the refusal cannot be suppressed by a command-line flag that is not explicitly documented as a debug override.

## 5a. Reduction pass

Diff-sized. Remove any scaffolding that is not required for the single demonstrated refusal.

## 6. Upward-feedback directive

Write `FEEDBACK.md` on the branch. Record the insertion point chosen, any Renode behavior that differed from the architecture's expectations, and whether OQ-02 (vendor vs submodule) needs a ruling before the next WO.

## 7. Flag-and-stop conditions

- Any design that would make an unmodeled path succeed.
- Pulling the full Renode source into the repo if a lighter approach exists.
- Changing northstar invariants.
- Any network call or credential use during the spike.

## 8. Kickoff prompt

Fetch, branch from current main. Read this work order and the three steering documents. Cadence is spike-first then STOP. Produce a running proof that one unmodeled path refuses by name, record the insertion point, write FEEDBACK.md, open one PR, and leave it unmerged. Do not implement the three peripheral families.
