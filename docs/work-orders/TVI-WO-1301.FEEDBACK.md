# TVI-WO-1301 — FEEDBACK

**Builder:** Claude (Opus 5.5), Furnace, 2026-11-23. **Branch:** `wo/tvi-1301-gpio-af-guard`, from `main` `604c3d6`, with `main` `2e4c56d` (docs only) merged.

**State:**
- §1.1 measured and stopped (16:59Z). The architect ruled at 17:52Z (both stops, the form, A1–A10, D1–D5, records, AFRH), 18:30Z (the memory: judged in CI) and 19:08Z (Q1–Q3). The build followed.
- **No §7 stop is open:**
  - no valid path refuses (both firmwares, all 15 boots);
  - the package is declared;
  - the memory is judged in CI per 18:30Z, and passes locally as a proxy (§3).
- **Open with the architect:** F1 and F5 (§5), asked at 19:23Z.
- Steering documents, `README.md`, `docs/reviews/` and Aethelgard are untouched. `model/Refusal.cs` changed only as granted at 19:08Z (Q1). One PR, left unmerged.

---

## 1. What was measured (WO §1.1; the evidence is `TVI-WO-1301.rm0433.md` §1–§8 and `TVI-WO-1301.ds12117.md`)

- **DS12117 Rev 10 Table 9** (pp89–100): 168 pins × 16 AFs. Two independent transcriptions are byte-identical:
  - mechanical, from the page's ruled grid;
  - visual, by an agent that read only the page images.
  The table was committed alone, before any comparison.
- **ST's open pin data** (Aethelgard `1c32c8ea`, ST `7d1f1514`): 0 AF-number conflicts. Disagreements D1–D5 were ruled; DS12117 is the authority.
- **Renode's 102 stock timer AF routes** all agree with the table.
- **The field table:** every GPIO access of ArduPilot's full 5 s boot and 14 generated boots, with pages and the stock port's behaviour.
  - Both firmwares select AFSEL before MODER.
  - Neither touches a port with its clock off.
  - Neither writes LCKR.
  - ArduPilot writes BSRR as 16-bit halves.
- **The stock port is not sealed.** The restatement was measured equal to the stock board by behaviour and by reflection.

## 2. What was built

