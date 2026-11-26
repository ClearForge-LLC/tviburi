# TVI-WO-2100 — The first release

**Repo:** `ClearForge-LLC/tviburi` · **Base:** `main`, named in the kickoff.
**Branch:** `wo/tvi-2100-first-release`.
**Author:** Claude (co-architect) · **Builder:** strong tier (the `1308` builder), with a full adversarial pass.
**Phase:** 2, Tviburi's half. Written 2026-11-26. **Cadence:** built beside `TVI-WO-1205`, which judges Phase 1's gate. The builder never tags. The tag is the architect's, after `1205`'s verdict reads "met" and Scotty says go.

**Grounds:**
- **Phase 2's work** (roadmap): "Tviburi's half is a first release, the `RUL-09` artifact." Aethelgard's half is its own work orders, cut from `TVI-WO-1205.consumer.md`.
- **`RUL-09`**: the artifact, its manifest and class map, cancel and restate, one file. **`RUL-10`**: the identity, the release constant, the consumer's file hashes, the compiler cache off.
- **The release program exists** (`tools/tviburi-release`, `TVI-WO-1200`, `1203`). `build` writes the four files and the tarball. Two builds of one tree are byte-identical. `verify` is the consumer's binding check as bytes. The release refuses a restated instance no committed replay runs under its guard (`1203` R1).
- **Not yet done:** a release built from a `main` commit by CI and kept; a consumer that has only the tarball; notes that say what the consumer does; and a check of the pins against what Aethelgard runs today.
- **Section 4.1 of the architecture:** a release is a reviewed, merged commit, and its tarball is reproducible from that commit.

> **What this is:**
> - **(1) The release job.** A `workflow_dispatch` job in CI, given a commit on `main`. In the pinned tools image it does the following:
>   - runs `tviburi-release build` twice into two fresh directories and compares them byte for byte;
>   - runs `verify` on one against the hash it printed;
>   - keeps the tarball, `MANIFEST` and `LEDGER.jsonl` as the job's artifact, with both hashes and the commit in the job summary.
>
>   It fails on any difference, on a commit not on `main`, or on a dirty tree. It never creates a tag or a GitHub Release.
> - **(2) The rehearsal: the consumer with only the tarball.** In a fresh directory with no checkout, the job takes the tarball, the consumer's pinned Renode and the stock files the manifest pins. It then:
>   - runs `verify`;
>   - builds the platform the way a consumer must, from the manifest's class map and the pinned stock files;
>   - boots ArduPilot's fixture and the armed run with the compiler cache off;
>   - runs `tviburi-check` with the release hash.
>
>   The consumer's own inputs (the ELFs, the board's stock-typed `.repl` and its scenario) may come from the tree, as Aethelgard's would come from Aethelgard. Nothing of the overlay may: no restate file, no model, no generator.
>
>   Pass: zero records, and the armed run arms. Two answers are wanted. Does any step need a file from the tree? Does the consumer need anything the manifest doesn't say? This is the step that tests whether the artifact is a release or a snapshot of this repository.
> - **(3) The pins against Aethelgard's tools image.** Do this read-only, from Aethelgard's public `main`. Check:
>   - that the manifest's `renode` build string is the one Aethelgard's `container/smoke.sh` asserts;
>   - that its stock pins match the files in that image, by sha256;
>   - whether the manifest's `infrastructure` build string is still true. Aethelgard rebuilds `Infrastructure.dll` with three NVIC commits (`AET-WO-1605`, `container/TOOLS-SOURCES.md`), and the manifest says `1.0.0+cd4b002…`. Measure what that assembly reports in Tviburi's pinned image and in Aethelgard's.
> - **(4) The release notes,** `docs/releases/v0.1.0.md`:
>   - the commit, the release hash, the ledger hash, the tarball's sha256, and the Renode build;
>   - how to verify;
>   - what is guarded (the cut, as ruled) and what is acknowledged (the ledger);
>   - the memory figure against the consumer's 1.5 GiB ceiling, from `1201`'s budget on that commit;
>   - what the consumer must do. Cite `TVI-WO-1205.consumer.md`'s items by number; never restate them;
>   - the rows that carry into Phase 2 and Phase 4.
>
>   The hashes are the release job's, filled in after the job runs on the release commit.
>
> It is NOT:
> - the tag or the GitHub Release (the architect's, after the verdict and Scotty);
> - a model or rule change (a gap is a row, or a stop);
> - Aethelgard's work;
> - the steering documents.

## 1. Build

1. **(3) first,** because it is read-only and a mismatch changes what (1) and (2) must pin.
2. **(1), then (2) on the branch,** against `main`'s commit as of the kickoff.
3. **(4) as a draft** with placeholders for the hashes.
4. **When `1205` merges,** merge `main`, run the job on the merged `main`, and fill (4) from it. Then READY.

## 2. Acceptance

- **The release job is green** on a `main` commit. The two builds are byte-identical, `verify` passes, and the summary carries both hashes.
- **The job fails as planted:**
  - on a commit not on `main`;
  - on a tree with one byte of a model changed after the first build;
  - on a `verify` given the wrong hash.
- **The rehearsal is green** from the tarball alone, or it stops with the file or fact the consumer lacked.
- **The notes:**
  - cite every one of `1205.consumer.md`'s items;
  - carry the hashes the job printed;
  - quote the memory figure with its run id.

## 3. Fences

- **Fence:**
  - the new release workflow and its scripts under `ci/` and `.github/workflows/`;
  - `spike/2100/`;
  - `docs/releases/`;
  - `docs/work-orders/TVI-WO-2100.*`.
- **Only after a stop is ruled:** `tools/tviburi-release/`, if the rehearsal shows the manifest lacks something the consumer needs.
- **Not:** every model, `tests/`, the steering documents, `TVI-WO-1205.*`.
- **Running beside you:** `1205`, which judges `main` read-only and writes its own files.
- One PR, left unmerged. Write `~/.cache/tvi-2100/pr-title-body.md`.

## 5. Adversarial pass, full

- **A release that only reproduces on this checkout:** a path, a timestamp, a locale, an umask or an image layer that the two builds share but a consumer would not.
- **A rehearsal that passes for another reason:** a file from the tree reached through a path, or a cached assembly. `tviburi-check` reading a log that has no witness counts as a fail, so prove the witness is there.
- **A pin true today that the consumer cannot check:** a build string no command prints, or a stock file the consumer's image does not hold.

## 7. Flag-and-stop conditions

- **The two builds differ.** Stop with the first differing byte and its file.
- **The rehearsal needs a file from the tree, or a fact the manifest does not state.** Stop with it. The fix is a release-program change, and it needs a ruling.
- **A pin does not match Aethelgard's image.** Stop with both values.
- **`1205`'s verdict is "not met".** Stop after (1)–(3), with the notes in draft.
