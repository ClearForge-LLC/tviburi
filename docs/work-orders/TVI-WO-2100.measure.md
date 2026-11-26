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

## 2. The rehearsal: the consumer with only the tarball — **stopped (WO §7, the second condition)**

**The tarball holds four files:** `MANIFEST`, `Tviburi.cs`, `TviburiRelease.cs.template` and `LEDGER.jsonl`. Their bytes check against the manifest with `sha256sum` alone (`file`, `overlay` with its length, `ledger-rows`). So `verify` needs nothing from the tree, beyond the verifier itself if the consumer uses Tviburi's.

**Building the platform from the manifest's class map and the pinned stock files cannot be done.** The manifest states:
- 11 `class` rows (stock type → guard type);
- 32 `restate <stock type> <instance> platforms/cpus/stm32h7.repl` and 2 `retype` lines;
- the stock pins.

It does not state:

| # | What the consumer lacks | Where it lives in this tree |
|---|---|---|
| L1 | **The restatement texts.** The manifest names the eleven `model/stm32h7-*-restate.repl` files, the board `model/tviburi-nucleo-h753zi.repl` and `tests/1301/replay-1301.sh` as `source` lines by sha256, but **none is in the tarball**. A consumer can check their hashes but cannot get them | `model/stm32h7-*-restate.repl` |
| L2 | **Each guard's declaration**: its variable name, and that its registration names the RCC guard's variable (`tviburiRcc RegistrationPeripheralAlias.X`), not the cancelled `rcc`. For the RCC itself, that is the 58 stock registrations restated onto it (`RUL-07`) | the restate files |
| L3 | **Properties that are not the stock entry's**, each one the guard refuses (or runs unguarded) without: `revision` on `GuardedSTM32H7_ADC` and `GuardedSTM32H7_PWR`, required and never defaulted (`TVI-ADC-CONFIG`, `TVI-PWR-CONFIG`); `dmamux:` on `GuardedSTM32DMA`, the DMAMUX1 guard's variable (`null` → "no `dmamux`", refusing); `hseFrequency` on the RCC guard; `package` and `external` on the GPIO guards; `optionBytes` on the flash guard; `levels` on the ADC; `mode` on every guard | the guards' constructors; the restate files; `replay-1301.sh` |
| L4 | **The cross-references a cancel leaves dangling**, which bind to the cancelled entry silently (`RUL-09` (b)): `dmaPeripheral: dmamux1` (the ADCs); DMAMUX1's routes onto the DMA guards; `spi4`'s `DMARecieve -> …@83` and `crypto`'s `OutDMA -> …@77`; `@ gpioPortX`, `-> gpioPortX`, `timerN@`, `@ i2cN` | the restate files; `replay-1301.sh`'s per-guard blocks |
| L5 | **The order of the restatements**: the DMA guards before DMAMUX1's; ADC's before DMAMUX1's untyped updates | the board's `using` order |
| L6 | **What a consumer must do with its own untyped blocks** for a cancelled instance (move the timers' and USARTs' `frequency` onto the guard; turn the flash controller's `init` into `optionBytes`; drop the PWR script) | `replay-1301.sh`; `TVI-WO-1205.consumer.md` (when merged) |

**So the artifact as released is a snapshot of this repository, not a release.** A consumer needs `model/` for L1–L5, and Tviburi's own replay harness for L6.

**The fix the WO names is a release-program change, which needs a ruling.** Two options:
- **(A)** Ship the restatement in the tarball, generated as today: the eleven restate files, or one `platform.repl` fragment in the board's order. Add a manifest line per guard naming each property it requires or accepts (L3).
- **(B)** Have the manifest carry every fact as rows: declarations, properties, cross-references and order. The consumer's emitter then generates the restatement. That is more work for both sides, and a second transcription of what the `.awk` files already generate.

I recommend (A).
