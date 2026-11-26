# TVI-WO-1203 — FEEDBACK

**Builder:** Claude (Opus 5.5), Furnace, 2026-11-26. **Branch:** `wo/tvi-1203-gate-proofs`, from `main` `bd9dbd5`.
**Grounds:** `TVI-WO-1100a`'s audit (#59). **Evidence:** `~/.cache/tvi-1203/` (the release diffs `rel/`, the cases' outputs `suite/`, the mutants' logs, the replays `rep/` and `arm/`), kept until the architect says.

## 1. What was built

### (1) The manifest restates onto a guard only what a committed replay runs guarded (`tools/tviburi-release`)

**The class map's rows**
- **Every class row in `pins.tsv` now has five columns:** `class <stock type> <guard type> <stock instances> <consumer instances>`. Names are one space apart, `-` means none, and no name may appear twice; a row without the two columns refuses the release.
- **A stock instance must be one the pinned chain declares with that stock type,** so a typo refuses rather than becoming anything else. It is `restate`d, and the release refuses it unless a committed `model/stm32h7-*-restate.repl` declares it under that guard.
  - A declaration is a top-level `<var>: <Guard> @ …` entry with a bare-identifier variable, named by its `as "<instance>"` or by its variable.
  - Each line is read with its `//` comment removed.
  - A later `<var>: @none` cancels the entry.
  - A file holding a `/*` block comment refuses.

**How a consumer's declared instance qualifies** (WO (1)'s question)
- It goes in the fifth column, must not be declared by the pinned chain, and is `retype`d: a new MANIFEST line.
- It qualifies when the consumer's committed platform (the ArduPilot fixture CI boots, `tests/1301/fixtures/aet-1c32c8ea-ardupilot/`) declares it with the stock type, and the committed replay's own re-typing rule turns it into the guard.
- Today these are SPI's `spi1` and `spi2`, the only instances any replay runs on the SPI guard.

**A restatement qualifies only because a replay runs it**
- `manifest` requires every `model/stm32h7-*-restate.repl` to be loaded by `tests/1301/replay-1301.sh`: an uncommented `cat` of that file, either unconditional or under an `if [ -f … ]` naming that same file.
- It also requires each file to regenerate from its committed `model/stm32h7-*-restate.awk` and the pinned `stm32h7.repl`, byte for byte, which fixes its addresses and registrations to the stock entry's.
- This is how `1308` adds DMAMUX1 and PWR: a class row, a restate file and its generator, and a replay block (told on the bus at `01ab3a5`, and again for the five-column form).

**The records that move, and only these** (no stop)
- **MANIFEST:**
  - `restate SPI.STM32H7_SPI spi4` removed;
  - `restate Timers.STM32_Timer` removed for `timer2`–`timer7` and `timer12`–`timer17`;
  - `retype SPI.STM32H7_SPI spi1` and `spi2` added.
- **The ledger's model rows:**
  - SPI's row (`spi4`) loses `guarded_by`;
  - the timer row splits into `guarded` (`timer1` and `timer8`, with `guarded_by`) and `unguarded` (the other twelve).
- **The release hash:** `8e4a1f4b…` at `main`, `84590455…` at this PR's head. (5)'s enums and the source comments also move it.
- **No other committed record names those hashes or rows** (`git grep`), apart from `1100a`'s historical records.

**Proof**
- `manifest`: ten checks. The six planted ones:
  - `timer5` named;
  - `timer5` "restated" by every form Renode would not load as the guard (commented-out block and one line, a name only in a trailing comment, an entry cancelled after, a variable that is not one identifier);
  - a `/*` block;
  - a typo (`timer05`);
  - a duplicate;
  - (with the four positive checks: the release passes, every file regenerates, every file is loaded, every retype is the consumer's).
- The release's unit tests: 20, two of them new.
- **Mutants `MF01`–`MF08`, each killed:**
  - `MF07` by the unit tests;
  - the others by a planted case.

### (2) Clause 3's three cells, each a CI case in `tests/spike.sh`

- **`removed-console`:** a removed guard (spi1 stock).
  - At the runner: FAIL, no load witness.
  - At the records-only check reading a console captured under Aethelgard's committed `renode.config` (the fixture's: the virtual stamp, one space): CHECK FAIL, with spi1's the only missing witness.
  - So the check read that framing: the other guards' witnesses are found, and are framed as RUL-11 (a) says.
- **`crash-console`:** Renode SIGKILLed by the case once spi1's witness is on that console.
  - The case records that the witness was seen before the kill, so a 120 s expiry can't pass as a crash.
  - Exit 137.
  - The witness is read; no end record.
- **`crash-runner`:** Tviburi's runner, unchanged. A hook at spi1's first write kills Renode (`os.kill(getpid(), 9)`), after a logger flush.
  - The runner FAILs with `renode exit 137, after-marker=none`.
  - Its check finds the witnesses and no end record.
  - **Why the flush:** measured without it, the log's unflushed witnesses die with the process. The run then fails on "no load witness", which is a removed guard's reason, not a crash's.
  - **Also measured:** `System.Diagnostics.Process` is not reachable from the hook's IronPython. The first form ran its writes with a Python error each, then lost 29 of 31 end records at a clean quit: the teardown race, failing closed.
