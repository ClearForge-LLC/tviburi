# TVI-WO-1150 — The seam spike: re-typing, the console, the release constant, recorded mode, ArduPilot's queue, and the cost per boot

**Repo:** `ClearForge-LLC/tviburi` · **Base:** `main`, named in the kickoff.
**Branch:** `wo/tvi-1150-seam-spike`.
**Author:** Claude (co-architect) · **Builder:** standard tier, with a targeted adversarial pass.
**Phase:** 1. **Cadence:** a measurement that stops. No change under `model/`, `scripts/`, `tests/` or `tools/`.

**Grounds:**
- **Northstar** invariants 1, 3, 5, 8.
- **Architecture:** `RUL-09`, `RUL-10`, `RUL-11`, `RUL-13` (each as amended 2026-11-23), `RUL-12`, `RUL-19`, `OQ-03`; §2.0, what the consumer does today.
- **Roadmap:** Phase 1, the `1150` row.
- **The review** `docs/reviews/2026-11-23-mythos-5-2-tviburi.md`, A1–A4, A10, A11, B5, B6, and its response.

> **What this is:** six measurements the amended rulings rest on, made before `TVI-WO-1200` builds on them.
> - **(i) Re-typing stock instances** (`RUL-09`). In the pinned build, a `.repl` that includes `platforms/cpus/stm32h753.repl` and then re-declares `timer1` and `rcc` with a spike subclass of each stock class. Record what the grammar allows: does the entry re-type the instance, merge into it, or fail as a duplicate registration? Does the `peripherals` listing name the new class? Do the stock entry's interrupts, `RegistrationPeripheralAlias` entries and constructor arguments survive, or must they be restated? Name the shortest form that works, or that none does.
> - **(ii) The console fixture** (`RUL-11`, B6). Run `0100a`'s `modf`, `modeled` and `forge` firmware with Renode's console as Aethelgard runs it: its `config()` text (`crates/aethelgard-cli/src/renode.rs` L73, at the Aethelgard commit you name), passed as `--config`, read the way its `log_stamped` (L2332) reads. Commit the captures under `spike/1150/fixtures/`, each naming that commit. Write a scratch reader in `spike/1150/` that applies `RUL-11`'s record grammar to both framings, and show it gives the same records from a console capture and a `logFile` capture of one run. Record how these keys show in the lines: `use-synchronous-logging`, which may remove `0100a` FEEDBACK F7's mid-line interleave; `collapse-repeated-log-entries`, which `seq` exists for; `always-log-machine-name`; and a multi-line message.
> - **(iii) The release constant** (`RUL-10`). Can a constant in a file included by `include @file.cs` carry the manifest's hash to the witness, under Aethelgard's `compiler-cache-enabled = False`? The manifest lists every file's hash, so a constant inside a hashed file would hash itself. Prototype one scheme that avoids that (for example one generated file holding only the constant, outside the manifest's hash and checked byte for byte), and show what a consumer that hashes its sealed copies catches with it: an edited model file, a swapped constant, a stock run, a stale constant.
> - **(iv) Recorded mode** (`RUL-19`). A spike copy of the SPI guard whose mode comes from the platform description, never from the environment. Its witness carries `mode`, and each would-be refusal is a `would-refuse` record with its `kind`, after which the access goes to the stock model. Show that `0100a`'s unchanged check fails a recorded run, and say why it fails.
> - **(v) ArduPilot's queue** (`RUL-13`, `OQ-03`). The access log of unmodified ArduPilot, the held ELF (on Furnace, `~/.cache/aet-4240v/ardupilot/arducopter.elf`; record its sha256), on the Sentinel-02 platform that `simulate --foreign` emits (Aethelgard architecture §2.27; name the spec). Use `1100`'s harness (`spike/1100/harness/`). Run once more under (iv)'s recorded guard on every SPI instance it uses, and list the `would-refuse` records. Then join the log into `1100`'s queue format (`spike/1100/data/queue.tsv`), with a column for "ArduPilot takes it", and re-rank by `RUL-13`'s amended order. **Sentinel-03 is already in `1100`'s 13 boards** (`spike/1100/queue.md` §1): reuse its rows, and re-measure only if Aethelgard has changed `crates/aethelgard-cli/tests/fixtures/sentinel-03.json` since `f42a0c88`. Propose no cut: `OQ-03` is Scotty's.
> - **(vi) Cost, and the records across a reset** (review A11). Per boot, with and without the overlay, under Aethelgard's configuration: the compile time to the `peripherals` listing, and peak RSS. Then the same for an ArduPilot run, against its 833 MiB and the 1 GiB bound (Aethelgard architecture §2.27). Across `machine Reset` with cold writes re-applied, as Aethelgard renders a brown-out: is the witness re-emitted? Does `seq` restart? Does a latched refusal stay latched? Is there one `end` or two? Record the `pc` of a refusal caused by a monitor write, and by a DMA write if one can be made, and whether either can be told from a CPU write.
>
> It is NOT:
> - any change to `model/`, `scripts/`, `tests/` or `tools/` (`TVI-WO-0100b` holds them; its console grammar waits for (ii));
> - the identity or the ledger (`TVI-WO-1200`), or a ruling on `OQ-03`;
> - any change to Aethelgard, or any write to its repository.

