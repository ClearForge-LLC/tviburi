# Tviburi — first independent review, and its seam with Aethelgard (Mythos 5.2)

**Reviewed:** `ClearForge-LLC/tviburi` `origin/main` at `963238f9`; PR #4 (`claude/tvi-wo-0100-blind-build`, `e47e8d60`); and the pushed `0100a` branch (`wo/tvi-0100a-blind-build-fixes`, `2d23ab5`), which is the live build. Aethelgard at `7fbe5d11`. Read-only clone at `~/tviburi-review`. No branch for `TVI-WO-1100` was pushed when I read.
**Method:** I read the steering documents, both work orders, both builds' models, and the Aethelgard code at the seam. One sub-reviewer checked the SPI guard against RM0433 Rev 9 and read the reference check; its findings are marked "sub". **No container was run:** every finding is from reading, a disassembly, or a host command.

## Executive summary

1. **The seam as written does not yet fit Aethelgard in three places.** The reference check reads a log dialect Aethelgard does not produce (A1). The model's identity needs an environment variable that Aethelgard's sandbox clears (A2). `RUL-09`'s "fragment that swaps stock peripherals" was measured only where the stock platform had no instance to swap (A3).
2. **One part fits for free.** Aethelgard already fails a boot on any `[ERROR]` log line, and a refusal is logged at Error (A4).
3. **The guard is a deny-list; invariant 1 asks for an allow-list.** Any write the guard has no rule for goes to the stock model. That is the silent pass the project exists to remove, and it is false on day one the way invariant 2 was (A5).
4. **The 0100a guard refuses unmodified ArduPilot at every SPI start,** on a configuration RM0433 says cannot mode-fault (A6). Aethelgard's own generated bring-up passes.
5. **One false-pass route in the guard:** the guard's copy of `CFG2` disagrees with what the firmware reads back (A7).
6. **No refusal at head has a wrong condition text.** Every cited page was re-checked (sub).
7. **A refusal means two different things today** — "silicon would fault" and "the twin cannot say" — and a consumer needs to tell them apart (B1).
8. **Highest leverage for Aethelgard is the clock tree, not motor timers** (Part C): what flies is ArduPilot, and the missing kernel clock is the scar class already seen twice.
9. `RUL-13`'s order, and `TVI-WO-1100`'s scope, miss both ArduPilot and Sentinel-03 (A10).

---

## Part A: findings

| Id | Finding | Concerns | Severity | Confidence |
|---|---|---|---|---|
| A1 | The reference check reads a dialect the consumer does not write | seam | high | high |
| A2 | Model identity needs an environment the consumer clears; `RUL-10` may not be buildable as worded | seam | high | high on the fact, medium on `RUL-10` |
| A3 | "A fragment that swaps stock peripherals" is unmeasured for stock instances | seam | high | medium |
| A4 | Refusals already fail Aethelgard's boot, unnamed | seam | low (good news) | high |
| A5 | The guard passes everything it has no rule for (known to the builder, not ruled) | Tviburi | high | high |
| A6 | ArduPilot is refused at every SPI start, against the manual | Tviburi, seam | high | high on order, medium on decoder |
| A7 | Guard copies diverge from read-back; one route is a false pass | Tviburi | high | high |
| A8 | The check takes no expected hash and ignores unnamed guards | Tviburi, seam | medium | high |
| A9 | Identity hash: same hash for different models | Tviburi | medium | high |
| A10 | `RUL-13` and `1100` measure the wrong firmware and miss Sentinel-03 | steering | medium | high |
| A11 | Steering-document gaps | steering | medium | high |
| A12 | Over-strict rules and weak tests | Tviburi | low | medium |

