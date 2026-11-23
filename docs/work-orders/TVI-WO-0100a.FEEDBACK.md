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

Each item comes with a recommendation.

**F1. Bench check on the Nucleo (ruling D1).**
- **The question:** is MODF evaluated while SPE=0? Only silicon settles it.
- **The procedure:** on NUCLEO-H753ZI SPI1, write `CFG2 = MASTER|SSM` with `CR1.SSI = 0` and SPE=0. Then read SR.MODF and CFG2.MASTER.
- **What each result means:**
  - **MODF=1, or MASTER=0:** the history branch is right, and its condition text can cite the bench.
  - **MODF=0 and MASTER=1, repeated across resets:** the history branch is a false refusal, and the passing order could be relaxed.
- **What it costs today:** firmware that writes CFG2 before SSI, which p2171's own procedure does, refuses.

**F2. Reads are now partly modeled (adversarial pass, posted on the bus).**
- **The false pass it closes:** the guard copied writes but not reads. The stock model reads `CFG2.MASTER` as 1 and the dropped tag bits as 0. So read-modify-write firmware sent the twin a different word from the one it would send silicon, and the guard judged the twin's word. Repro: the `rmw` firmware, `SSIOP=1` then `CFG2 |= MASTER|SSM`. Before the fix it passed; silicon is in row T3.
- **The fix:**
  - A CFG2 read returns the guard's copy, which is silicon's value. Every field is rw with reset 0 (p2221–2222), and every write silicon would drop refuses.
  - A CR1 read takes SSI from the copy (p2215).
  - A CR1 read while IOLOCK is latched refuses.
- **A side effect:** HAL's `SET_BIT(CR1, SPE)` was a false refusal, and is now a pass. The modeled firmware enables that way.
- **What is still unmodeled:** reads of other CR1 bits come from the stock model as they always did. Those are CSTART, CSUSP, the CRC bits and HDDIR, which the guard does not track.

**F3. RCC `SPI1RST`: not a false-pass route. This corrects my earlier bus note.**
- **What was posted:** at 08:52Z I posted that RCC_APB2RSTR.SPI1RST (p442) does not reset `spi1`. The architect's 09:23Z note took it as a real route, and my correction went up at 09:23Z as well.
- **Why it was wrong:** my probe's grep cut the line off with `head`.
- **What was measured:** with RCC access logging, the `APB2Reset` write calls `spi1.Reset()`, and the guard's `Reset()` clears its copies with it.
- **Now pinned:** `conditions` case 15 enables, resets SPI1 through RCC, then expects SPE to refuse as slave.
- **For TVI-WO-1100's coverage queue:** **no row is needed.**

**F4. The model version against RUL-10.** See §3. For TVI-WO-1200.

**F5. CI's image pin must follow Aethelgard `main`'s `container/IMAGE` (RUL-09).**
- **Today:** `tests/spike.sh` pins `e5fc7ddb…`, the build architecture §2.0 cites, and the CI workflow reads it from there.
- **Coming:** Aethelgard's PR #355 re-pins `main` to `1e0ab105…`, which has the same Renode build string, `aet1605`. When that lands, the `IMAGE=` line in `tests/spike.sh` changes, alone, by PR.
- **Never by tag.** The workflow pulls by digest only.

**F6. A SIGTERM'd Renode looks clean to the check (measured).**
- **What the check sees:** SIGTERM runs `Dispose`, so a run killed by `timeout` has a witness and an end. If it saw no refusal before the kill, the check passes it.
- **Who is covered:** the runner's end-of-script marker catches this (`adv-noend`).
- **For the architect, under RUL-12:** a consumer needs its own completion evidence. Is that enough, or should the end record carry something that a SIGTERM path cannot produce? For example, a monitor command that only the consumer's script runs at its end.

**F7. The console interleaves log entries mid-line (measured).**
- **What happens:** Renode prints log entries from a separate thread, so a refusal line split the echo `TVIBURI-END` into `TVIB[…log…]\nURI-END`.
- **The fix:** `scripts/monitor-output` removes log entries before the marker is searched for.
- **Who is exposed:** Aethelgard parses console sentinel lines the same way (architecture §2.0), so it is exposed to the same split. That is not changed here.

**F8. RUL-11 testing gap (adversarial pass).**
- **The limit:** the UART analyzer prefixes every line with `[host: …]`, so a firmware UART line never starts with `{`. The forge tests therefore never reached the check's source filter.
- **What covers it now:** a unit test, in which a well-formed record under another peripheral's name does not count. It kills the source-filter mutant.
- **What has no end-to-end case:** a second guarded peripheral. None exists yet.

**F9. Recorded, not acted on (ruling):**
- the manual's two inconsistencies: p2171 puts IOLOCK in "SPI_CFG1"; p2184 says "pulled low" where p2165 says "equal to SSIOP";
- the `SSM=0, SSOE=1` pass (R7).

**F10. Not built: the rest of RUL-12's "different hash" clause.**
- The check enforces "a witness, an end, no refusal".
- It does not compare the witness's `model_version` with a pinned value, because the version's definition is TVI-WO-1200's.
- That is one flag to add once RUL-10's manifest hash exists.

## 6. Gates

PLACEHOLDER-GATES

## 7. Scope notes

- **Fences:** only `model/`, `scripts/`, `tests/`, `firmware/`, `tools/`, `.github/workflows/`, README's status line, `FEEDBACK.md` (regenerated quotes) and `docs/work-orders/TVI-WO-0100a.*` changed. Nothing in the steering documents, `docs/reviews/` or Aethelgard changed.
- **Beyond the literal list, each tied to the WO's adversarial §5 or acceptance:**
  - **The `end` record:** the crashed-simulator acceptance needs it.
  - **The read handling (F2):** it closes a guard-copy-versus-silicon route, which §5 names.
  - **`TVI-SPI-CFG2-SPE` and `TVI-SPI-I2SMOD`:** rulings D3 and D6.
  - **The `spray` and `forge` firmware:** the RUL-11 tests.
  - **The `rmw` firmware:** F2's test.
- **How to run:**
  - On Furnace: `tests/spike.sh`, `tests/adversarial.sh` and `tests/mutants.sh`. Each takes an optional source directory and defaults to `git archive HEAD`, and needs `~/bin/capped-run` and the image already pulled.
  - In CI, `TVIBURI_CI=1` uses plain `podman run`. Only a disposable runner may set it.
- **Concurrency:** one container of mine at a time throughout. Another builder's container (`/harness/capture`) was seen running once and was left alone.
