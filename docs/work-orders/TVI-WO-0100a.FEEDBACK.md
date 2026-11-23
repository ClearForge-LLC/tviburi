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

Taken from the final code (`f2bec3d`), run `modf` of the last suite run. The model version is `sha256:1257143b…73330`. Each record is one line in Renode's log file; they are shown unwrapped.

**The MODF scar** (`firmware/spi1.rs` `modf`): the witness, the refusal, and the end record.

```
[09:28:27.3810]  [INFO] spi1: {"tviburi":"witness","seq":0,"guard":"GuardedSTM32H7_SPI","model_version":"sha256:1257143b90b1eb8829d02f6786a38040c6690a048176fb91a5e409ea2ac73330"}
[09:28:27.8130]  [ERROR] spi1: {"tviburi":"refusal","seq":1,"rule":"TVI-SPI-MODF","peripheral":"sysbus.spi1","register":"SPI_CR1 / SPI_CFG2","bit":"CR1.SSI (bit 12) = 0","condition":"master mode with software SS management and the internal SS input at its active level (SSM=1, SSI=0, SSIOP=0): silicon sets SR.MODF (bit 9), forces CR1.SPE to 0 and clears CFG2.MASTER; the stock model implements none of these","model_version":"sha256:1257143b90b1eb8829d02f6786a38040c6690a048176fb91a5e409ea2ac73330","citation":"RM0433 Rev 9 §50.5.2 p2184; §50.4.7 p2165, Figure 618 p2167; §50.11.4 p2221 (SSM, SSIOP); §50.11.6 p2225 (SR.MODF)","state":"CR1 last passed=0x00000000, CFG2 last passed=0x04400000, IOLOCK latched=no, master history=CR1=0x00000000, CFG2=0x04400000","access":{"offset":"0x0","value":"0x1","width":32,"pc":"0x0800002A","virtual_time":"00:00:00.000000000"}}
[09:28:27.8574]  [INFO] spi1: {"tviburi":"end","seq":2,"guard":"GuardedSTM32H7_SPI","model_version":"sha256:1257143b90b1eb8829d02f6786a38040c6690a048176fb91a5e409ea2ac73330"}
```

**The refused access is still in the trace** (`sysbus LogPeripheralAccess spi1`), at the refusal's PC:

```
[09:28:27.8126]  [INFO] spi1: [cpu: 0x800002A] WriteUInt32 to 0x0 (Control1), value 0x1
```

**The check's output and the runner's verdict for that run:** `REFUSAL {…}`, then `CHECK REFUSED`, then `VERDICT REFUSED (renode exit 0)`, and the runner exits 3. The modeled path gives `CHECK PASS` and `VERDICT PASS`, and exits 0. Its log holds only `witness` at seq 0 and `end` at seq 1.

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

All runs below are on the final code: the reduction commit `f2bec3d`; the commits after it change only documents. They ran on Furnace, one container at a time, under `capped-run`, with `--network none`.

**Acceptance (WO §2).** `tests/spike.sh`: 22 checks, all pass.
- **IOLOCK:** the planted `iolock` firmware sets IOLOCK, then writes CFG2. It refuses with the `TVI-SPI-IOLOCK` record (`iolock`).
- **The reference check exits non-zero on each failure route:**
  - a removed guard, which leaves no witness: `stock-modf`, `stock-forge`, and four ways in `adv-removed`;
  - a refusal: `modf`, `cfg2first`, `iolock`, `rmw`, `conditions`;
  - a crashed simulator: `crash`, which SIGKILLs Renode after its witness. Exit 137 leaves no end record, so `CHECK FAIL`.
- **It exits 0 on the modeled path** (`modeled`).
- **It ignores a firmware UART line that imitates a record:**
  - `forge`: a forged refusal is ignored, and the run passes;
  - `stock-forge`: a forged witness and end are ignored, and the run fails with no witness;
  - `adv-spray`: record bytes written to every unguarded SPI1 register are not taken as records.