## 1. Measure

1. **Commit the questions and predictions** in `spike/1150/predictions.md` before any run: for each of (i)–(vi), what you expect and what would change a ruling.
2. **Run (i)–(vi), in that order.** Every container goes through `~/bin/capped-run`, one at a time, at `nice -n 10`, in the image Aethelgard `main`'s `container/IMAGE` names, with `--network none`. Clone Aethelgard read-only to `~/tvi-1150-aet`, with its push URL disabled; never touch another worktree.
3. **Write the results** in `spike/1150/results.md`: one section per item, each ending with the ruling it confirms, contradicts or leaves open. A scratch reader may live under `spike/1150/`, with no build file committed.

## 2. Acceptance

- **Each item is answered by a run, or named as not answered with the reason.** Facts read from source and not run are marked so.
- **(ii)'s captures are committed** and name the Aethelgard commit their configuration came from.
- **(v)'s joined queue cites both views** as `1100`'s does, and states the ELF's sha256 and the platform's spec.
- **No model, script, test or tool changes.** Spike-only: the gates are not run. Say so.

## 3. Fences

- **Fence:** `spike/1150/` and `docs/work-orders/TVI-WO-1150.FEEDBACK.md`.
- **Not:** `model/`, `scripts/`, `tests/`, `tools/`, the steering documents, `docs/reviews/`, or Aethelgard.
- **Running beside you:** `TVI-WO-0100b`. Your fences do not overlap. Post (ii)'s fixture commit on the bus when it lands; `0100b` waits for it.
- One PR, spike-only, left unmerged. Write `~/.cache/tvi-1150/pr-title-body.md`.

## 5. Adversarial pass, targeted

- **A re-typed instance that loads but is not the guard:** the listing names the class, yet a write reaches the stock model. Prove each by a guarded write.
- **A console line that the grammar accepts as a record but whose source is not the guarded peripheral,** or a record split by the console.
- **A release constant that matches while a model file differs,** or a recorded run any reader could take as a pass.
- **An ArduPilot write the access log misses:** DMA, or a write after the logged window.

## 7. Flag-and-stop conditions

- **Stop after §1.3** with the results.
- **(i) shows that no `.repl` form re-types a stock instance.** Stop with the reproduction: `RUL-09`'s class map then needs Aethelgard to stop including the stock entries, which is a consumer change.
- **(ii) shows a record a firmware can write under Aethelgard's configuration.** Stop with the reproduction: it breaks `RUL-11`.
- **(vi) shows the overlay takes an ArduPilot boot past the 1 GiB or 30 s bound.** Stop: it is northstar §6's failure.
