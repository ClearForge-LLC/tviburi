# TVI-WO-2100 — measurement and evidence

**Builder:** Claude (Opus 5.5), Furnace, 2026-11-26. **Branch:** `wo/tvi-2100-first-release`, from `main` `cd235c7`.

## 3. The pins against Aethelgard's tools image (done first, WO §1.1)

Image: `ghcr.io/clearforge-llc/aethelgard-tools@sha256:1e0ab105ec42…` — the digest in Aethelgard `main` `20971352` `container/IMAGE`, and the same as Tviburi `tests/spike.sh`'s `IMAGE`. Read with `~/bin/capped-run --network none`.

| Pin (`tools/tviburi-release/pins.tsv`) | In the image | Aethelgard `main` `20971352` | |
|---|---|---|---|
| `build renode 1.17.0+20260907gitf1dd1b4af.aet1605` | `/opt/renode/Renode.dll` informational version `1.17.0+20260907gitf1dd1b4af.aet1605`; `renode --version`: `build: 1.17.0+20260907gitf1dd1b4af.aet1605`; sha256 `5734eb0c…f633` | `crates/aethelgard-twin/src/platform.rs:33` `RENODE_BUILD`; `container/smoke.sh:50`; `container/Dockerfile:121` `5734eb0c…f633` | match |
| `build infrastructure 1.0.0+cd4b002aac2398994c4a61a34a8b31b5700facf2` | `/opt/renode/Infrastructure.dll` (the rebuilt one, AET-WO-1605) informational version `1.0.0+cd4b002aac2398994c4a61a34a8b31b5700facf2`; sha256 `ddb19d7b…24b5` | `container/Dockerfile:28` source `cd4b002a…`, `:115` built with `SourceRevisionId=cd4b002a…`, `:120` sha256 `ddb19d7b…24b5` | match |
| `stock 13c5571e… platforms/cpus/stm32h753.repl` | `13c5571e…d10ce` | `platform.rs:40` | match |
| `stock 86b2c2c5… platforms/cpus/stm32h743.repl` | `86b2c2c5…231c` | `platform.rs:44` | match |
| `stock b85cb4af… platforms/cpus/stm32h7.repl` | `b85cb4af…251e` | `platform.rs:48` | match |

**Finding (not a mismatch):** the rebuilt `Infrastructure.dll`'s build string is `1.0.0+cd4b002a…`, the same as an unpatched build of that commit, because the rebuild sets `SourceRevisionId` to the same commit (Dockerfile:115). So the string the MANIFEST names cannot distinguish AET-WO-1605's patched assembly from Antmicro's; only its sha256 (`ddb19d7b…`, pinned by Aethelgard's Dockerfile) does. `Renode.dll`'s string does carry the patch (`.aet1605`). A row: the MANIFEST could name each assembly's sha256 beside its build string.

## 1. The release job

- **`ci/release.sh`** carries the job's steps; `.github/workflows/release.yml` runs `ci/release.sh all <commit> origin/main` on `workflow_dispatch`, with `contents: read` only, and keeps the tarball, `MANIFEST`, `LEDGER.jsonl` and `summary.md` as its artifact. Nothing in it can tag or publish.
- **A `workflow_dispatch` job runs only once its file is on the default branch,** so CI cannot run it on this PR. Its script was run on Furnace instead, the same steps under `~/bin/capped-run` in the pinned image, on `main` `cd235c7`:
  - two builds, from two `git archive` extractions (umask 022 and 077, mtimes 2000 and 2030), **byte-identical** in all five files;
  - **`VERIFY PASS`** against the hash the first printed;
  - release `sha256:24c81a9c…3c9443`, ledger `sha256:f89130b6…f604d`, ledger rows `026feeca…0047`, tarball `sha256:f314ff2a…ab53`, Renode `1.17.0+20260907gitf1dd1b4af.aet1605`.
- **Planted** (`spike/2100/planted.sh`, in a throwaway clone), every plant failing by name:
  - P1, a commit not on main: "… is not on origin/main";
  - P2, a dirty checkout: "the checkout is not clean: ?? planted.txt";
  - P3, one byte of a model changed after the first build: "the two builds differ: tviburi-release.tar, differ: byte 4959, line 64";
  - P4, verify against the wrong hash: "verify against sha256:0000…".