| Part | File |
|---|---|
| The guard (`RUL-07`'s subclass; rules TVI-GPIO-CONFIG, -WIDTH, -READ, -FIELD, -AF, -PACKAGE; the query `WhichSignal`) | `model/GuardedSTM32_GPIOPort.cs` |
| The table and LQFP144's bonding as data, generated into the guard by `tools/tviburi-afcheck csharp` | `model/ds12117-rev10-table9.tsv`, `model/ds12117-rev10-lqfp144.tsv` |
| The restatement (11 ports, 22 connection blocks, silicon's resets) | `model/stm32h7-gpio-restate.{awk,repl}` |
| The board: each port `enforcing`, `package: "LQFP144"` (cited); the Nucleo's LEDs and button restated | `model/tviburi-nucleo-h753zi.repl` |
| Records' optional witness fields, `package` the first (granted 19:08Z) | `model/Refusal.cs` (+8 −2) |
| The ST comparison as a CI test (Rust, as ruled), ST's files with their BSD-3 notice | `tools/tviburi-afcheck/`, `tests/1301/fixtures/st-open-pin-data/`, `tests/1301/st-{ruled,aliases}.tsv` |
| Planted firmware: 20 paths | `firmware/gpio.rs` |
| 32 cases, each `gpio-<case>` in the acceptance group; gate clause 6 (25 values) | `tests/1301/gpio.sh`, `tests/1301/fields.resc` |
| The memory check in CI (ruled 18:30Z, 19:08Z) | `tests/1301/ci-memory.sh`, `tests/1301/fixtures/aet-1c32c8ea-ardupilot/`, `.github/workflows/ci.yml` (+6) |
| Bench rows G1 (TVI-GPIO-AF), G2 (TVI-GPIO-PACKAGE); class row; mutants G01–G24 | `model/bench-checks.tsv`, `tools/tviburi-release/pins.tsv`, `tests/mutants.tsv` |
| Every check call names the 11 ports (a record from an unnamed source fails a run); the runner's names line granted 18:30Z | `scripts/tviburi-spike`, `tests/spike.sh`, `tests/1300/rcc.sh` |

**`tools/tviburi-check`:** one unit test added (Q1's proof). **`tools/tviburi-release`:** untouched. Q2 was granted, but no row needs an `(A<n>)` reason: the acknowledged rows use the class "inherited, unaudited (acknowledged by name)".

## 3. Acceptance (WO §2)

| Clause | Where shown | Result |
|---|---|---|
| ArduPilot's full 5 s boot, recorded, with the guard: no `would-refuse` under any GPIO port | `replay-1301.sh accept` on the fresh capture | **0**; 11 recorded witnesses and 11 ends; the boot reached its end; its boot block differs from stock only by 1300's `AHB2ENR` |
| …the one-file peak under 1,048,576 KiB, with the runs and the margin | CI: `tests/1301/ci-memory.sh`, 5 runs, each ≤ 1,015,808 KiB (32 MiB spare, ruled 18:30Z). The ELF is built from Aethelgard's pinned image and held to `a799dfb6` | **Furnace, as a proxy (host load 10–13):** 951,832; 971,916; 1,008,376; 974,744; 985,060 KiB, all under the limit; ELF `a799dfb6` reproduced byte for byte. **CI's numbers are the PR's** |
| The generated firmware's GPIO sequences pass, enforcing | the 14 boots replayed, ports enforcing; `gpio-ardupilot`, `gpio-gen-s03` (planted) | 0 refusals; every boot-block difference is silicon's reset (A5, ruled expected); motor observers identical to stock |
| A planted AF with no function refuses `predicted-fault`, naming the pin, the AF and the page; the table's AF passes | `gpio-blank`, `-blankfirst`, `-afterwards`, `-afrh`, `-pk8af`; `gpio-valid` | as stated |
| Every field outside the allow-list refuses `unmodeled` (gate clause 6) | `gpio-fields` (25), `gpio-pupdr`, `-lckr`, `-brr`, `-read`, `-misaligned` | all refuse by name |
| The table and ST's open pin data agree, or each disagreement is ruled | `afcheck` | D1–D5 ruled; the check fails on anything else |
| CI green | the PR | see the PR |

**Mutants:** G01–G24, each killed by its named case (`tests/mutants.sh` on exports of `3ec764c` and `025b6e4`).

**Suites on `bfc1594`:** see §7.

## 4. The adversarial pass (WO §5)

There were two passes:
- **Mine,** through the build.
- **An independent one,** by a fresh reviewer given the code, the stock source and the manuals, not my conclusions.

Each finding was verified in the code or the manual before acting.

| WO §5 item | Result |
|---|---|
| A sub-word write to AFRH | merged into its word and judged (`gpio-afrh`) |
| A BSRR write that changes ODR | BS priority modeled (p544; the stock port gives BR priority: `gpio-bsrr`); 16-bit halves (`gpio-bsrrhalf`). **But see F1** |
| LCKR written out of sequence | any write starting the key sequence refuses (`gpio-lckr`). LCKR = 0 passes: it starts no sequence and reads 0, as silicon (F9) |
| AF mode before AFSEL, and the reverse | both refuse when the state is a blank cell (`gpio-blankfirst`, `-afterwards`). **But see F5** |
| A table row whose page does not say it; a blank cell read as AF0 | the reviewer found every pin's 16 cells verbatim on its stated page, all 168; two independent transcriptions; `afcheck` tests the guard's constant against the file |
| A path that bypasses the guard: a second address | refuses (`gpio-twoaddr`, F10) |
| …a registration left on a cancelled instance | the cancelled ports' connections remain only as inert `CombinedInput` inputs; every destination equals stock (`gpio-registrations`; rm0433.md §4) |
| …a monitor command | the monitor's own `WriteDoubleWord` on the port reaches the guard (`gpio-adv-monitor`) |
| A record whose text the firmware controls | none: records carry fixed text and hex values; a firmware imitation of a witness is not a record (the check's unit test) |

**Found and fixed in the build:**
- **(mine)** PK8–PK15, pins no package has, passed silently in AF mode or as output. Port K is PK0–PK7 (Table 9 p100).
- **(F2, independent)** After any reset, a write of `00` to MODER, OSPEEDR or PUPDR was dropped. Renode's register calls a field's change handler only where a write differs from its cached value, which reset leaves at 0, while A5's silicon resets are non-zero. The guard now refreshes the cache before writing.
  - **This also corrects §1.1:** ArduPilot's `PUPDR` A boot-block change was this artefact, not silicon's reset carried (rm0433.md §2, §9).
- **(F3)** Recorded mode recorded only the first pin of the first rule, and latched unrecorded faults. It now records every pin and rule, and latches only what it recorded.
- **(F10)** A second bus address now refuses.

## 5. Findings and questions for the architect (asked 19:23Z)

1. **F1: the stock port drives a pin's connections from ODR in every mode.** That covers LEDs, EXTI, the timers' inputs, and IDR. Silicon drives a pin from ODR only in output mode:
   - p537 (input): "The output buffer is disabled";
   - p538 (AF): "driven by the signals coming from the peripheral";
   - p539 (analog).
   Example: after reset, BSRR = 1 on GPIOB lights GreenLED with PB0 analog. An ODR write on a timer-AF pin would override the timer's level at the consumer's motor observers.
   **Refusing the write would refuse a valid path:** ArduPilot writes ODR = `0xFFFF` before MODER. ODR and BSRR.BS are classed `stock, wrong (…)` in the ledger. **Options:** leave it a ledger gap (the WO excludes the pins' electrical behaviour), or a later row: the guard gates each pin's output by MODER.
2. **F5: AF mode set before AFSEL refuses `predicted-fault` on a transient state.** 139 of 168 pins print `-` at AF0. A MODER-first firmware therefore refuses at the MODER store, though silicon ends valid. (That libopencm3 orders it this way is from memory.)
   - Neither measured firmware does it.
   - RM0433 lists AFRx before MODER (pp534–535).
   Built as ruled, on the state after each write. **Keep, or judge AF routing only when a peripheral asks the query (a later row)?**
3. **Findings only:**
   - **(F6)** RM0433 says "PK[11:8] are not used" and "PK[15:12] are not used" (pp554–555), beside p546's "x = A to J" and p548's "A to K". With no package declared, PK8 output passes (the rule is off, as ruled).
   - **(F7)** The stock `invertedAFPins` misses PA1 AF4 (TIM15_CH1N, p89): a Renode data gap.
   - **(F8)** PH0/PH1/PC14/PC15 are reported as GPIO while HSE/LSE use them (p540): RCC coupling.
   - **(F9)** LCKR = 0 passes; A8 said "any write".
4. **D3 and D5, rows for Aethelgard** (ruled): its pin data comes from ST's open pin data.
   - ST's shared H747 GPIO file gives `DSIHOST_TE` at AF13 on PA15, PB11 and PJ2, which the H753 lacks.
   - ST numbers `SPDIFRX1_IN0`–`IN3` where DS12117 has `IN1`–`IN4` (8 cells).
   No committed board uses either function.
5. **The teardown flake (1300's §5.5), measured again.**
   - 2 of about 80 GPIO case runs lost end records. Renode's process exited before machine disposal finished: no `Disposed` line.
   - Both passed on three reruns. 20 bare load-and-quit runs were complete.
   - It fails closed. Each run now needs 13 end records, and CI may meet it; a rerun is the remedy, as before.
6. **Consumer side (Aethelgard Phase 2), measured, not changed.**
   - Every port reference in its platforms must name the guard's variable:
     - the untyped `gpioPortX:` blocks (chip selects, motor observers), which is the n3 trap;
     - children registered `@ gpioPortX`;
     - the package property, which no consumer platform declares today. The witness says `package: none` until it does.
   - Its boot block changes on every board: silicon's MODER/OSPEEDR/PUPDR resets (A5).
7. **Outside my fence, done by ruling:**
   - `.github/workflows/ci.yml` (the 19:08Z CI build);
   - `model/Refusal.cs` (Q1);
   - `scripts/tviburi-spike` (18:30Z);
   - `tests/1300/rcc.sh`: its one check call names the ports, or every RCC case fails on the GPIO guards' records.
   I took this from a 1300a working copy (`~/.cache/tvi-1300a/m3src`, read-only) only to measure §8's combined file.

## 6. Process

- **Containers:** one at a time, capped. One CI-memory run measured ArduPilot's build in its pinned image, with `--network none`.
- **Two slips:**
  - **A suite broke itself:** bash reads `tests/spike.sh` lazily, and I edited it mid-run. That run's 81 checks had all passed. Suites have since run from an export of HEAD.
  - **A mount hid the image's `/var`:** I mounted the variants at `/var`, which hid `/var/lib`; the first trial could not boot. It was caught at once.
- **A permission check stopped an `rm -rf` inside a container script.** I rewrote the loop as one fresh container per board, with nothing to remove.
- **Tokens:** a mint for each push (`tviburi` only), each revoked (HTTP 204).

## 7. Close

**Suites on `bfc1594`, run from its export:** (filled in at READY).

**Reduction pass (diff): −187 net · 2 duplicates removed · 1 dead path removed · 0 protected kept · 3 findings below.** A fresh subagent proposed the cuts; I applied three and kept the rest as findings.
- Removed: the Python comparison, which the ruled Rust tool supersedes; the fixture's byte-identical `platform.repl`; and the guard's unread `Row.Class`.
- **Findings:**
  1. **The §1.1 harness only reproduces §1–§8's numbers:** `proto1301.py` and its variants, `gpio-restate.awk`, `fields.py`, `memory-1301.sh`, `smoke/`, `capture-gen.sh`, and `replay-1301.sh`'s modes other than `accept`. It is about 590 lines. Keep it as evidence, or delete it and let the branch's history hold it? Your call, as in 1300.
  2. **The 11 port names are typed at every check call** (9 copies). One `GUARDS` variable in `tests/spike.sh` would serve them all. I left that shared file's lines as 1300a will also touch them.
  3. **`tests/1301/visual/visual.tsv` is byte-identical to the model table,** asserted in prose. An `afcheck` assertion would make it a check.

**Head at READY:** see the PR.
