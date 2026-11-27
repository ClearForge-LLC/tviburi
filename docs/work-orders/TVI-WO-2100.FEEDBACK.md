# TVI-WO-2100 — FEEDBACK

**Builder:** Claude (Opus 5.5), project manager, Furnace, 2026-11-26. Opus workers built the release-program change and ran the firewalled rehearsal. **Branch:** `wo/tvi-2100-first-release`, from `main` `cd235c7`. **Rulings:** Q23–Q25 (18:38Z), Q26 (19:45Z), the stop at 18:20Z. **The measurement:** `TVI-WO-2100.measure.md`.

**Contents:** §1 what was built · §2 acceptance · §3 the rehearsal · §4 adversarial pass · §5 rows and findings · §6 gates · §7 CI and the release

## 1. What was built

| Item | Built |
|---|---|
| **(3)** the pins | Read-only against Aethelgard `main` `20971352`'s image (`1e0ab105…`). Every pin matches. `Renode.dll` reports `1.17.0+20260907gitf1dd1b4af.aet1605` (= `platform.rs` `RENODE_BUILD`, = `smoke.sh`). `Infrastructure.dll` reports `1.0.0+cd4b002a…`. The three stock files match by sha256. **Finding:** the rebuilt `Infrastructure.dll` (AET-WO-1605, three NVIC commits) reports the unpatched string, because its build sets `SourceRevisionId` to the same commit; only its sha256 (`ddb19d7b…`) names the rebuild |
| **(1)** the release job | `ci/release.sh` and `.github/workflows/release.yml` (`contents: read`, no tag or Release). Two builds from two `git archive` extractions (umask 022 and 077, mtimes 2000 and 2030) compared byte for byte, the whole directory with every differing file named; `verify` with the verifier built from the released commit's own tree; the tarball, `MANIFEST`, `LEDGER.jsonl` and a summary kept as the artifact. Refused: a commit not on `main`, a dirty checkout. Triggers: dispatch, and pull requests touching the release's files (Q24 (a)), which build the PR's base commit |
| **Q23 (B)** property rows | One `property <guard> <name> required\|optional <meaning>` row per public constructor parameter of every guard (39), copied into the MANIFEST. The release refuses each of these by name: a parameter with no row (R1), a row with no parameter (R2), a class-map guard missing a row (R3), and an `optional` row on a parameter with no C# default. Every required row cites the case that shows it. GPIO's port-A/B reset values had none: `spike/2100/gpio-reset.sh` (`ospeedrreset`, `pupdrreset`) |
| **Q25** assembly sha256s | `build <asm> <string> <sha256>` in `pins.tsv` and the MANIFEST (format 2). The release build refuses an image whose `Renode.dll` or `Infrastructure.dll` differs |
| **Q26** the ledger's PWR | A canned stub restated onto its guard is a `model` row with `guarded_by`, not the stub's `canned` row: `pwr` is guarded by `Tviburi.GuardedSTM32H7_PWR`, and `fmc` stays canned. Planted with a unit test and mutant `Q26-stubCanned`. **`tests/1308/pwr.sh`'s `pwr-ledger` is updated:** it had asserted the old canned row |
| **(2)** the rehearsal | §3 |
| **(4)** the notes | `docs/releases/v0.1.0.md`, a draft, filled from the dispatch run on the release commit (§7) |

## 2. Acceptance