### A1. The reference check reads a dialect the consumer does not write — seam — high
- **Evidence (read).** `tools/tviburi-check/src/main.rs:3-6, 47-66` accepts lines of Renode's `logFile` backend, `[HH:MM:SS.ffff] [LEVEL] source: {…}`, a 13-character host timestamp. `scripts/prelude.resc:4` opens that file.
- Aethelgard reads Renode's **console**, with `log-timestamps-type = Virtual` set in its own sealed configuration. Its parser requires a 21-byte stamp, `[hh:mm:ss.nnnnnnnnn] ` (`crates/aethelgard-cli/src/renode.rs:2332-2352`). `grep logFile crates/aethelgard-cli/src` finds nothing.
- **Consequence.** Phase 1's gate clause 3 ("a consumer-side check that reads only the records") can pass in CI while proving nothing about the first consumer's parser. The `end` record and "exactly one witness" rules are also written for one process per run; Aethelgard's droop scenarios reset the machine mid-run.
- **Recommendation.** Define the record grammar as "the message text after `source: `", and state both framings. Test the check against a console capture made under Aethelgard's configuration file, which is a small committed text.

### A2. Model identity needs an environment the consumer clears — seam — high
- **Evidence (read).** `model/Refusal.cs` (0100a) computes the version from `$TVIBURI_MODEL_DIR`. Aethelgard runs every tool with `env_clear()` (`crates/aethelgard-cli/src/run.rs:556`; `architecture.md` §6 step 4). As built, every guarded run inside Aethelgard would emit `TVI-LOAD` and refuse every write. The builders know the variable is wrong (`RUL-10`; deferred to `TVI-WO-1200`).
- **The deeper point (opinion).** `RUL-10` says the models compute the hash "from the bytes they were compiled from, at load, never from a name". A model compiled by `include @file.cs` has no handle on its own source bytes except through a path, which is a name. I do not see how `1200` builds this as worded.
- **What Aethelgard already does.** It copies each of its own models into sealed scratch, includes that copy, and pins the file's sha256 on its side (`renode.rs:27-31`; `twin/src/model.rs:62-73`). The consumer hashing exactly the bytes it handed Renode is the stronger identity.
- **Recommendation.** Amend `RUL-10`: the consumer hashes the overlay files it includes against the manifest; each model carries a compiled-in release constant, which the witness reports and the consumer compares. Rule this before `1200` starts.

### A3. "A fragment that swaps stock peripherals" is unmeasured for stock instances — seam — high
- **Evidence (read).** `TVI-WO-0100` registered a guarded SPI1 on the Nucleo board file, where "Stock Renode only Tags SPI1" (`model/tviburi-nucleo-h753zi.repl:2`). Nothing was replaced.
- **How Aethelgard builds its platform:**
  - It emits its own `.repl`, adding SPI instances with the type string `SPI.STM32H7_SPI` (`crates/aethelgard-twin/src/platform.rs:229`); only `spi4` is stock (`:74`).
  - It pins `platforms/cpus/stm32h7.repl` by sha256 (`RENODE_FILES`, `platform.rs:37-49`), and that file declares the timers, RCC, I2C and DMA: the families `RUL-13` puts first.
  - It judges the `peripherals` listing by each instance's model name (`platform.rs:1156`). A guarded class lists under a new name.
- **Unmeasured.** Whether a `.repl` entry can change the type of an instance an included file already declared. `architecture.md` §2.9 of Aethelgard records a duplicate registration as a load failure.
- **Consequence.** For SPI, Aethelgard's emitter must itself name the guarded class. For timers and RCC, either re-declaration works or Aethelgard must stop including the pinned stock file's entries. Neither is "include the overlay's fragment" (roadmap Phase 2).
- **Recommendation.** A one-day spike before `1200`: replace `timer1` and `rcc` on `stm32h753.repl` in the pinned build, and record what the `.repl` grammar allows. Ship a class map (stock class to guarded class) in the manifest in place of a board fragment.

