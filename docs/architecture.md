# Tviburi — architecture

**Status:** ratified by the merge of the 2026-11-23 rework (see §6); first drafted 2026-11-21. **Supersedes nothing.** Read after `northstar.md`.
**Authors:** Grok (seed, 2026-11-21); Claude, co-architect (rework, 2026-11-23) · **Gate:** Scotty · **Builder:** a separate build session.
**Numbering:** the seed's `RUL-01`…`RUL-06` and `OQ-01`…`OQ-03` keep their numbers; `TVI-WO-0100` and the reviews cite them. New rulings continue from `RUL-07`. From outside this repository, cite them as `TVI-RUL-nn`, because Aethelgard has its own `RUL-` series.

---

## 1. What it is

**Tviburi is an overlay on Renode's STM32H7 platform.** It has two parts:
- **guarded models:** subclasses of the stock peripheral models whose unmodeled paths refuse by name (northstar invariant 1);
- **a versioned gap ledger:** what the model build does and does not model.

Both are released as one hashed artifact, which a consumer pins and loads into its own Renode.

The ownership boundary is deliberate:
- **Tviburi owns** the models and the statement of what they do not yet model.
- **Aethelgard, or any other consumer, owns** the specification-to-board-and-firmware path, its platform description, its trace, and the gates that judge the pair.

**The interface is not a trace** (corrected in §2.1). Three things cross it:
- the overlay artifact, pinned by sha256;
- the records the guarded models emit during a run: a load witness and refusals;
- the ledger, read by its hash.

## 2. Ground truth measured

### 2.0 What the consumer does today

Read at Aethelgard `main` `f42a0c88` on 2026-11-23. Paths are in that repository.

**How Renode is run:**
- **Only the CLI runs Renode** (`crates/aethelgard-cli/src/main.rs` `simulate`, `src/renode.rs`, `src/renode/foreign.rs`). `campaign` and `verify` reuse that path. `aethelgard-twin` never runs Renode; it emits the `.repl` and holds the judge's expectations.
- **The build is pinned.** It is Renode 1.17.0's portable release, checked by sha256, with `Infrastructure.dll` and `Renode.dll` rebuilt with three upstream NVIC patches. The build string is `1.17.0+20260907gitf1dd1b4af.aet1605` (`twin/src/platform.rs` L25, L33). The CLI refuses any other version or build by name (`main.rs` L1871-1890). The stock `.repl` include chain is pinned by sha256 (`platform.rs` L36-49).
- **The sandbox:** Landlock, seccomp (no sockets), dropped capabilities, uid 65534, and an empty environment (`run.rs` L1-19, `main.rs` L1840-1862). A boot is bounded at 30 s wall and 1 GiB.

**What it loads and reads:**
- **The platform is Aethelgard's,** emitted from its solved topology, and it includes stock `platforms/cpus/stm32h753.repl`.
- **Aethelgard already compiles its own C# models into Renode** (`models/renode/BMP581.cs`, `ICM42688P.cs`), pinned by sha256 (`twin/src/model.rs` L62-73). An overlay of C# models is therefore a shape it already consumes.
- **The verdict comes from parsing Renode's console:** device models' log lines, the access log, sentinel lines and monitor-Python hooks (`renode.rs`). **Renode exits 0 even when a load fails**, so a clean load is judged from the console: a CPU must appear in the `peripherals` listing, with no error line (`renode.rs` L1-50).
- **Aethelgard writes its own trace,** deterministic CBOR at version 18 (`chaos/src/event.rs` L58), from those records. Its header has `artifacts` and `tools` lists, each `[name, sha256 or version]`.

**What it already knows it cannot judge:**
- **A witness ledger already exists on the consumer's side** (`cli/src/ledger.rs`; Aethelgard `RUL-20`). It has one row per register field the generated firmware writes or leaves at reset. Each row is classed:
  - judged: `independent-rule` or `observed`;
  - not judged: `same-source`, `ignored-by-model` or `silicon-only`.
  Each `ignored-by-model` row cites the Renode source line that drops the field. A fabrication package needs every row of the second kind acknowledged by name.
- **Recorded Renode gaps,** cited in Aethelgard's architecture and `ledger.rs`:
  - SPI `MODF`, `CFG2.MASTER` and `SSM`;
  - I2C error flags, NACK and clock synchronisation;
  - timer `MOE` and `CCxP` tags, the period off by one, a fixed 250 MHz clock;
  - DMA enables as tags, `DMAR` undefined, and DShot frames unwitnessable;
  - BOR level, silicon-only;
  - every `RCC_D2CCIP1R` field, `SPI123SEL` included, reading 0;
  - USB not in RCC, and the I/O compensation cell.

