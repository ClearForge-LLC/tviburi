# TVI-WO-0100b — SPI guard fixes: the NSS-output pass, IOLOCK and the D1 history corrected, refusal kinds, the check's expected version, and SPI's allow-list

**Repo:** `ClearForge-LLC/tviburi` · **Base:** `main`, named in the kickoff.
**Branch:** `wo/tvi-0100b-spi-guard-fixes`.
**Author:** Claude (co-architect) · **Builder:** strong tier, with a full adversarial pass.
**Phase:** 1. **Cadence:** one measurement, a stop, then the build.

**Grounds:**
- **Northstar** invariants 1, 2, 5, 8.
- **Architecture:** `RUL-07`, `RUL-08`, `RUL-12`, `RUL-16`, `RUL-17`, `RUL-18`, `RUL-19`; `RUL-10` and `RUL-11` as amended 2026-11-23.
- **Roadmap:** Phase 1, the `0100b` row; gate clauses 3, 4, 6 and 7.
- **The review** (`docs/reviews/2026-11-23-mythos-5-2-tviburi.md`) A6, A7, A8, A12, B1, and its response; `TVI-WO-0100a.FEEDBACK.md` F1, F2, F10; `TVI-WO-0100a.rm0433.md`.

> **What this is:** the SPI guard brought to the rulings the review produced.
> - **(1) A6, the NSS output.** `TVI-SPI-MODF` passes `SSM` = 0 with `SSOE` = 1: "When master uses hardware NSS output (SSM=0 and SSOE=1), the NSS signal input is forced to non active state internally to prevent master mode fault error" (RM0433 Rev 9 §50.11.4 p2221; p2166). Whether the unmodeled NSS output then refuses is `RUL-17`'s: as `unmodeled`, only if `SSOE` is outside SPI's allow-list. In recorded mode it is a `would-refuse`. **Measure:** does any pin on the platforms that run ArduPilot and the generated firmware carry `SPIx_NSS` by its alternate function? If none does, say so: whether `SSOE` = 1 with NSS unrouted counts as modeled is the architect's, at the stop.
> - **(2) A7.** Verified at `main`: a `CFG2` read returns the guard's copy, and a `CR1` read takes `SSI` from it (`0100a` F2). The remainder: every field the guard copies under (6) is served from its copy on read. `SR.MODF` while the D1 history is set (F2) stays as built; record it.
> - **(3) A12.** `IOLOCK` latches only from a `CR1` write silicon accepts: it "can be set when SPI is disabled only" (p2217). The D1 history is cleared by `IFCR.MODFC` (p2227) only if RM0433 says `MODFC` clears the fault; cite the page, or keep the history and say why. **Clearing it must not clear the doubt about `MASTER`:** a fault would have cleared `MASTER`, and `MODFC` does not restore it. So after `MODFC` the copy's `MASTER` is unknown, and a `CFG2` read or an enable refuses, until `CFG2` is written again. Add the test that builds the history from a `CR1` write, with the mutant that removes that `NoteMasterState()` call. Model `IOLOCK`'s hardware clear on `SPE` 1→0 (p2215) only if it removes a refusal ArduPilot or the generated firmware meets.
> - **(4) B1, a `kind` on every refusal** (`RUL-18`). Classify each rule branch, with its page. Expected: `MODF` with `SSI` = 0, and a `CFG2` write under `IOLOCK` or with `SPE` = 1, are `predicted-fault`; T3, T4a, TI mode, slave mode, the D1 history, `TVI-SPI-WIDTH` and `TVI-LOAD` are `unmodeled`. Each `predicted-fault` names its bench check in FEEDBACK, as F1 does, until `1200`'s ledger holds it.
> - **(5) A8, the check.** It takes the expected model version as an argument and fails any witness with another. It fails on any `{"tviburi":` record from a source it was not given. It fails a witness whose `mode` is not `enforcing` (`RUL-19`), and the guard's witness carries `mode: "enforcing"`. **The console framing waits for `TVI-WO-1150` (ii):** add it, with that fixture as a test, once its commit is posted; until then the check reads the `logFile` framing alone. The version compared is still `0100a`'s until `1200`; say so.
> - **(6) SPI's allow-list** (`RUL-17`). For every field of every SPI register (RM0433 Rev 9 §50.11, Table 429 p2233), classify it: modeled by the guard; implemented by the stock model (inherited, unaudited: invariant 2); a tag, or bits the model drops or ignores. Name the value the model behaves as. Keep the list as data keyed by RM0433's register and field names (`RUL-16`), so `1200`'s ledger can emit it. **Before building,** measure each field against the generated firmware's SPI writes (`spike/1100/data/writes.tsv`) and ArduPilot's: from `1150` (v) if it is committed, otherwise from an SPI-only access log taken with `spike/1100/harness/`. Say which.
>
> It is NOT:
> - the identity or the ledger file (`TVI-WO-1200`), or any peripheral other than SPI;
> - recorded mode in `model/` (`1150` (iv) prototypes it; the work order that builds it is named when `1150` reports);
> - any change to Aethelgard.

## 1. Build

1. **Measure (6), and (1)'s routing question, first.** Commit the field table, with each row's page and the values both firmwares write, in `docs/work-orders/TVI-WO-0100b.rm0433.md`, with the truth-table rows for (1), (3) and (4) at this branch's head. **Stop,** and list every field outside the allow-list that either firmware writes with a value the model does not implement. The architect acknowledges fields by name, or rules them refused.
2. **Build (1)–(6)** to the acknowledged table. Each refusal comes with a test, and each test with a mutant it kills.
3. **Merge `main` before READY.**

## 2. Acceptance

- **ArduPilot's SPI start** (`spi_lld_start`'s order, as measured), replayed as a planted firmware, passes with the acknowledged table. The generated firmware's SPI1 sequence passes.
- **Every field outside the allow-list:** a planted write of a value the model does not implement refuses with `kind: unmodeled` (gate clause 6).
- **The `MODF` scar still refuses,** with `kind: predicted-fault`, and its condition text matches RM0433 Rev 9 by page (gate clause 4).
- **The check exits non-zero** on a wrong expected version, a record from an unnamed source, and a recorded-mode or mode-less witness (gate clause 7); and, once `1150` (ii) lands, reads the console fixture.
- **CI is green on the PR.**

## 3. Fences

- **Fence:** `model/`, `tools/`, `tests/`, `firmware/` (planted firmware only), and `docs/work-orders/TVI-WO-0100b.*` (the field table and FEEDBACK).
- **Not:** `scripts/`, the steering documents, `docs/reviews/`, `spike/`, or Aethelgard.
- **Running beside you:** `TVI-WO-1150`. Your fences do not overlap.
- One PR, left unmerged. Write `~/.cache/tvi-0100b/pr-title-body.md`.

## 5. Adversarial pass, full

- **A field outside the allow-list that still passes:** a sub-word write, or a read-modify-write that carries a stale value from a field the stock model drops.
- **A read served with a value silicon would not give,** from a copy or from the stock model.
- **A record that yields a pass:** from an unnamed source, from a recorded-mode witness, with no `mode`, or with no version argument.
- **A Renode log line under a guarded peripheral's name whose text the firmware controls** (the review's residue, "On forging a record"). Search the stock SPI model's log calls.

## 7. Flag-and-stop conditions

- **After §1.1,** with the field table and the fields to acknowledge.
- **ArduPilot's SPI start still refuses** under the acknowledged table. Stop with the field and the page.
- **RM0433 Rev 9 contradicts a `kind`,** or a rule this build passes on. Stop with the page.