### A4. Refusals already fail Aethelgard's boot, without a name — seam — good news
- **Evidence (read).** A boot's findings include `renode_error_log` for any line containing `[ERROR]` in the run's log (`renode.rs:2843-2848`), and `platform_load_error` at load (`:174`). The log filter always keeps such lines within its 8 MiB budget (`:236-246`). 0100a logs refusals at `LogLevel.Error`.
- **So `RUL-12` half-holds with no Aethelgard change:** a refusal cannot pass. What is missing is the other half: a required witness, and a named finding in place of the generic one.
- **Risk.** The witness is logged at Info. If the log passes its budget, Aethelgard treats reads as unwitnessed, which fails closed; that should be confirmed for a witness line, by a run.

### A5. The guard passes everything it has no rule for — Tviburi — high
- **Evidence (read).** `model/GuardedSTM32H7_SPI.cs` (0100a): after its `switch`, every write falls through to `base.WriteDoubleWord`. The header says "Reads are not guarded".
- **Instances (sub, read against the held stock source `~/.cache/aet-4230/renode-src/STM32H7_SPI.cs` and RM0433):**
  - **No kernel clock.** `SPI123SEL` resets to `pll1_q_ck` and PLL1 is off at reset; Renode tags the selector. An enabled SPI with no clock source passes.
  - **`TXDR` written while `SPE` = 0** is queued by the stock model; the manual says it is ignored.
  - **`LSBFRST`, `CRCEN`, `COMM`, `IOSWP`, `CPOL`/`CPHA`, `MBR` are tags,** and `DSIZE` below 8 bits is sent as a byte.
- **Status.** The blind build said this itself: PR #4's `FEEDBACK.md` R3, "a deny-list of one rule", with a lean toward turning Renode's own tags and unhandled-bit warnings into refusals. `grep -i allow-list docs/` finds nothing: the rework's rulings, the roadmap and `0100a` did not take it up.
- **Why it matters.** Northstar invariant 1's falsifier is "find any path the model does not implement that still returns success". Each bullet is one. The amendment that scoped invariant 2 to Tviburi-authored models did not scope invariant 1.
- **Recommendation.** Per guarded peripheral, refuse any write to a register or field the ledger does not list as modeled or as inherited-and-acknowledged. The ledger then is the allow-list, and invariant 1 becomes checkable by enumeration.

### A6. ArduPilot is refused at every SPI start, against the manual — Tviburi and seam — high
- **Evidence (sub; quote re-checked by me).** The guard refuses `SSM` = 0 with `SSOE` = 1 (its branch "hardware NSS output … NSS output is not modeled"). RM0433 Rev 9, `SPI_CFG2` `SSM`: "When master uses hardware NSS output (SSM=0 and SSOE=1), the NSS signal input is forced to non active state internally to prevent master mode fault error" (text line 225821).
- **The refusal is deliberate.** `TVI-WO-0100a.rm0433.md` (lines 107–110) keeps it "because invariant 1 covers the unmodeled output", after PR #4's R7 leaned toward a pass. What no Tviburi document records is who takes this path: `grep -i ardupilot docs/` finds only a file path.
- **ChibiOS's order,** disassembled by the sub-reviewer from the held ArduPilot ELF (`~/.cache/aet-4240v/ardupilot/arducopter.elf`, `spi_lld_start`; decoder at `target/r3/tvi/thumb.py`): `CFG2` is written with `SSOE|MASTER` and `SSM` = 0, then `CR1 |= SPE`. The source is not held, so this rests on the decoder.
- **Aethelgard's own bring-up passes** (checked by me): `CR1 = 0x1000`, then `CFG1`, then `CFG2 = 0x84400000`, then `0x1001` (`target/r3/gen-s03/board/src/board.rs:1050-1094`).
- **Consequence.** With the overlay loaded, `simulate --foreign` cannot pass, and that is Aethelgard's only judgement of the firmware that flies.
- **Recommendation.** Pass `SSM` = 0 with `SSOE` = 1 for the mode-fault rule. If the unmodeled NSS output deserves a record, it needs a kind that does not fail the run (B1).