### 2.0a What the first Tviburi build measured

`TVI-WO-0100`'s blind build (`claude/tvi-wo-0100-blind-build`, FEEDBACK §1–§2), in the same pinned image:
- **No Renode source is needed.** The portable release ships `nucleo_h753zi.repl` and its include chain, and it compiles C# included with `include @file.cs` offline (Roslyn is bundled).
- **The stock SPI model cannot be overridden by method.** Every `Read*`/`Write*` in `STM32H7_SPI` is `virtual final`.
- **Bus hooks fail as a guard:** they need one hook per access width, they cannot be set from the `.repl`, and one monitor command silently replaces them.
- **What works is a subclass that re-implements the bus access interfaces.** Renode dispatches all three widths to it, and a monitor hook does not displace it.
- **Stock tags discard writes and read 0,** so a guard must keep its own copy of the bits its rule needs.
- **A refused write cannot halt the CPU at that instruction:** the halt lands at the end of the translation block.
- **Mid-access, `ElapsedVirtualTime` reads 0;** the PC is exact.

### 2.1 Corrections: what the seed assumed, against what was measured

| Assumed (seed, 2026-11-21) | Measured | Why it matters |
|---|---|---|
| "The interface is the same one Aethelgard already uses with Renode: a sandboxed external tool that produces a replayable trace" | Aethelgard never consumes a Renode trace. It runs Renode itself, with its own platform and models, parses console records, and writes its own CBOR trace (§2.0) | A Tviburi trace format would have no consumer. The seam is models in, records out (`RUL-09`, `RUL-11`) |
| "The only shared artifacts are the trace and the hash of the gap ledger" (README) | The shared artifacts are the overlay, the load witness and refusal records, and the ledger | As above |
| A refusal can be seen from the simulator's exit status | Renode exits 0 even on a failed load. Both `TVI-WO-0100` builds had a path where a refusal was printed and the process exited 0 (verdict R9) | Northstar invariant 8; `RUL-12` |
| "Renode 1.17.0" | The consumer runs a patched build, `…aet1605`, and refuses any other | Tviburi must run and test on the consumer's exact build (`RUL-09`) |
| A refusal check can wrap the stock model's write method | The methods are `virtual final`; bus hooks are displaceable (§2.0a) | `RUL-07` |
| The model can stop at the refused access | It stops at the end of the translation block | `RUL-08`: the record is the verdict, not the halt |
| Virtual time locates an access | It is quantum-granular mid-access; the PC is exact | Northstar invariant 5, amended |
| The gap ledger is a new idea | The consumer already has a per-field witness ledger whose `ignored-by-model` rows are Tviburi's work queue (§2.0) | `RUL-13`: build order comes from the consumer's ledger |
| Every modelled side-effect can cite the manual (invariant 2 as seeded) | Inherited Renode models cite nothing | Northstar invariant 2, amended |

### 2.2 Held references

- **ST RM0433 Rev 9:** sha256 `672a9433…`.
- **DS12117 Rev 10:** sha256 `607b9ead…`.