## 2. The rehearsal: the consumer with only the tarball — **stopped (WO §7, the second condition): a fact the manifest does not state**

**What the tarball alone gives, measured.**
- It holds four files: `MANIFEST`, `Tviburi.cs`, `TviburiRelease.cs.template` and `LEDGER.jsonl`.
- **The binding check needs no Tviburi tool.** `spike/2100/consumer-verify.sh` checks the released bytes against the MANIFEST's own lines in a fresh directory that holds only the tarball, with POSIX `sh`, `tar`, `sed` and `sha256sum`:
  - the MANIFEST against the pin;
  - the template's sha256;
  - the overlay's first 912,038 bytes;
  - `Tviburi.cs`'s tail, which equals the template with its one placeholder (`@@MANIFEST_SHA256@@`, readable in the shipped template) filled with the MANIFEST's sha256;
  - the ledger's first line, and its rows' sha256.

  Result: `CONSUMER VERIFY PASS` on `main` `cd235c7`'s tarball. It fails by name on a wrong pin and on one edited overlay byte.
- **`tviburi-release verify` and `tviburi-check` are built from `tools/`,** so a consumer with only the tarball has neither. The check above replaces `verify`. `tviburi-check` is Tviburi's reference reader of the records contract (`RUL-11`/`RUL-12`), and the consumer reads records with its own (Aethelgard's console parser). Not a lack, but named: the release ships no reader.

