# TVI-WO-0100a — Bring the blind build to mergeable: IOLOCK refuses, the records contract, the RM0433 check, and CI

**Repo:** `ClearForge-LLC/tviburi` · **Base:** the blind build's head `e47e8d6` (PR #4, branch `claude/tvi-wo-0100-blind-build`), with `main` merged in.
**Branch:** `wo/tvi-0100a-blind-build-fixes`. The resulting PR supersedes PR #4.
**Author:** Claude (co-architect) · **Builder:** strong tier, with a full adversarial pass.
**Phase:** 1. **Cadence:** one check, stop, then the build.

**Grounds:**
- **Northstar** invariants 1, 2, 5, 8.
- **Architecture:** `RUL-07`, `RUL-08`, `RUL-11`, `RUL-12`, `RUL-15`.
- **Roadmap:** Phase 1, the `0100a` row.
- **The blind verdict, §5, "X, before merging"** (`docs/reviews/2026-11-22-tvi-wo-0100-verdict.md`). Read it in full.
- **Your own FEEDBACK** on that branch, R8 and R9.

> **What this is:** the seven fixes the verdict names, made to the rulings the rework has since taken.
> - **(1) R8, IOLOCK: refuse.** Once the firmware has set `CR1.IOLOCK`, any later write the guard's copy of CFG2 would track refuses. The rule is named `TVI-SPI-IOLOCK`. Narrow it only to the fields RM0433 Rev 9 says IOLOCK locks, cited by page. Until it is cited, refuse every CR1 and CFG2 write after IOLOCK is set. **Reason:** invariant 1. The guard's copy must never disagree with silicon in the passing direction.
> - **(2) R9, the records contract** (`RUL-12`). Raw Renode need not exit non-zero.
>   - Each guarded model emits a load witness at construction: a one-line JSON object carrying the model version, on the logger under the peripheral's own name (`RUL-11`).
>   - A run with no witness, or with any refusal, is not a pass.
>   - Ship a consumer-side reference check: a small program that reads only the logger records and exits 0 only on a witness and no refusal. It is in Rust (`RUL-14`), with no crates beyond `std` and `serde_json`, if `serde_json` is needed.
>   - Your runner keeps its exit 3, and calls that check rather than re-implementing it.
>   - The README names the runner and the records contract as the supported entry points.
> - **(3) Construction-time failures** emit a named refusal record with the four fields, not a bare exception.
> - **(4) The adversarial and mutation harnesses** are committed under `tests/` and run by CI. Any result you cannot reproduce in CI is labelled unverified.
> - **(5) Stale quotes:** regenerate every quoted record and trace line from the final code.
> - **(6) The README's status line.**
> - **(7) RM0433:** check the MODF truth table and every condition text against RM0433 Rev 9. It is held on Furnace at `~/.cache/aet-held/rm0433-rev9.pdf`, sha256 `672a9433…`, read by page. Each condition cites revision, section and page.
> - **(8) CI** (`RUL-15`): a GitHub workflow that runs `tests/spike.sh` and the committed harnesses in the pinned tools image named by Aethelgard's `container/IMAGE`, pinned by digest here, with no network for the run.
>
> It is NOT:
> - the identity and ledger work (`TVI-WO-1200`): keep the model-version field as the branch has it, and name in FEEDBACK how it differs from `RUL-10`;
> - any peripheral other than SPI;
> - any change to Aethelgard.

## 1. Build

1. **Check (7) first.** Commit the truth table, with each row's page, in `docs/work-orders/TVI-WO-0100a.rm0433.md`. **Stop**, and report any row where the build's rule disagrees with the manual.
2. **Build (1)–(6) and (8).** Each refusal comes with a test, and each test with a mutant it kills.
3. **Merge `main` before READY.** The steering documents changed in PR #5.

## 2. Acceptance

- **Every verdict §5 item for X is closed,** or is explicitly ruled on the bus.
- **IOLOCK:** a planted firmware sets IOLOCK and then writes CFG2. It refuses with the rule's record.
- **The reference check:**
  - It exits non-zero on: a removed guard (no witness), a refusal, and a crashed simulator.
  - It exits 0 on the modeled control path.
  - It ignores a firmware UART line that imitates a record.
- **CI is green on the PR.**

## 3. Fences

- **Fence:** `model/`, `scripts/`, `tests/`, `firmware/`, `tools/` (the reference check), `.github/workflows/`, `README.md`'s status line, `FEEDBACK.md`, and `docs/work-orders/TVI-WO-0100a.*`.
- **Not:** the steering documents, `docs/reviews/`.
- One PR, left unmerged. Write `~/.cache/tvi-0100a/pr-title-body.md`.

## 5. Adversarial pass, full

- **A refusal or a missing witness that still yields a pass,** at the runner or at the reference check.
- **A firmware-written line accepted as a record.**
- **A route to a guard copy that disagrees with silicon:** IOLOCK, sub-word writes, reset, or a write the stock model drops.
- **A citation that does not say what is claimed.** Read the page.

## 7. Flag-and-stop conditions

- **After §1.1,** with the truth table and any disagreement.
- **RM0433 Rev 9 contradicts the rule** that the build passes on. Stop with the page.
- **The logger channel can be written by firmware** in this Renode build. Stop with the reproduction: it breaks `RUL-11`.