- **The other acceptance tests:**
  - **every rule branch, in order** (`conditions`), including the read cases and the RCC reset;
  - **no model version:** a `TVI-LOAD` record at seq 0, no witness, every access refused (`load`);
  - **the record is byte-identical across two runs** (`stable`);
  - **the firmware's end marker is required** (`nomark`);
  - **the check's unit tests** (`unit`).
- **CI is green on the PR** (§1, item 8).

**Adversarial pass (WO §5), full.**
- **The committed cases:** `tests/adversarial.sh`, 14 checks, all pass:
  - guard removal, four ways;
  - an extra argument, and a variant that names no path;
  - the caller's `TVIBURI_MODEL_DIR`;
  - a value-changing monitor write hook, which is installed and does not displace the guard;
  - `logLevel 3` after and before load;
  - record bytes sprayed into SPI1;
  - a script that stops before its end.
- **A fresh reviewer** (read-only, given the code and not my conclusions) then attacked §5's four routes. Its findings:
  - **One real false pass: read-modify-write (F2).** Fixed and tested.
  - **One claim that contradicted my measurement: RCC reset.** I re-measured, and the reviewer was right (F3); it is now tested.
  - **Five test weaknesses, all fixed:**
    - `adv-env` could not fail; it now asserts the rule.
    - `adv-hook` was a no-op; the hook now changes the value, and the record shows it.
    - Mutant M20 died for the wrong reason; it now deletes the witness.
    - The forge tests never reached the source filter (F8); a unit test now kills that mutant.
    - The harness counted any exit 1 as a kill; it now needs a `FAIL` line from a named test.
  - **One false refusal: `SET_BIT` on CR1,** fixed by F2.
  - **Two citations, corrected:** I2SMOD is defined on p2232, and TI mode's "not required".
  - **One plausible route, closed:** an unaligned 32-bit access. It refuses, and is tested.
- **Every result in this FEEDBACK is reproduced by the committed harnesses.** None is labelled unverified.

**Mutation (diff-sized): 45 mutants, one per guard this WO adds or changes. All 45 are killed,** each by a `FAIL` line of the test its row names (`tests/mutants.tsv`).
- **The first run was invalid.** Bash's `read` collapses empty tab-separated fields, so rows with empty replacement text took the test name as the replacement. M11 was "killed" by a compile break.
- **The fix:** an explicit `(empty)` token, and a check that every row has five fields.
- **The second run:** one survivor, R02, the check's source filter. The new unit test kills it.
- **Not mutated:**
  - `belt-and-suspenders` code: there is none;
  - the `named ||` guard against a second `NameChanged`: Renode sends it once (measured), so a mutant is equivalent;
  - the runner's empty-`/out` check and `rc -eq 0`: no test can make Renode exit non-zero after a passing check without a crash, which `crash` covers at the check.

**Reduction pass (diff): −50 net · 5 duplicates removed · 0 dead paths removed · 0 protected kept · 2 proposals rejected.**
- **How it was run:** a fresh subagent proposed nine cuts and I took seven. One full suite run (36 checks) and one mutant run (45/45) proved the batch.
- **The duplicates removed:**
  - the six width and load preambles, now one helper;
  - the guarded-register list;
  - the runner's variant list;
  - the `TVI-LOAD` citation;
  - the monitor-output filter.
- **Also removed:** `State()`'s restatement of the written value, and the check's three verdict arms, now one.
- **One reduction was a regression, caught before commit.** The variant list's substring match accepted `"modf cfg2first"`, which built firmware with no guarded write that would then pass. An exact match replaced it, with a test (`adv-bad-variant`) and a mutant (S06).
- **Rejected proposals:**
  - deriving the test lists from function names, because it would change the run order and so the order failures show in CI's log;
  - two one-line helpers in `spike.sh`, each of which adds a definition to save a line.

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