**What `RUL-09` (b) leaves to the consumer, and what it cannot derive.** The ruling makes cancel-and-restate the consumer's job, from the pinned stock file and the class map. Its amendment replaced a shipped `.repl` fragment with the class map on purpose, so the restate texts not being shipped is not by itself a lack. Measured over the eleven committed restate files against the stock entries they restate:
- **Derivable by (b) from the stock chain and the class map** (the bulk):
  - `X: @none`;
  - the guard at the stock address `as "X"`;
  - the stock entry's properties and interrupt lines;
  - the registration with its alias (stated by `RUL-07` for the RCC: every `rcc RegistrationPeripheralAlias` registration in the chain onto the RCC guard, the 58 restated blocks);
  - **every reference to a cancelled variable repointed** (`dmaPeripheral: dmamux1`, DMAMUX1's routes, `spi4`/`crypto`, `@ gpioPortX`, `timerN@`, `@ i2cN`), found by grepping the pinned chain for the cancelled names.

  A consumer can reproduce all of this, though only if (b) is read as "repoint every reference", which is the trap it already warns of.
- **Not derivable: properties that are no stock entry's, which the guards need.** None is in the manifest, the class map or the ledger's `model` rows:

| Guard | Property | Without it | Committed case |
|---|---|---|---|
| `GuardedSTM32H7_PWR` | `revision` ("V"/"Y"), required, never defaulted (`1308` Q7) | `TVI-PWR-CONFIG` at every access | `pwr-norev` |
| `GuardedSTM32H7_ADC` | `revision`, required (`1307` Q2) | `TVI-ADC-CONFIG` | `adc-norevision` |
| `GuardedSTM32DMA` | `dmamux: <the DMAMUX1 guard's variable>` | "no `dmamux`": `TVI-DMA-CONFIG` | `dma-config` |
| `GuardedSTM32_GPIOPort` | `package` (e.g. "LQFP144"), and `external` levels | refused by name | `gpio-package`, `gpio-packageinput` |
| `GuardedSTM32H7_RCC` | `hseFrequency` (no default, `1300b` (4)) | `TVI-RCC-HSE` at an HSE switch | `rcc` suite |
| `GuardedSTM32H7_Flash` | `optionBytes` (the consumer's `init` word, moved) | its stock init cannot apply (E29, `1304`) | `flash` suite |
| `GuardedSTM32H7_ADC` | `levels` (or `SetVoltage`) | `TVI-ADC-LEVEL` at a `DR` read | `adc-undeclared` |
| every guard | `mode` ("enforcing" default) | — (defaulted) | — |

**So the stop is L3:** the consumer cannot build a platform that loads every guard correctly without facts the release does not state. It must read them from this tree's restate files and `tests/1301/replay-1301.sh`, or from the work orders. Some are consumer facts (the revision, the crystal, levels and option bytes), and `1205`'s checklist covers them. But the release does not say **which properties each guard takes or requires**, so a consumer cannot know to supply them.

**The fix is a release-program change (WO §7), which needs a ruling.** Options:
- **(A)** Ship the eleven generated restate files in the tarball, as `source` lines already hash them, plus one manifest line per guard naming its required and optional properties. **This partly reverses `RUL-09`'s amendment** (a shipped fragment instead of the class map alone).
- **(B)** Keep the class map alone, and add only the per-guard property lines to the manifest (`property <guard> <name> required|optional <meaning>`). The consumer's emitter does (b) itself and supplies what each line names.

I recommend **(B)**: it is the smallest change that closes the measured lack, and it keeps `RUL-09` as ruled. (A) is the cautious choice if Scotty wants the consumer's restatement checked byte for byte against Tviburi's.

### 2.1 After Q23–Q25 (ruled 18:38Z): the rehearsal from the tarball alone, **green**

**Built:**
- `property` rows: 39, one per public constructor parameter of each of the 11 guards, checked against the C# source;
- the `build` lines carry each assembly's sha256, and the build refuses a mismatched `Renode.dll` or `Infrastructure.dll`;
- MANIFEST format 2.

The release hash moves to `sha256:18ef6b10…9773` on `c7ef230`; no committed test pinned the old one. GPIO's two port-A/B reset values had no case; `spike/2100/gpio-reset.sh` adds `ospeedrreset` and `pupdrreset`, both refusing `TVI-GPIO-CONFIG`.

**The rehearsal, run by a fresh worker behind a firewall.**
- **What it could read:** the tarball, `RUL-07/09/10/11/12/19`, the image's stock files, the two captures and the board's consumer facts (revision V, the battery level, PB6/PB7 pull-ups, USART1's receive request 41).
- **What it could not:** `model/`, `tests/`, `scripts/` or the replay harness.
- **What it wrote:** the consumer's side, `spike/2100/consumer-platform.py` (the class map applied by (b), every `property` row supplied), `consumer-argv.py` and `consumer-boot.sh`.

**Result on `c7ef230`'s release `sha256:18ef6b10…`:**

| Run | `tviburi-check` (the release hash, the MANIFEST's 34 instances) | Enforcing witnesses | Ends | Refusals | Arms |
|---|---|---|---|---|---|
| ArduPilot's fixture | CHECK PASS | 34 | 34 | 0 | — |
| The armed run | CHECK PASS | 34 | 34 | 0 | yes, from 9.332 s |

`consumer-verify.sh` passes in a fresh directory with only the tarball.

**Controls** (the first run, on the previous head): without the battery `levels`, the armed run refuses `TVI-ADC-LEVEL`, `unmodeled`. ArduPilot still arms after that enforcing refusal, so arming is not a gate; the records are (`RUL-08`).

**The worker's ten findings, and what each became:**
1. **GPIO reset values:** the rows said "the stock entry's", but no stock entry declares them; Tviburi's generator writes RM0433's (p541–542). **Corrected:** each row now states the value per port.
2. **HSE:** neither capture declares one; a consumer fact (`1205`'s checklist).
3. **`optionBytes`:** "the stock entry's init" was wrong; it is the consumer's own init word. **Corrected.**
4. **The PWR stub's `size`, `initable` and `script`** have no rows. **Now stated:** the MANIFEST says a stock property with no row is not the guard's, and is dropped.
5. **Connections** (USART1's `ReceiveDmaRequest -> …@41`) are the consumer's own wiring, with no row; a consumer fact (`1205`).
6. **`RUL-09` (b) is silent on references from live entries:** other peripherals' `rcc` registrations, the timers' AF inputs, `spi4`, `crypto`. The worker repointed them as (b)'s trap implies. **A row for the architecture's wording** (a steering document, not this fence).
7. **`frequency`:** the consumer's own value overrides the stock entry's, and must move onto the guard. **Corrected.**
8. **ADC `levels`** named no instance of the ADC1/ADC2 pair. **Corrected:** the row names `adcM1S2` and `adc3`.
9. **`package` "LQFP144"** is a consumer fact (the part's package).
10. **The ledger misstates PWR.** Its 74 field rows are the guard's, but `pwr`'s only model-level row is the stub's `canned` row ("a stock Python script's fixed answers"), with no `guarded_by`. A consumer reading the ledger by its hash would think `pwr` is the stub. That is a release-program ledger change outside the widened fence: **Q26.**