### A7. Guard copies diverge from read-back — Tviburi — high
- **Evidence (sub; the stock lines re-read by me).** The stock model returns `MASTER` as always 1 (`STM32H7_SPI.cs:188`) and `SSI`, `SSIOP`, `SSOE` as tags that read 0 (`:129`, `:200-201`). The guard keeps what was written and does not serve reads.
- **False pass.** Firmware that never writes `MASTER` but does `CFG2 |= SSM` reads `MASTER` = 1 from the stock model and writes it back. The guard's copy then says master, where silicon is a slave.
- **False refusals.** Any read-modify-write `CR1 |= SPE` carries `SSI` = 0. ST's HAL enables that way (from memory).
- **Recommendation.** Serve reads of every field the guard copies from the guard's copy. More generally, a guard that keeps state must own both directions of that state.

### A8. The check takes no expected hash, and ignores unnamed guards — medium (sub)
- **Any well-formed hash is accepted** as a witness (`main.rs:115-116`), and the `guard` field is never read. `RUL-12` says "with a different hash" must refuse.
- **A refusal from a guarded peripheral not named on the command line is skipped** (`main.rs:80`).
- **A pass shows a clean teardown, not completion:** the `end` record comes from `Dispose`, which SIGTERM also triggers (`main.rs:16-17`).
- **Recommendation.** Take the expected model version as an argument; fail on any `{"tviburi":` record from an unnamed source.

### A9. Identity hash: same hash, different model — medium (sub)
- The include paths are fixed in `scripts/prelude.resc`, independent of the directory hashed.
- Renode's own DLLs enter only as a version string. Aethelgard's build is a patched 1.17.0; a different patch under the same string hashes the same.
- Not hashed: the base board `.repl`, the prelude, the run scripts.
- All of this is `1200`'s; A2's recommendation removes most of it.

### A10. `RUL-13` and `TVI-WO-1100` measure the wrong firmware — steering — medium
- **`RUL-13` orders work "by what flies first: motor timers, DShot DMA…"** from the witness ledger of the *generated* firmware. The generated firmware is bring-up. What flies is unmodified ArduPilot, which Aethelgard judges separately (`simulate --foreign`) and for which it has no witness ledger at all.
- **`1100` item (1) reads "every committed board under `boards/`".** That directory holds `sentinel-01` and `sentinel-01-bench` only. Sentinel-03, the next board to be fabricated and the only one with motors, a second SPI bus and three USARTs, is a test fixture (`crates/aethelgard-cli/tests/fixtures/sentinel-03.json`) and would not be measured.
- **Recommendation.** Add to `1100`: the access log of the pinned ArduPilot ELF on the Sentinel-02 platform, and the ledger for the Sentinel-03 fixture. Rank a path by whether ArduPilot takes it.

### A11. Steering-document gaps — medium
- **Northstar §3 still promises "a replayable trace of every register access"** as the pass artifact, and invariant 6 requires it. `architecture.md` §1 and §2.1 say the interface is not a trace and a Tviburi trace has no consumer. Nothing in the roadmap builds one, and Aethelgard's log filter discards access lines it does not judge (`renode.rs:236-256`). Invariant 6 has no owner.
- **Invariant 8's second clause** ("cannot show that the refusal runtime and the hashed model are what executed") depends wholly on `RUL-10` (A2).
- **Phase 2 gate clause 5,** "No model code lives in Aethelgard's repository": Aethelgard has `models/renode/BMP581.cs` and `ICM42688P.cs`. The clause needs "no Tviburi model code", or a decision that sensor models move (B4).
- **Not addressed anywhere:**
  - **Reads.** Every rule so far guards writes.
  - **Who wrote.** A monitor write reaches the guard like a CPU write (`tests/conditions.resc` relies on it). Aethelgard's judge reads and writes registers from the monitor; a record's `pc` is then `"none"`, and the contract does not say what a consumer does with a refusal its own harness caused.
  - **Reset.** The guard's `Reset()` clears its copies. Aethelgard renders a brown-out as `machine Reset` with cold writes re-applied; whether the witness, the sequence numbers and the latch behave across that is unmeasured.
  - **Cost.** Northstar §6 names "so slow or heavy that people stop running it". Each Aethelgard boot recompiles every included model, inside a 30 s and 1 GiB bound, and a campaign is many boots. Nothing measures the overlay's compile time or memory.
