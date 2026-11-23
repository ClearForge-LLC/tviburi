# TVI-WO-1150 — questions and predictions, committed before any run

**Written:** 2026-11-23, before any container of this work order ran. Nothing below is measured.
**Read so far (read, not run):** Aethelgard `main` `7dec1d7b` (`crates/aethelgard-cli/src/renode.rs` `config()` L73–94 and `log_stamped` L2332; `main.rs` L1940–2070; `ci/foreign-judge.sh`); its image pin `e5fc7ddb…`; the held ArduPilot ELF's sha256 `a799dfb6…` (the one `4400e`–`4400g` ran on `sentinel-02-own`).
**Marking:** "from memory" is a guess about Renode not checked against any source this session.

## (i) Re-typing stock instances (`RUL-09`)

**Question.** In a `.repl` that includes `platforms/cpus/stm32h753.repl`, does a later entry `timer1: <SpikeClass> @ sysbus …` re-type the stock instance, merge into it, or fail as a duplicate? Does `peripherals` name the new class? Do the stock entry's interrupts, `RegistrationPeripheralAlias` entries and constructor arguments survive?

**Prediction.**
- From memory: Renode's `.repl` treats a second entry for a declared name as an update. An update that names a type replaces the type and drops the earlier entry's constructor attributes, but keeps the registration and interrupts unless they are restated. Confidence: low.
- So the shortest working form is probably `timer1: Spike.SpikeTimer @ sysbus <addr>` with the constructor arguments restated (`frequency`, `initialLimit`), or with the type alone if the merge keeps them.
- `peripherals` will name the new class whenever the load succeeds.
- `rcc` is harder. Other entries reference it (`STM32H7_RCC`'s constructor takes peripherals by name, and others may name `rcc`), so an update that drops attributes may fail on a missing argument.

**What would change a ruling.**
- No form re-types: the stop in WO §7. `RUL-09`'s class map then needs Aethelgard to stop including the stock entries.
- A form that loads but where a write still reaches the stock model: the class map is unsafe as written.
- A form that works but drops interrupts silently: the class map needs a "restate" list per class.

## (ii) The console fixture (`RUL-11`, B6)

**Question.** Under Aethelgard's `config()` text passed as `--config`, how do `0100a`'s records look on the console? Does a reader applying `RUL-11`'s grammar give the same records from the console and from a `logFile` capture of the same run?

**Prediction.**
- Console lines: `[00:00:00.nnnnnnnnn] [ERROR] spi1: {…}`, a 21-byte virtual stamp. The `logFile` lines keep a host stamp `[HH:MM:SS.ffff]`, because `log-timestamps-type` applies to the console backend only (from memory; low confidence). If it applies to both, the `logFile` framing is the virtual one, and `RUL-11` (b) is wrong as written.
- `use-synchronous-logging = true` removes `0100a` F7's mid-line interleave on the console.
- `collapse-repeated-log-entries = True` folds identical lines. Records differ by `seq`, so none is folded.
- `always-log-machine-name = False`: the source is `spi1`, not `machine-0:spi1`.
- A multi-line message: continuation lines unstamped and indented (the review's sub-reviewer, from source), so the reader rejects them and no record is split.
- **The empty environment:** `0100a`'s `Refusal.cs` reads `$TVIBURI_MODEL_DIR`. Run with Aethelgard's empty environment, the witness is a `TVI-LOAD` refusal and every guarded write refuses (review A2). Both are captured and labelled: the environment exactly as Aethelgard's, and with only that variable set.
- The `forge` firmware's UART text is never accepted as a record, because its source is `usart3` or the analyzer.

**What would change a ruling.**
- A record the firmware can write under this configuration: the stop in WO §7; `RUL-11` broken.
- The two framings give different records from one run: `RUL-11`'s "the record is the text after `<source>: `" is not framing-independent.
- `logFile` lines carry a virtual stamp under this configuration: `RUL-11` (b)'s wording is wrong.

## (iii) The release constant (`RUL-10`)

**Question.** Can a constant in a file included by `include @file.cs` carry the manifest's hash to the witness, with `compiler-cache-enabled = False`?

**Prediction.**
- Yes. One generated file, `Release.cs`, holding only `const string Release = "<sha256 of the manifest>"`. The manifest lists every other file's sha256 and names `Release.cs` by its fixed template, so it does not hash itself. A consumer then checks: every sealed copy against the manifest; `Release.cs` byte for byte against the template filled with the manifest's own sha256; and the witness's constant against the pinned manifest hash.
- The compiler cache is off, so the constant compiles fresh each boot. It cannot be stale from a cache.
- What the consumer catches: an edited model file (its file hash), a swapped constant (the byte-for-byte check, and the witness), a stock run (no witness), and a stale constant (the witness disagrees with the pin). What it cannot catch: a model compiled from bytes other than those the consumer hashed, which the consumer rules out by including only its sealed copies.

**What would change a ruling.** If two `include @` files cannot share a constant (a separate compilation unit per include), the constant must sit in each model file. That reopens self-hashing, so `RUL-10` (b) needs another carrier.

## (iv) Recorded mode (`RUL-19`)

**Question.** A spike copy of the SPI guard, with its mode from the platform description. Its witness carries `mode`; each would-be refusal is a `would-refuse` record with its `kind`; the access then goes to the stock model. Does `0100a`'s unchanged check fail a recorded run, and why?

**Prediction.**
- The `modf` firmware in recorded mode: one `would-refuse` record, then the stock model takes the write. The check, which knows only `refusal`, `witness` and `end`, fails on an unknown record type, or passes if it skips unknown types.
- **The discriminating case** is recorded mode with the `modeled` firmware, which makes no `would-refuse` at all. If `0100a`'s check accepts any witness with a well-formed version (review A8), it passes that run. Then the unchanged check does **not** fail a recorded run, and the reason is that it never reads `mode`. Confidence that it passes: medium-high.

**What would change a ruling.** If the unchanged check passes a recorded run, `RUL-19`'s "a check that fails closed on a missing mode" is the only defence, and it must land in `0100b` before any recorded witness ships.

## (v) ArduPilot's queue (`RUL-13`, `OQ-03`)

**Question.** The access log of the held ELF on `sentinel-02-own`'s emitted platform under `simulate --foreign`, joined into `1100`'s `queue.tsv` with a column for "ArduPilot takes it", then re-ranked by `RUL-13`'s amended order.

**Prediction.**
- ArduPilot writes RCC enables and kernel-clock selectors (`D2CCIP1R`, `D2CCIP2R`, `D3CCIPR`), GPIO `MODER`/`AFR`/`OSPEEDR`/`PUPDR` on every port it uses, DMA stream registers (`SxCR`, `SxNDTR`, `SxPAR`, `SxM0AR`) and DMAMUX, SPI `CFG1`/`CFG2`/`CR1` on its SPI buses, TIM `CCMR`/`CCER`/`BDTR` if it starts motor outputs within the window, and USART `CR1`–`CR3`/`BRR`.
- In a 5 s window ArduPilot does not arm, so `BDTR.MOE` may never be written. The motor class may rank lower than `1100` put it, and clocks and GPIO higher.
- The recorded guard on SPI: `would-refuse` at every SPI start for `SSM` = 0 with `SSOE` = 1 (review A6), one per bus per start.
- DMA-driven SPI data writes to `TXDR` do not appear in the access log as CPU accesses. If they appear at all, the source is the DMA and not the CPU (from memory; low confidence).
- Sentinel-03's fixture: unchanged since `f42a0c88`, so its rows are reused.

**What would change a ruling.** If ArduPilot never writes a field `1100` ranked in its proposed cut (1–172), that row drops below the clocks and GPIO in the amended order. `OQ-03` itself is Scotty's; no cut is proposed here.

## (vi) Cost, and the records across a reset (review A11)

**Question.** Per boot, with and without the overlay, under Aethelgard's configuration: the compile time to the `peripherals` listing, and peak RSS; the same for an ArduPilot run, against its 833 MiB and the 1 GiB bound. Across `machine Reset` with cold writes re-applied: the witness, `seq`, the latch, the `end` record. The `pc` of a refusal caused by a monitor write, and by a DMA write.

**Prediction.**
- Compile of two small C# files with Roslyn, no cache: about 1–3 s more per boot, and 30–80 MiB more peak RSS. An ArduPilot boot under the overlay stays under 1 GiB, with less margin than today. Confidence: medium. Host load moves both.
- Across `machine Reset`: the guard's `Reset()` clears its copies. The witness is emitted at registration, which a reset does not repeat, so there is no second witness. `seq` is a static or a per-instance counter and does not restart. The latch: if it is an instance field cleared in `Reset()`, it clears, which would be a defect under `RUL-08`. One `end`, from `Dispose`, at process exit.
- A monitor write: `pc` reads the CPU's current PC, a real-looking address, not `none`. A DMA write: the same. So neither can be told from a CPU write by `pc` alone. Confidence: low; this is the measurement that matters most here.

**What would change a ruling.**
- An overlay that takes ArduPilot's boot past 1 GiB or 30 s: the stop in WO §7.
- A latch cleared by reset: `RUL-08` is violated across a brown-out.
- A monitor write indistinguishable from a CPU write: the response's §4.7 assumption fails, and the Phase 2 row needs another discriminator.