| Clause | Evidence |
|---|---|
| The release job is green on a `main` commit: two builds byte-identical, `verify` passes, both hashes in the summary | On Furnace, `ci/release.sh all cd235c7` twice: byte-identical, `VERIFY PASS`. In CI, the PR-triggered run on #69 (base `cd235c7`): green. On the release commit: the dispatch (§7) |
| It fails as planted: off-main, one model byte, a wrong hash | `spike/2100/planted.sh`, all failing by name, plus a dirty checkout: P1 "… is not on origin/main"; P2 "the checkout is not clean"; P3 "the two builds differ: LEDGER.jsonl (…); MANIFEST (…); Tviburi.cs (differ: byte 901727, …); tviburi-release.tar (…)"; P4 "verify against sha256:0000…" |
| The rehearsal is green from the tarball alone, or it stops with what the consumer lacked | It stopped first (18:20Z: the guards' non-stock properties), was ruled (Q23–Q25), then went green (§3) |
| The notes cite every item of `1205.consumer.md`, carry the job's hashes, and quote the memory figure with its run id | §7 |

## 3. The rehearsal

**The worker and its limits.** A fresh Opus worker played the consumer behind a firewall. It could read:
- the tarball;
- `RUL-07/09/10/11/12/19`;
- the image's stock files;
- Aethelgard's two captures and the board's facts.

It could not read `model/`, `tests/`, `scripts/` or the replay harness.

**What it wrote:**
- `spike/2100/consumer-platform.py`: the class map applied by `RUL-09` (b), and every `property` row supplied;
- `consumer-argv.py` and `consumer-boot.sh`.

`spike/2100/consumer-verify.sh` is the binding check with no Tviburi tool (POSIX `sh`, `sha256sum`). It fails by name on a wrong pin and on one edited byte.

**Result** on `c7ef230`'s release `sha256:18ef6b10…`. ArduPilot's fixture and the armed run both give CHECK PASS, enforcing: 34 witnesses, 34 ends, 0 refusals. The armed run arms at 9.332 s.

**Control:** without the battery `levels`, the armed run refuses `TVI-ADC-LEVEL`, yet ArduPilot still arms. The records are the gate, not arming (`RUL-08`).

**Its ten findings:**
- Four corrected property rows:
  - the GPIO reset values per port (no stock entry declares them);
  - `optionBytes` is the consumer's init word;
  - `frequency` is the consumer's, if it updates it;
  - `levels` names its instances.
- One MANIFEST line: a stock property with no row is dropped.
- Six consumer facts, sent to `1205`'s builder at 19:46Z: revision, HSE, levels, externals, package and the receive-DMA wiring.
- One row for the architect: `RUL-09` (b), §5.
- The ledger's PWR row (Q26).

## 4. Adversarial pass (WO §5)

An independent Opus reviewer worked on `afd88a4` from the code, the WO's three attack classes and the rulings, in its own copy.

| Class | Finding | Fixed |
|---|---|---|
| Q23's check | **A parameter passed with no row:** a comment holding `'` opened a "literal" in the C# reader that swallowed the next parameter (`string extra = null`), and the build succeeded with no row for it | `e9368c0`: the reader masks comments and every literal form (regular, verbatim, interpolated, char), keeping line numbers. Planted, with defaults holding commas, parentheses, generics and `"a,b"` |
| Q23's check | **R1 missed real peripheral classes:** `: BasicDoubleWordPeripheral`, a generic class, an attribute on the same line, `unsafe` | `e9368c0`: declarations are read with any attributes, modifiers, type parameters and `where`. A peripheral is a class-map guard, or a class whose base list names a `*Peripheral` type, a stock type or another overlay peripheral, at any depth. Each form is planted |
| mutant coverage | **Five new checks survived their deletion** (the row form, a class-map guard with no declaration, the constructor line filter, the row width, declared twice) | Each has a planted case and a mutant (AP01–AP14); the line filter was removed as unneeded on masked text |
| workflow | **The pull-request run never ran the PR's own tools,** since it builds the base. Its path filter also missed files the job reads | `1aaa449`: on a pull request the job also runs `ci/release.sh dry` on the PR's tree (built twice, compared, verified), `planted.sh`, `consumer-verify-test.sh`, and the binding check in the image against `/opt/renode`. The filter adds `spike/2100/**`, `tests/ci/common.sh`, `tests/spike.sh` and `tools/tviburi-check/**` |
| a pin the consumer cannot check | **Nothing told the consumer to check the assemblies' sha256s,** and the rehearsal did not | The MANIFEST's `builds` line says so. `consumer-verify.sh` checks both assemblies given a Renode root, and the PR run does it in the image |
| the rehearsal's verify | **`consumer-verify.sh` accepted a fifth, hidden member and a duplicate `Tviburi.cs`** (where `tviburi-release verify` refuses), and counted the placeholder by lines | It requires exactly the four members once each, and counts occurrences. `spike/2100/consumer-verify-test.sh` plants all eight cases |

**What held** (ran):
- **A third build** (a different tree path and output path, `TZ=Pacific/Kiritimati`, `LANG`/`LC_ALL=tr_TR`, umask 077 in the container, `SOURCE_DATE_EPOCH=1`) is byte-identical in all five files.
- **The rehearsal's witnesses** all name the hash of their tarball's `MANIFEST`. Each boot compiled the overlay fresh (no cache), and only `Tviburi.cs` came from the release.
- **`consumer-verify` refuses** an edited ledger byte, an edited overlay byte, a wrong pin and a symlink.
- **The property rows match the guards' code.**
- **The workflow's dispatch input** reaches only `env` and `with:`; it has `contents: read`, and no step can be skipped.
- **A CRLF checkout** fails the build loudly.

**The rehearsal carries to the final release by identity:** its `overlay` line (the models' bytes) is unchanged since `c7ef230`; only the release program's sources, the ledger and the MANIFEST lines moved.

## 5. Rows and findings

- **For the architect's docs PR (ruled 19:45Z), `RUL-09` (b)'s wording.** Proposed text: "*The consumer also repoints every reference to a cancelled variable from a live entry: another peripheral's registration on a cancelled `rcc`, a connection whose destination is a cancelled instance (the timers' alternate-function inputs, `spi4`'s `DMARecieve`, `crypto`'s `OutDMA`), and a property naming one (`dmaPeripheral`). Each binds to the cancelled entry with no error, as an untyped update does.*" (The rehearsal's finding 6.)
- **For Aethelgard:**
  - its pin of `Infrastructure.dll` by sha256 is the only thing that names the rebuild. The manifest now carries that sha256 too.
  - The consumer facts in `1205.consumer.md` (§3).
- **The release ships no records reader:** `tviburi-check` and `tviburi-release verify` are built from `tools/`. The binding check needs neither (`consumer-verify.sh`); the consumer reads records with its own reader.
- **A `workflow_dispatch` job runs only from the default branch,** so this PR proves the workflow through its `pull_request` trigger on the base commit (Q24). The release itself is the architect's dispatch after the merge.

## 6. Gates

- **Reduction pass (diff): −8 net · 5 duplicates removed · 1 dead paths removed · 2 protected kept · 9 findings below** (`d9adfbd`, a fresh Opus subagent).
  - **Removed:**
    - `hex_pin` now calls `hex64`;
    - `props::bare` is shared;
    - the single-caller `assembly()` is inlined;
    - `ci/release.sh`'s `jobs()` is folded into the inline `CARGO_BUILD_JOBS` form;
    - the unset `RELEASE_PIN` option and a one-use local are dropped;
    - `consumer-verify*.sh` state the four members once.
  - **Kept, as deliberate:**
    - `consumer-verify.sh` is a second, tool-free implementation of `verify`'s byte checks;
    - the rehearsal's consumer side is firewalled from the tree.
  - **Findings, outside this fence:**
    - `tests/spike.sh`'s `reproducible` copies `twice`'s umasks and mtimes, and its file list omits the template;
    - `t_identity` creates `id-records` without the `[ -d ] ||` guard the other cases use, so it fails if `verify` runs first;
    - the `build` line is parsed in three places;
    - `release.yml` derives the image twice;
    - `consumer-boot.sh` holds a firewalled copy of the image digest.
- **Proofs on the head** (Furnace, cargo at six jobs, one container at a time):
  - the release program's unit tests (31);
  - `tests/spike.sh` `unit`, `manifest`, `verify`, `reproducible`, `identity`, `adv-release` and the ten `*-ledger` cases;
  - this WO's mutants RP, RA, AP01–AP14 and Q26 (22), all killed;
  - `ci/release.sh dry` (byte-identical, `VERIFY PASS`);
  - `consumer-verify-test.sh` (8 plants, plus the in-image pass against `/opt/renode`);
  - `planted.sh` (4 plants);
  - the route inventory (0 failures).

## 7. CI and the release

**The stop and the resume.**
- **Stopped** at 2026-11-26T22:53Z, under WO §7's fourth condition: Scotty ruled `1205`'s clause 1 (b), so (1)–(3) were done and (4) stayed a draft.
- **Resumed** at 2026-11-27T03:46Z: `TVI-WO-1206` merged (#72, `e9b569f`) and Phase 1's gate is met.

**The merges of `main`.**
- **`8e15435` (1205, #68):** `tests/mutants.tsv` conflicted on the two appends. Kept both. My first merge commit carried the conflict markers; I amended it before any push, and all 670 rows checked.
- **`e9b569f` (1206, #72):** `tools/tviburi-release/src/main.rs` conflicted at one seam. Kept both sides: this WO's `restated_stub` (Q26), and `1206`'s `HOMES` and `check_acknowledged`. All 671 mutant rows hold: ASCII, 5 fields, `old` once.

**The release, built locally on the merged tree (`6170cd9`), as ruled 03:46Z.**
- **Build:** `ci/release.sh dry` gives two builds byte-identical and `VERIFY PASS`.
- **Hashes:**
  - release `sha256:df5f52583a4df39c9ddda5e663355ea20834549154a3d0a362f72e6de01659c7`;
  - ledger `sha256:53265b30…7203`;
  - `ledger-rows` `413ad37b…4e90`;
  - tarball `a7999392…1fd8`.
- **The hash moved with `1206`:** its ledger rows, and its three acknowledgement homes, which are now `source` lines.
- **What the notes change:** only files that are not sources, so the head's release hash is this one. The PR-mode job's `dry` build on the head shows it.
- **Checks on that build:**
  - `consumer-verify-test.sh` (8 plants);
  - `consumer-verify.sh` in the image against `/opt/renode`;
  - `planted.sh` (P1–P4);
  - `tests/spike.sh` `unit identity verify reproducible manifest`, the eleven `*-ledger` cases and `adv-release`.

  All green.
- **The record** is the release job's dispatch on #69's merge commit, by the architect. Its hashes must equal the notes'. If any differs, the notes are wrong and a follow-up fixes them before the tag.

**Memory:** `1201`'s budget on `main` `e9b569f` (`ci` 45936018679): medians 743,276 KiB on main and 745,444 KiB on the branch (+2.1 MiB); the branch's peaks were 735,972–760,704 KiB, under 1,572,864 KiB. This WO changes no model.

**CI on the pushed head:** recorded on the bus at READY; this file is not pushed again for it.
