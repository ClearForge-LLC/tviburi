# TVI-WO-0100a — FEEDBACK (builder → architect)

**Status:** built, and ready for review as PR #6, which supersedes PR #4. It is not merged; the builder never merges.
**Base:**
- the blind build's head `e47e8d6`, with `main` `963238f` merged in;
- §1.1's truth table at `38a3b88`;
- the build after the architect's rulings of 2026-11-23T08:45Z.

**Tooling:**
- every run is in `ghcr.io/clearforge-llc/aethelgard-tools@sha256:e5fc7ddb…8d1b58`, which is what Aethelgard `main` pins, read with `git show origin/main:container/IMAGE`;
- Renode `1.17.0+20260907gitf1dd1b4af.aet1605`;
- `--network none` for every run;
- on Furnace, one container at a time under `~/bin/capped-run` at `nice -n 10`;
- in CI, on `ubuntu-24.04` with plain `podman run`.

## 1. The verdict's §5 items for X, and how each closed

| # | Item | Closed by |
|---|---|---|
| 1 | **R8, IOLOCK** | `TVI-SPI-IOLOCK`: after a CR1 write that carries IOLOCK, every CFG2 write refuses until reset. IOLOCK locks the whole of SPI_CFG2 (RM0433 Rev 9 p2215, p2221), which is the WO's narrowing, cited. The planted firmware `iolock` (`CFG2=MASTER`, then `CR1.IOLOCK`, then `CFG2=MASTER\|SSM`) refuses with the rule's record. Rulings D3 and D6 added `TVI-SPI-CFG2-SPE` and `TVI-SPI-I2SMOD` beside it. |
| 2 | **R9, the records contract** (RUL-12) | Records travel on Renode's logger under the peripheral's own name (`spi1:`). The reference check is `tools/tviburi-check`, std-only Rust: it reads only the log file and exits 0 only on a witness, an end record and no refusal. The runner keeps exit 3 and takes that verdict from the check. The README names both as the supported entry points. Deviations from the WO's wording are in §2. |
| 3 | **Construction-time failures** | No exception and no `FailFast` remain. A model version that cannot be computed is held until the peripheral is named. It is then emitted as a `TVI-LOAD` refusal at `seq 0`, with the four fields (`model_version: "unavailable"`), in place of the witness. Every later write refuses with `TVI-LOAD`, and the run continues to a refused verdict. Test: `load`. |
| 4 | **Harnesses committed** | `tests/adversarial.sh` (the cases in `tests/spike.sh`'s adversarial group), and `tests/mutants.sh` with `tests/mutants.tsv`. CI runs both. Every result in this FEEDBACK was reproduced by them; nothing is labelled unverified. |
| 5 | **Stale quotes** | Regenerated from the final code, in §4 below and in the blind FEEDBACK's Q3 and Q4 (`FEEDBACK.md`). |
| 6 | **README status line** | Updated. It names the runner and the records contract. |
| 7 | **RM0433** | `TVI-WO-0100a.rm0433.md`: each row cites the page, and every quote was matched against the PDF's pages. Every condition text in the guard now cites revision, section and page, per branch. |
| 8 | **CI** (RUL-15) | `.github/workflows/ci.yml` runs the acceptance suite, the adversarial cases and the mutation run in the pinned image, by digest, with `--network none` for every run. Green on PR #6. |

## 2. Where the build departs from the WO's wording, and why (posted on the bus 2026-11-23)

- **The witness is emitted when the peripheral is named, not at construction.**
  - Measured: a log call from a model's constructor carries no source name.
  - The name exists only from the `PeripheralsChanged` `NameChanged` event, which logs as `spi1:`.
  - RUL-11 requires the peripheral's own name, so the witness waits for it.
- **There is a third record kind, `end`, emitted from `Dispose`.**
  - It is needed because the WO's acceptance says the check exits non-zero on a crashed simulator, and a witness alone cannot show a crash.
  - Measured: an unhandled model exception (exit 134) and SIGKILL leave no end record.
  - **Limit:** SIGTERM still disposes the machine and writes an end record. An end record therefore shows a clean teardown, not that the consumer's script finished.
    - The runner covers this with its own end-of-script marker (adversarial `adv-noend`).
    - A consumer needs the same.
  - The consumer's contract is therefore: a witness, then an end, with no refusal.
- **Every record carries `seq`.** Renode collapses identical consecutive messages into "msg (2)" (measured).
- **Refusals log at Error, and witness and end log at Info.**
  - `logLevel` cannot hide Error: `logLevel 4` is rejected (measured).
  - Hiding Info hides the witness or the end, which fails closed (adversarial `adv-loglevel`).
  - **Consequence for Aethelgard** (not changed here): its clean-load judgement fails on any error line, so a refusal will also read as a failed load. That is correct, but its messages should say which.

## 3. The model version and RUL-10 (kept as built, for TVI-WO-1200)

The model version is unchanged from the blind build:
- it is sha256 over the files in `$TVIBURI_MODEL_DIR`, plus the Renode and Infrastructure build strings;
- the runner sets that variable to `/repo/model`.

**How it differs from RUL-10:**
- **The source is named, not compiled.** It hashes the files an environment variable names, not the bytes Renode compiled (RUL-10: "never from a name or an environment variable").
- **It covers less than the identity must.** It covers `model/` only. It does not cover `scripts/prelude.resc`, the stock `.repl` chain inside the image, or the image digest, and RUL-10 makes the identity the overlay manifest's hash.
- **A caller can repoint it.** A consumer that sets `TVIBURI_MODEL_DIR` to another directory gets a different, and equally well-formed, version. The runner overrides the caller's value (adversarial `adv-env`), but a consumer running Renode directly is not protected.

## 4. Records, regenerated from the final code

PLACEHOLDER-RECORDS

## 5. Findings for the architect

PLACEHOLDER-FINDINGS

## 6. Gates

PLACEHOLDER-GATES

## 7. Scope notes

PLACEHOLDER-SCOPE