- **`OQ-04`'s prior is sound, with one change.** A guard that refuses wrong values does not make a field "observed" in Aethelgard's sense, which means a reaction witnessed in this run. It makes it "refused if wrong", and only for fields whose guard predicate is complete (A5). Aethelgard's ledger should gain its own class for that, keyed to the Tviburi ledger hash, and not reuse `observed`. The independence condition in the prior (a bench scar or a separate derivation) is the right one under Aethelgard `INV-07`.

### A12. Over-strict rules, weak tests, a stale table — low (sub)
- **The history rule (D1)** refuses ST's own documented order (RM0433 p. 2171 writes `CFG2` before `SSI`) and is not cleared by `IFCR.MODFC`. The manual does not settle it; a bench row would.
- **IOLOCK:** "every `CFG2` write refuses" is right while the lock holds (p. 2215). The guard also latches it from writes made with `SPE` = 1, which silicon ignores, and never clears it on `SPE` 1→0.
- **No test builds the history from a `CR1` write,** so removing that `NoteMasterState()` call would survive the suite. Unverified by a run.
- **`docs/work-orders/TVI-WO-0100a.rm0433.md`** describes the earlier head in two rows.
- **Solid (sub):** every citation at head; the check's JSON reader against duplicates, truncation, `(2)` suffixes, sequence gaps and embedded newlines; CI pinned by digest with no network for runs.

### On forging a record
I did not construct a forging firmware, because I found no route to try. In both builds a UART byte reaches a file backend or a line prefixed with the UART's own name, never a line whose source is the guarded peripheral. Renode writes a multi-line message's continuation lines indented and unstamped (sub, from the held source), which both parsers reject. The residue is any Renode log message that carries guest-controlled text; I did not search for one exhaustively, and no run was made.

### What Aethelgard's Phase 2 would change, crate by crate
- **`aethelgard-twin`:** emit guarded class names for added instances and whatever A3's spike allows for stock ones; hold the overlay manifest's hash beside `RENODE_FILES`; add expected witness lines per guarded instance.
- **`aethelgard-cli`:** copy and include the overlay files as it does its own models; parse records from the console; require one witness per guarded instance with the pinned version; turn a refusal into a named finding ahead of `renode_error_log`; the same in `renode/foreign.rs`; `doctor` reports the overlay.
- **`aethelgard-chaos`:** a new record kind for a refusal and one for the witness, a trace version bump, both hashes in the header's `artifacts`, and `check_run` binding the new verdict kind to its record (see my Aethelgard report, A8).
- **`aethelgard-evolve`:** a rule for a refusal. It is neither electrical nor software in the "cannot say" case; in the "silicon would fault" case it is a software attribution (B1).
- **`cli/src/ledger.rs` and `verify.rs`:** the join by register and field to the Tviburi ledger read by hash; the new class from A11; `gate_report` carrying the two hashes.
- **`container/`:** the overlay artifact pinned by sha256 in the tools image.
- **`aethelgard-ir`, `eda`, `svd-hal`, `hwdef`, `pincheck`:** nothing.

---

## Part B: ideas