- **What the console's crash cell rests on, at the runner:** the console framing exists only in the consumer. The runner's own crash cell is `crash-runner`.

### (3) Clause 4, by what it claims

- **`stock-modf`:** stock Renode's run is the MODF firmware's, and it passes that path.
  - Its two writes are pinned: `CFG2` = MASTER|SSM, then `CR1` = SPE with SSI = 0, which is p2184's trigger.
  - It reaches the end marker `0xAF7E0002` with Renode exit 0.
  - Its only problems are spi1's missing witness and end.
- **`modf-page`:** checks the MODF refusal against `tests/1203/rm0433-rev9-p2184.txt`, which holds the held text's own lines (sha256 `8e02c0c0…`, line numbers named): the §50.5.2 heading (p2181), the paragraph and p2184's footer.
  - Checked: its citation (§50.5.2 p2184), its trigger ("SSI bit in slave select software mode) pulled low"), and its three effects (MODF set, SPE forced to 0, MASTER cleared).
  - `tests/1203/rm0433-p2184.sh` regenerates the excerpt and compares it with the held text where that text exists (Furnace: match).
  - **CI cannot reach the held text,** so in CI the excerpt is what is held. A finding, not a fix (§5 item 4).

### (4) Clause 6's 20 rows, each refused by a case matched to its own write

- **`i2c-isrmon`:** `ISR.TXE`, `ISR.TXIS`.
- **`adc-fieldsmon`:** `SQ1`–`SQ16`, `AWD1CH`, `PRESC`.
  - Each takes its smallest disallowed value from the guard's own arrays.
  - The arrays are first held to the manual's sets (a channel 0–19, p1012 and pp1023–1026; `PRESC` 0000–1011, p1039), so a widened or narrowed set fails the case.
- Both are monitor writes (`pc: none`), as their names say, recorded. Each record is a `would-refuse` naming the field its write set, in order, matched by value. The sweeps' count-only check (`1100a` D1-3) is not repeated.
- **No row needed a rule change:** both guards refused all 20 as they stand.

### (5) The access log names every RCC and DMA guard register

