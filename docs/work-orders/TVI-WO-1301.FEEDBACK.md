# TVI-WO-1301 — FEEDBACK

**Builder:** Claude (Opus 5.5), Furnace, 2026-11-23. **Branch:** `wo/tvi-1301-gpio-af-guard`, from `main` `604c3d6`, with `main` `2e4c56d` and `c95f161` (docs only) merged; the suites were not rerun for them.

**State:**
- §1.1 measured and stopped (16:59Z). The architect ruled at 17:52Z (both stops, the form, A1–A10, D1–D5, records, AFRH), 18:30Z (the memory: judged in CI) and 19:08Z (Q1–Q3). The build followed.
- **No §7 stop is open:**
  - no valid path refuses (both firmwares, all 15 boots);
  - the package is declared;
  - the memory is judged in CI per 18:30Z, and passes locally as a proxy (§3).
- **STOPPED at 19:57Z: CI could not meet the memory bound** (§3). **Measured as ruled at 20:20Z** (§3a); the decision is Scotty's.
- **Ruled at 19:47Z:** F1 (follow-up `TVI-WO-1301a`), F5 kept, the harness deleted, rows (§5). The merge order: 1300a (#21) first, then `main` merged here, the suite rerun, and CI re-measuring the combined file.
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
| …the one-file peak under 1,048,576 KiB, with the runs and the margin | CI: `tests/1301/ci-memory.sh`, 5 runs, each ≤ 1,015,808 KiB (32 MiB spare, ruled 18:30Z). The ELF is built from Aethelgard's pinned image and held to `a799dfb6` | **CI, run 45249617507 on `1b00410` (this branch alone, without 1300a): FAILS.** The ELF reproduced `a799dfb6`. The peaks were **1,049,556; 1,019,700; 1,029,164; 1,041,540; 1,018,148 KiB**, at 18.9–20.3 s wall. All five are over the 32 MiB-spare limit, and run 1 is over the bound itself. Every boot reached its end, with 0 GPIO would-refuse. **On Furnace, unslowed (about 10 s wall):** 951,832 to 1,008,376 KiB, all under |
| The generated firmware's GPIO sequences pass, enforcing | the 14 boots replayed, ports enforcing; `gpio-ardupilot`, `gpio-gen-s03` (planted) | 0 refusals; every boot-block difference is silicon's reset (A5, ruled expected); motor observers identical to stock |
| A planted AF with no function refuses `predicted-fault`, naming the pin, the AF and the page; the table's AF passes | `gpio-blank`, `-blankfirst`, `-afterwards`, `-afrh`, `-pk8af`; `gpio-valid` | as stated |
| Every field outside the allow-list refuses `unmodeled` (gate clause 6) | `gpio-fields` (25), `gpio-pupdr`, `-lckr`, `-brr`, `-read`, `-misaligned` | all refuse by name |
| The table and ST's open pin data agree, or each disagreement is ruled | `afcheck` | D1–D5 ruled; the check fails on anything else |
| CI green | the PR | see the PR |

**Mutants:** G01–G24, each killed by its named case (`tests/mutants.sh` on exports of `3ec764c` and `025b6e4`).

**Suites:** see §7.

## 3a. The memory, measured in CI as ruled (20:20Z)

**CI run 45257810560, head `e831735`.** A temporary step, `tests/1301/ci-memory-gc.sh`, removed before READY, ran five full ArduPilot boots per configuration, interleaved. The ELF was `a799dfb6` again.

| Configuration | Peaks (KiB), runs 1–5 | Wall (s) | 5 of 5 ≤ 1,015,808? |
|---|---|---|---|
| base: `main` `bf7fb36`'s one file, GPIO stock | 980,288; 969,316; 977,208; 960,768; 960,464 | 13.3–14.2 | yes (max 980,288) |
| base-gc | 982,220; 981,004; 972,768; 978,728; 972,368 | 13.5–14.1 | yes (max 982,220) |
| 1301 | 989,328; 978,280; 988,472; 989,224; 973,560 | 13.1–14.1 | yes (max 989,328) |
| 1301-gc | 1,000,328; 1,000,212; 974,968; 973,636; 981,788 | 13.7–14.6 | yes (max 1,000,328) |

- **The regular memory step** in the same run (1301) **passed**: 980,536; 981,432; 973,928; 972,420; 970,364 KiB.
- **The GC settings:** `DOTNET_gcServer=0`, `DOTNET_gcConcurrent=0`, `DOTNET_GCConserveMemory=9`. Renode is a self-contained .NET 8 application, and its CoreCLR reads `DOTNET_*` at start. The runtime's own report, from inside Renode, shows them applied: `ServerGC=False ConcurrentGC=False GCConserveMem=9 LatencyMode=Batch`, where the default is `ConcurrentGC=True GCConserveMem=0 Interactive`. **They lower nothing:** the medians are equal or a few MiB higher.
- **The same code failed a run earlier** (45249617507: 1,018,148 to 1,049,556 KiB at 18.9–20.3 s a boot). This runner took 13–14 s a boot, and every configuration, `main` included, peaked about 30–40 MiB lower. **The peak follows the runner's speed** (as §8 found on Furnace, r = 0.978). On this runner, 1301 costs about 10 MiB over `main` at the median, and the margin is about 26 MiB at the worst run.
- **For Scotty (northstar §6; `RUL-09` (a)):** the bound is judged on whatever runner GitHub gives, and runners vary. `main` alone was not measured on the slow runner. **Inferred, not measured:** the slow run's 1301 peaks less about 10 MiB give about 1,007,900 to 1,039,300 KiB, so `main` alone would likely have failed the 32 MiB-spare limit there too.

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

## 5. Findings, as ruled (2026-11-23T19:47Z)

1. **F1 is inherited from the stock port, and is not refused.** The stock port drives a pin's connections from ODR in every mode: LEDs, EXTI, the timers' inputs, and IDR. Silicon drives a pin from ODR only in output mode:
   - p537 (input): "The output buffer is disabled";
   - p538 (AF): "driven by the signals coming from the peripheral";
   - p539 (analog).
   Refusing ODR/BSRR writes would refuse the valid "ODR, then MODER" order; ArduPilot's board init writes ODR = `0xFFFF` first. The ledger's ODR and BSRR.BS rows say `stock, wrong (… p537-539)`.
   **The fix is `TVI-WO-1301a`:** the guard drives a pin's connections from ODR only in output mode, and from the peripheral in AF mode. **Its inputs, from this work order:**
   - after reset, `GPIOB_BSRR = 0x00000001` lights `GreenLED` (PB0) with PB0 analog (silicon: dark);
   - `GPIOC_BSRR = 0x2000` with PC13 an input fires EXTI13 and makes IDR13 read 1 (silicon: neither);
   - an ODR or BSRR write on a timer-AF pin overrides the timer's level at the consumer's motor observers. The generated boards wire `pa8obs` and others to PA8–PA11 and PE11–PE14 (`timer1`, AF1), through `CombinedInput`, which is an OR;
   - OTYPER = 1 then ODR = 1 drives the pin high (silicon: Hi-Z); A1 covers this one.
2. **F5 is kept: AF mode set before AFSEL refuses `predicted-fault`.** That is the lesser error, since neither firmware does it. **A row:** once a peripheral's start asks the `WhichSignal` query, judge AF routing at the start, not at the MODER store.
3. **Rows** (findings for the roadmap):
   - **F6:** RM0433 says "PK[11:8] are not used" and "PK[15:12] are not used" (pp554–555), beside p546's "x = A to J" and p548's "A to K". With no package declared, PK8 output passes, as ruled.
   - **F7: a Renode data gap.** The pinned `platforms/cpus/stm32h7.repl` (sha256 `b85cb4af…`) gives port A `invertedAFPins: [[5, 3], [7, 1, 3]]` (line 183). It lacks `[1, 4]`, so PA1's AF4 (TIM15_CH1N, DS12117 Table 9 p89) follows CH1 uninverted.
   - **F8:** PH0/PH1 and PC14/PC15 are reported as GPIO while HSE or LSE uses them (RM0433 p540). This needs RCC coupling.
   - **F9:** LCKR = 0 passes. It starts no key sequence and reads 0, as silicon does; A8 said "any write".
   - **The guard-name list:** typed at 9 check calls in `tests/spike.sh` and `tests/1300/rcc.sh`. One `GUARDS` variable, after 1300a.
4. **D3 and D5, rows for Aethelgard** (ruled): its pin data comes from ST's open pin data.
   - ST's shared H747 GPIO file gives `DSIHOST_TE` at AF13 on PA15, PB11 and PJ2, which the H753 lacks.
   - ST numbers `SPDIFRX1_IN0`–`IN3` where DS12117 has `IN1`–`IN4` (8 cells).
   No committed board uses either function.
5. **The teardown flake (1300's §5.5), measured again.**
   - Three runs lost end records: `gpio-nopackage` and `gpio-otyper`, in one run of the 32 GPIO cases, and `rcc-resetfwd`, in one 145-check suite run. Renode's process exited before machine disposal finished.
   - Each passed on rerun. 20 bare load-and-quit runs were complete.
   - It fails closed. Each run now needs 13 end records, so CI may need a rerun.
6. **Consumer side (Aethelgard Phase 2), measured, not changed.**
   - Every port reference in its platforms must name the guard's variable:
     - the untyped `gpioPortX:` blocks (chip selects, motor observers), which is the n3 trap;
     - children registered `@ gpioPortX`;
     - the package property, which no consumer platform declares today. The witness says `package: none` until it does.
   - Its boot block changes on every generated board: silicon's MODER/OSPEEDR/PUPDR resets (A5).
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

**Suites, each run from an export of HEAD:**
- **`bfc1594`:** `tests/spike.sh` gave **144 of 145 checks**, with all 32 GPIO cases among them. The one failure was `rcc-resetfwd`: the teardown flake (§5 item 5). Its answers were all correct, and 11 of 13 end records were written before Renode exited. It passed on rerun. `tests/adversarial.sh`: **ALL PASS**.
- **`ec04588`,** after the reduction pass: `rcc-resetfwd`, `afcheck`, `unit`, `gpio-valid`, `-ledger`, `-fields` and `-registrations` give **ALL PASS**. `tests/1301/ci-memory.sh` (one run, the trimmed fixture) gives the ELF `a799dfb6` and a peak of 991,676 KiB: **ALL PASS**.

**Reduction pass (diff): −187 net · 2 duplicates removed · 1 dead path removed · 0 protected kept · 3 findings below.** A fresh subagent proposed the cuts; I applied three and kept the rest as findings.
- Removed: the Python comparison, which the ruled Rust tool supersedes; the fixture's byte-identical `platform.repl`; and the guard's unread `Row.Class`.
- **Then, as ruled at 19:47Z:** the §1.1 harness, about 590 lines that ran only from Furnace's scratch, was deleted. It is in git history at `1b00410`. `replay-1301.sh` keeps `accept` alone.
- **Findings:**
  1. **The guard-name list** is now a row (§5 item 3).
  2. **`tests/1301/visual/visual.tsv`** is byte-identical to the model table, asserted in prose. An `afcheck` assertion would make it a check.

**Head at READY:** see the PR.