| Id | Idea | Value | Cost | Risk |
|---|---|---|---|---|
| B1 | A `kind` on every refusal: `predicted-fault` or `unmodeled` | A consumer can attribute: the first is the firmware's defect, the second is the twin's gap. Today `TVI-SPI-MODF` carries both under one rule | S | A wrong `predicted-fault` is a confident wrong answer; require a page citation and a bench row for that kind |
| B2 | Model the fault where the manual is clear, and refuse only where it is not | `SSI` = 0 could set `MODF` and clear `SPE` and `MASTER`, as silicon does; the firmware then behaves as on the bench, and Aethelgard's existing judges see it | M | More model to get wrong; keep it behind B1's citation rule |
| B3 | The ledger as the allow-list (A5) | Makes invariant 1 enumerable, and gives `1100`'s join a precise right-hand side | M | More refusals at first; ArduPilot will meet them (A6) |
| B4 | Move Aethelgard's sensor models under the same contract | `BMP581.cs` and `ICM42688P.cs` have unmodeled registers too, and no refusal | M | Scope growth before Phase 1 closes; decide, do not drift |
| B5 | A recorded-mode run: log would-be refusals without failing | Lets Aethelgard run ArduPilot under the overlay and get the real queue from what flies, before any guard blocks it | S | Must be impossible to mistake for a pass: a distinct witness kind |
| B6 | One committed console capture from Aethelgard as Tviburi's parser fixture | Closes A1 without either repository importing the other | S | The capture goes stale when Aethelgard's Renode configuration changes; pin it by Aethelgard commit |

---

## Part C: top three, ranked

1. **Settle the three seam facts before `TVI-WO-1200` builds on them (A1, A2, A3).** The record grammar, the identity rule and the swap mechanism are each written as rulings, and each is either unmeasured or contradicted by how Aethelgard runs Renode today. All three are cheap now: one spike on replacing a stock instance, one amendment to `RUL-10`, one committed console fixture. Left alone, `1200` builds an identity scheme the consumer's sandbox defeats and Phase 1's gate passes against a parser nobody uses.

2. **Turn the guard into an allow-list, and give a refusal a kind (A5, B1, B3).** A deny-list guard restates the problem one layer down: it refuses what its author thought of. With the ledger as the list of what is modeled, and each refusal marked as a predicted silicon fault or as "cannot say", invariant 1 becomes something CI can enumerate, and Aethelgard can attribute a refusal to the right half.

3. **Build the clock tree first, measured from ArduPilot (A10, B5).** My opinion on `RUL-13`'s order: the first guarded family should be the RCC enables and kernel-clock selectors, then GPIO alternate-function routing, and only then timers and DMA. The evidence is from my Aethelgard review. Sentinel-03's acknowledged ledger rows are dominated by GPIO mode, alternate-function and pull fields and by `RCC` selectors. The missing SPI kernel clock was a real silicon defect the twin passed. And one clock rule covers every peripheral in both the generated firmware and ArduPilot, while motor-timer rules in the generated firmware cover a bring-up image that never flies. A GPIO routing guard built from DS12117's own table would also give Aethelgard the second source for alternate-function numbers that its Sentinel-01 review asked for.

---

## Coverage and limits

- **Read in full by me:** Tviburi's `README.md`, `CLAUDE.md`, `northstar.md`, `architecture.md`, `roadmap.md`, `TVI-WO-0100a.md`, `TVI-WO-1100.md`; PR #4's model, runtime, runner and harness; 0100a's model and runtime, and the first 120 lines of the reference check.
- **Read after drafting:** PR #4's `FEEDBACK.md`, which changed the status lines of A5 and A6.
- **Searched, not read in full:** `docs/reviews/2026-11-22-*` (the verdict, comparison, rubric and PR #2 review) and `TVI-WO-0100.md`. The brief asked for these in full. I searched them for each finding's terms and otherwise relied on `0100a`'s summary of the verdict.
- **Read by the sub-reviewer:** the RM0433 SPI chapter against every citation, the whole reference check, the tests and CI workflow, the held stock Renode SPI and RCC sources.
- **Not run:** any container. The ArduPilot write order comes from a hand-written Thumb decoder, not from source.
- **Both repositories are unchanged;** `git status --porcelain` is empty in each.