- **RCC:** the stock `STM32H7_RCC`'s own `Registers` enum, copied by reflection from the pinned build, plus `CSICalibration` (`RCC_CSICFGR`, which the stock enum leaves unnamed).
- **DMA:** the stock `STM32DMA`'s (stream 0), plus streams 1–7 in its pattern (`Stream1Configuration`, …). Stock logged stream 0's name with `+offset` there.
- **`lognames`:** 126 offsets are read by the monitor with access logging, namely every `Fields` offset and every offset the enums name (the RCC's `C1_` aliases). Each is logged by its enum's name, none `unknown`.
- The PWR stub's names come with `1308`'s guard.

## 2. Acceptance

| Criterion | Where |
|---|---|
| The manifest check refuses a planted restatement; `main`'s manifest passes after (1) | `manifest` (§1 (1)); `MF01`–`MF08` killed |
| Every clause-3 cell, the clause-4 halves and the 20 clause-6 rows are CI cases, run in a shard and named in the aggregate | `removed-console`, `crash-console`, `crash-runner`, `stock-modf`, `modf-page`, `i2c-isrmon`, `adc-fieldsmon`; CI_AGGREGATE |
| The 14 boots, the fixture and the armed run: no new record, and the armed run arms | §1.1 below |
| CI green, the budget judged; rows re-weighed from this run | CI_RUN |

### 1.1 The replays

- **The 14 generated boots** (`1305`'s captures at Aethelgard `1cb994c1`) **and ArduPilot's 5 s fixture** (sentinel-02-own), each replayed by the committed `tests/1301/replay-1301.sh` on `main`'s tree and on this branch's (`8e44b68`, whose `model/` equals the head's), every guard recorded:
  - zero records on both;
  - equal witnesses (30–32);
  - a byte-identical boot block, all 15 pairs (`rep/loop.log`).
- **The armed run** (`1305`'s capture of Aethelgard `4400h` `s03-arm-s`, via `spike/1100a/harness/replay-armed.sh`) on the branch:
  - arms: `COMMAND_ACK` 400 at 9.003 s, armed from 9.332 s, as on `main`;
  - zero records;
  - 32 witnesses and 32 ends on the branch's release.

## 3. Adversarial pass

A fresh subagent (Opus) took the three attacks, read-only. It found real breaks; each is fixed above unless noted.

**A case passing for another reason**
- `stock-modf` would have passed on any firmware reaching the marker. Now its writes and its problems are pinned.
- `adc-fieldsmon` took its probes from the guard under test. Its arrays are now held to the manual.
- The excerpt's header said p2183 for the section heading (it is p2181).
- `lognames` read only the `Fields` offsets.
- The block-form commented plant could not reach the check it was meant to kill.

**An instance restated through a form the check doesn't parse**
- Both parsers accepted:
  - a `/*` block;
  - a trailing `// as "x"`;
  - an entry cancelled by `@none`.
- No check compared a restate file with its generator: the timer, GPIO and RCC files are not regenerated by their own cases.
- "Loaded" only matched the file's name anywhere in the replay.
- **A regression:** a typo in a class row became a `retype` instead of refusing.
- **All fixed:** the two instance columns; comment stripping, block refusal and cancellation in both parsers; every file regenerated; `cat` under a matching test; planted cases; mutants `MF03`–`MF08`. The Python re-derivation is now a line parser with the same rules, so the two agree.

**A crash that times out, read as a pass**
- `crash-console`'s 137 did not exclude a 120 s expiry (the loop kills either way). Now the case records that the witness was seen before the kill.
- `crash-runner` now requires `after-marker=none`.

**Not fixed, named**
- An OOM kill after the witness would also satisfy both crash checks. It would still be a real crash, so the cell holds; only the wording "killed by the case" would not.
- CI asserts at least one witness per boot, not one per restated instance (§5 item 3).

**Tried and not broken**
- Every case fails safe when its regex matches nothing.
- The console cases prove the check read Aethelgard's framing.
- `isrmon`'s and `fieldsmon`'s records match their writes by value and order.
- The excerpt is p2184's own text.
- Rust and Python agree on all eight committed restate files.
- The MANIFEST's 30 restates equal the declared set.

## 4. For the architect: clauses 1 and 6's "a planted firmware" (wording only, not built)

**Measured.** Every clause-6 sweep, and the new cases, write by the monitor (`pc: none`), not by firmware. No guard's rule reads the PC: only `model/Refusal.cs` does (L75–78), to put it in the record. So a monitor write exercises the same rule a firmware store would, and its record says which kind of write it was.

**Proposed reading,** for a steering-document PR of yours:
- **Clause 6:** "For each guarded peripheral, **a planted write** gives each field outside its allow-list a value the model does not implement, and it refuses with `kind: unmodeled`, naming that field. A planted write is a store by planted firmware, or a monitor write where the rule reads only the value written; the record's `pc` says which."
- **Clause 1:** "A planted firmware that hits each path shows it, **or a planted monitor write, for a path whose rule reads only the value written.** A path whose rule depends on CPU state, order or timing keeps a planted firmware (an enable sequence, a refusal by history, a NACK's time)."

**What it costs and rules out.** None of today's cases change. It rules out a monitor write standing in for a firmware path that needs the CPU, such as `cfg2first`'s history or `1306a`'s NACK timing.

## 5. Findings and rows

1. **A recorded `would-refuse` stands for "refuses" in every clause-6 sweep,** this PR's included. That is the existing precedent (recorded mode lets one run test many fields), but the gate's word is "refuses". The reading in §4 could say "refuses, or in recorded mode records a `would-refuse`".
2. **The timer, GPIO and RCC restate files had no regeneration case of their own.** `manifest` now regenerates all eight. Each guard's own `restate` case (DMA, flash, USART, I2C, ADC) duplicates that for its file; a reduction for the next gate.
3. **No CI check asserts a witness per restated instance.** The budget counts at least one per boot. The replays here show 30–32, which fits every restated instance witnessing (30 plus the SPI ports each platform declares), but nothing pins it. A row: a per-instance witness check over the fixture boot.
4. **The held RM0433 text cannot reach CI.** `modf-page` checks the committed excerpt, and only `tests/1203/rm0433-p2184.sh`, run where the text is held, ties the excerpt to it. A row if the architect wants it run on a schedule.
5. **`crash-runner`'s first form showed the teardown race at a clean quit:** 29 of 31 end records lost. It failed closed. The existing row stands.
6. **The DMA guard's access-log names for streams 1–7 differ from stock's** (`Stream1Configuration` where stock said `StreamFIFOControl+0x4`). No Tviburi test or replay read them. A consumer that compares stock and guarded access logs line for line would see it.

## 6. Reduction pass

Run twice, before and after the adversarial corrections:
- **0 lines net:** two expressions in the release program shortened.
- **1 dead branch removed:** `stock_rows`' default for a class with no instance list, which can no longer occur.
- **1 redundant check removed:** `declared_guarded`'s separate comment skip, which the bare-identifier check (and later the comment stripping) covered.
- **1 protected kept and labelled:** `t_manifest`'s independent re-derivation, `belt-and-suspenders:`.
- **The ledger rows were byte-identical after each removal.**
- **1 finding:** §5 item 2.

## 7. Gates

- **Order:** (1) first, pushed at `01ab3a5` and told on the bus; then (4), (2)/(3), (5); then the adversarial pass, the reduction pass, and this file.
- **Containers:** one at a time, `~/bin/capped-run`, `nice -n 10`, `--network none`, `free -g` checked before heavy runs.
- **Local runs on the final tree:**
  - `manifest`, `unit`, `stock-modf`, `modf-page`, `removed-console`, `crash-console`, `crash-runner`, `lognames`, `i2c-isrmon` and `adc-fieldsmon` pass;
  - `MF01`–`MF08` killed.
- **Route:** `tests/ci/route.sh origin/main` passes (inventory 0 failures, `class=code`, `budget=yes`). Re-run after this file is committed.
- **CI:** CI_RUN.