Both are held on Furnace (Aethelgard's review of 2026-11-15). Every citation names the revision and page.

There is no governing standard to trace against. The reference manual is the standard each model is checked against (northstar invariant 2).

## 3. Rulings

| ID | Ruling | Reason |
|---|---|---|
| RUL-01 | Start from a Renode fork of the STM32H7 platform, not a clean-room simulator. **Amended 2026-11-23:** "fork" means an overlay of guarded subclasses and a `.repl` fragment on the stock platform; Renode's source is neither vendored nor forked (`RUL-09`). | The CPU, bus, and virtual-time engine are the expensive parts. The gaps are in fidelity and honesty, not in the existence of a simulator. The overlay was measured to need no Renode source (§2.0a). |
| RUL-02 | Unmodeled paths refuse; they do not pass. | This is northstar invariant 1. Silent success is the defect the project exists to remove. |
| RUL-03 | The gap ledger is emitted by the twin as a versioned artifact. Consumers store the hash. **Format ruled 2026-11-23 (`OQ-01`): canonical JSON Lines.** One entry per line, sorted by (peripheral class, register, field); UTF-8, LF, no trailing whitespace; a first line naming the schema version and the model hash. The hash is the sha256 of the bytes. | The ledger is a property of the model version, so copying it into every campaign would drift. **Why JSONL over CBOR:** the ledger is reviewed in pull requests, so it must diff line by line. It is emitted from C# and read from Rust, and both read strict JSON without a canonical-CBOR implementation. Aethelgard's specification and scenario readers already refuse malformed JSON by name. |
| RUL-04 | First peripherals for explicit refusal work: advanced-control timers, DMA (memory-to-peripheral and circular), brown-out / voltage detector. **Amended 2026-11-23:** the order within and beyond these comes from `RUL-13`, and the RCC kernel-clock selectors join them. | These cover the majority of safety-critical "twin passed, silicon would have faulted" surface once simple polled SPI/I2C is left behind. **Why the RCC selectors:** the generated firmware's SPI1 had no kernel clock on silicon, and the twin passed (Aethelgard's second Mythos review, A1). |
| RUL-05 | Stay in Renode's C# model shape for the first usable version. | Re-expressing models in another language is a later auditability step, not a prerequisite for the refusal contract. C# is also the only language Renode compiles at load. |
| RUL-06 | Work-order prefix is `TVI`. | Distinct from Aethelgard's `AET`. Ids must not collide across the two repos. |
| RUL-07 | **A guard is a subclass of the stock model that re-implements the bus access interfaces** (`IDoubleWordPeripheral`, `IWordPeripheral`, `IBytePeripheral`), checks its rule, and calls the base for a modeled access. It keeps its own copy of any bits the stock model discards. Bus hooks are not used as guards. | Measured in `TVI-WO-0100` (§2.0a). Method overrides are impossible. A bus hook misses other widths and is silently displaced by one monitor command, so a guard built on one can be disarmed without a trace. |
| RUL-08 | **The refusal record is the verdict, and it latches.** After the first refusal, nothing the run does is claimed. The CPU halt is best-effort. | The halt lands at the end of the translation block (§2.0a), so later stores may still run. A verdict that rested on the halt would claim what it cannot show. |
| RUL-09 | **Delivery is a released overlay artifact, pinned by sha256** (`OQ-02` closed). It is a tarball of the guarded models, the `.repl` fragment that swaps stock peripherals for guarded ones, the ledger, and a manifest of every file's sha256. Renode itself comes from the consumer's pinned build. Tviburi's CI and tests run on exactly that build string, and the manifest names it. | Neither vendoring nor a submodule is needed, because the overlay needs no Renode source. Aethelgard already pins its own C# models by hash and refuses any other Renode build, so this is a shape it consumes today. Testing on any other build would test a different simulator. |
| RUL-10 | **The model's identity** (northstar invariant 3) is the sha256 of the overlay manifest, and the manifest names the Renode build string. The guarded models compute it from the bytes they were compiled from, at load, never from a name or an environment variable. | The blind verdict's run Y5b showed a hash that followed an environment variable, not the compiled shim. |
| RUL-11 | **Records travel on a channel the firmware cannot write.** Every load witness and refusal is emitted through Renode's logger under the guarded peripheral's own name, as one JSON object on one line. The consumer accepts it only from that source, never from a UART or semihosting stream. | The consumer judges from console text. Firmware under test can print anything to a UART. A forged refusal only fails a run. A forged load witness could make a stock, unguarded run look guarded, which is a silent pass. |
| RUL-12 | **Fail closed at the consumer** (northstar invariant 8). Each guarded model emits a load witness carrying the model hash when it is constructed. A consumer that pinned the overlay must refuse a run with no witness, or with a different hash, and must count any refusal record as a fail. Tviburi's own runner, which is used for Tviburi's tests, exits 3 on a refusal and nonzero on a missing witness. | Renode's exit status says nothing (§2.0). The consumer's gate reads records, so fail-closed has to be stated as a records contract that the consumer enforces, and that Tviburi's tests check from the consumer's side. |
| RUL-13 | **Build order comes from the consumer's coverage.** The next guarded path is chosen from Aethelgard's witness-ledger rows classed `ignored-by-model` or `same-source` for its committed boards' generated firmware. They are ordered by what flies first: motor timers, DShot DMA, the kernel clocks, BOR, then the rest. Each pick is cited to the ledger row it answers. | The seed's fixed list was a good guess, made without the data. The consumer already lists, per register field, what its firmware writes and the stock model drops (§2.0). Ordering by that list spends model work where a false pass would reach a flying board. |
| RUL-14 | **Languages:** C# for models (`RUL-05`). Rust for any program larger than a thin harness (a ledger checker, a runner). POSIX shell only for harnesses that call those. | Rust matches the consumer and the pinned tools image, which already carries the toolchain. A program in a third language would be one more thing to pin and audit. |
| RUL-15 | **CI runs on GitHub-hosted runners, in the consumer's pinned tools image, with no network for the run itself.** Every test that claims a refusal or a pass runs there on every pull request. | Reproducibility from a clean checkout was unmet by both `TVI-WO-0100` builds' reviewers until a pinned image was used (review of PR #2, item 8). Self-hosted runners would make Furnace part of the trusted path. |

## 4. Seams

- **Model vs consumer: a security boundary.**
  - Tviburi does not import Aethelgard crates, and Aethelgard does not import Tviburi models as source.
  - The consumer loads the overlay by pinned hash, and Tviburi's C# then runs inside the consumer's sandbox (§4.1).
  - What crosses is `RUL-09`'s artifact, `RUL-11`'s records, and the ledger read by hash (northstar invariant 7).
- **Firmware vs records: a security boundary.** The firmware under test is untrusted input, and it must not be able to write a record (`RUL-11`).
- **Renode host vs model.** The refusal logic lives in the guarded models. It requires no change to Renode's CPU or bus. **This is not a security boundary:** the models run in Renode's process with Renode's rights.
- **Gap ledger vs run records.** The ledger is emitted once per model build. Records are emitted per run. They are separate artifacts. **Not a security boundary.**
- **Tviburi's ledger vs Aethelgard's witness ledger.** They are the same idea from two sides (Aethelgard's review response of 2026-11-21, §3):
  - the consumer's says, per field its firmware writes, whether anything judged it;
  - Tviburi's says, per field the model knows, whether it is modeled, refused or inherited.
  - **The join is the consumer's,** keyed by register and field. Tviburi never reads the consumer's ledger, and the consumer never restates Tviburi's.

### 4.1 Security posture

- **No credential, no network** (northstar invariant 4). The overlay is data and C# source. It is compiled offline at load, inside the consumer's sandbox (§2.0: Landlock, seccomp with no sockets, uid 65534, an empty environment).
- **The overlay is code the consumer executes.** It is trusted only by hash (`RUL-09`). A release is a reviewed, merged commit, and its tarball is reproducible from that commit.
- **Firmware is untrusted** (`RUL-11`).
- **Tviburi's own CI** holds no secret beyond what GitHub grants a workflow by default. Publishing a release artifact is the only write.

## 5. Open questions

| ID | Question | Decider | Blocks |
|---|---|---|---|
| OQ-01 | Exact on-disk format of the gap ledger. | Scotty | **Closed 2026-11-23 by `RUL-03`'s amendment.** The field names are set by `TVI-WO-1200` |
| OQ-02 | Whether the first fork vendors Renode or uses it as a submodule. | Scotty | **Closed 2026-11-23 by `RUL-09`:** neither |
| OQ-03 | Minimum viable set of timer/DMA/BOR flags that must refuse before the first silicon re-check. | Scotty + builder | Phase 1's exit gate. **To be answered from data** by `TVI-WO-1100`'s coverage map (`RUL-13`) |
| OQ-04 | Does a Tviburi-authored, cited model that reacts to a field count as "the twin's model reacts to it" under Aethelgard's `RUL-20` (2)? If yes, a ledger row can move from `ignored-by-model` to `observed`. | Aethelgard's architect, ratified by Scotty | Phase 4. **Prior, held loosely:** yes, but only for a field whose Tviburi model cites RM0433 and has passed a bench scar or an independent re-derivation. A model that shares its reading of the manual with the generator would be `same-source` (Aethelgard `INV-07`) |
| OQ-05 | The licence for this repository. Nothing is licensed today, and a consumer cannot pin and redistribute the overlay in a published image without a licence. | Scotty | Phase 2's first release. **Prior:** MIT OR Apache-2.0, as Aethelgard, which Renode's MIT licence permits |

## 6. Amendments

| Date | Section | Change | Rationale |
|---|---|---|---|
| 2026-11-21 | — | Initial version | Seed |
| 2026-11-23 | §1, §2, §2.0–§2.2, §3, §4, §4.1, §5 | Rework (repo-genesis): ground truth measured from Aethelgard `f42a0c88` and `TVI-WO-0100`'s blind build; a corrections table; `RUL-01`, `03` and `04` amended; `RUL-07`…`RUL-15` added; `OQ-01` and `OQ-02` closed; `OQ-04` and `OQ-05` opened; security posture added | Scotty, 2026-11-23: a solid foundation for Aethelgard to consume Tviburi's output. The seed's seam (a shared trace) had no consumer on the other side (§2.1) |

## 7. Provenance

- **Seed:** written 2026-11-21 by Grok. It rests on the 2026-11-21 conversation and on Aethelgard's published measurements of Renode. No Tviburi code existed.
- **Rework:** 2026-11-23, by Claude as co-architect. It rests on:
  - a read of Aethelgard `main` `f42a0c88` (cited in §2.0);
  - `TVI-WO-0100`'s blind build and its FEEDBACK;
  - the blind verdict and the review of PR #2 (`docs/reviews/2026-11-22-*`);
  - Aethelgard's review response of 2026-11-21, §3.
