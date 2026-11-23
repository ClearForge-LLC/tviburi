# TVI-WO-1100 — the coverage map: the queue and a proposed `OQ-03` cut

**Status:** measured on 2026-11-23, stopped after WO §1.3. This is a spike: no model code was written, and no gate was run. The rule and the predictions were committed before any run (`predictions.md`, commit `2c5c1d8`).

**Contents:**
1. Inputs
2. Method
3. The rule, as committed, and the readings made after the data
4. Predictions against the measurement
5. The queue
6. The join: rows seen in one view only
7. Outside the two classes: power, DMA and DShot
8. The proposed `OQ-03` cut
9. Files

---

## 1. Inputs

- **Aethelgard:** `main` at `f42a0c88`. Cloned read-only to `~/tvi-1100-aet`, with the push URL disabled, and mounted `:ro` in every container. The CLI and `aethelgard-bridge` were built from it (`cargo build --locked`) into this worktree's untracked `target/`.
- **Image:** Aethelgard main's `container/IMAGE`, `ghcr.io/clearforge-llc/aethelgard-tools@sha256:e5fc7ddb…`, with Renode `1.17.0+20260907gitf1dd1b4af.aet1605`. Every container ran under `~/bin/capped-run`, at `nice -n 10`, with `--network none` after the build. Only one of mine ran at a time.
- **The source that executed:** the STM32H7 models are compiled into `Infrastructure.dll`.
  - Measured in the image: the `STM32H7_SPI` and `STM32H7_RCC` types are in `Infrastructure.dll`, sha256 `ddb19d7b…`, which the image rebuilt from renode-infrastructure `cd4b002a` plus three NVIC patches.
  - `ledger.rs` cites `066a7f13`.
  - `diff -rq` of the two trees: `src/Emulator/Peripherals` is identical. Only `NVIC.cs`, which carries the patches, `MappedMemory.cs` and a unit test differ. So every `066a7f13` line cited here is a line that executed.

**Boards.** The architect ruled on the bus at 2026-11-23T08:45Z: every committed specification that `verify` accepts, including the motor boards. The full per-board table is `data/boards.tsv`.

| Board (spec basename) | Spec | `verify` | Ledger rows | Motors |
|---|---|---|---|---|
| `sentinel-01-minimal` (→ `boards/sentinel-01`) | `examples/` | pass | 301 | — |
| `sentinel-01` (→ `boards/sentinel-01-bench`) | `examples/` | pass | 354 | — |
| `sentinel-02-bench` | `examples/` | pass | 280 | — |
| `sentinel-01-ext-bmp581` | `examples/` | fail (`extract`) | 354 | — |
| `motors-dshot600` | cli fixture | pass | 339 | 4, DShot600, TIM1 |
| `motors-pwm` | cli fixture | pass | 339 | 4, PWM, TIM1 |
| `serial-dshot-9600` | cli fixture | pass | 393 | 4, DShot, TIM1 |
| `sentinel-03` | cli fixture | pass | 710 | 4, DShot, TIM1 |
| `sentinel-02-shaped` | cli fixture | fail (`extract`) | 505 | 4, DShot, TIM1 |
| `hse-external-8` | cli fixture | not verified (`DrawsNoBoard`) | 623 | 4, DShot, TIM1 + TIM2 |
| `hse-crystal-16` | cli fixture | fail (`extract`) | 623 | 4, DShot, TIM1 + TIM2 |
| `driver-judges-nothing` | cli fixture | pass | 306 | H-bridge and servos, TIM1 + TIM2 |
| `d1-drv8876` | ir fixture | not verified (`DrawsNoBoard`) | 443 | DRV8876 and a servo, TIM1 + TIM2 |

- **A failed gate leaves the ledger.** A `fail` or `not_verified` verdict comes from a board gate: `extract`'s copper capacitance, or no board drawn. The ledger is built before any gate (`verify.rs` L1700–1720), and each of these runs proved, built and booted its firmware.
- **The cut does not depend on those boards.** Re-ranked on the eight boards `verify` passes cleanly, ranks 1–172 hold the same rows (§8).

**Not covered, with the reason:**
- `examples/sentinel-02-bench-hse8.json`: `verify` needs 32G; skipped as ruled.
- `examples/invalid-zero-domain.json`: invalid by design.
- `crates/aethelgard-ir/tests/fixtures/d2-drv8833.json`: refused before any ledger, `PartLimitUndeclared` U2 (`input_pwm_max_hz`, exit 2).
- **The "four-port fc-base fixture at specification 28":** not on `main`. It is `AET-WO-4408`'s deliverable, `fc-base` today is an ArduPilot build, not a specification, and "specification 28" is the schema version every spec carries.
- **The 14 non-motor test fixtures under `crates/*/tests/fixtures`:** outside the ruling's list. All run on HSI (`hse_hz` 0), so they can add no motor row and no PLL row. Their effect on the cut is bounded in §8, not measured.

## 2. Method

**View (1), the consumer's.**
- Plain `aethelgard verify --spec <spec>`, unchanged; the verdict's `ledger.rows` are read by `reader/ledger-rows.jq`.
- Every row is kept, judged ones too. A judged row whose note says "also ignored-by-model" is marked (`also_ignored`), because the ledger lets the stronger class win (`ledger.rs` L31–33).
- **The verdict's rows do not carry the Renode citation.** `LedgerRow` has no `source`, and `register_rows` copies only `judge` and `note` (`ledger.rs` L2046–2060). For each row, the cited source is therefore that of the `table()` entry `classify()` matches. That entry's source is rendered in `data/table-entries.tsv` and matched by `reader/view1.awk`.
- Register addresses come from the CMSIS header Aethelgard pins (`reader/cmsis-regs.awk`).

**View (2), the simulator's.**
1. **Capture.** `harness/capture.sh` runs the same `verify`. An unsandboxed watcher in the same throwaway container copies each boot's exact argv (`/proc/<pid>/cmdline`) and the CLI's scratch while Renode runs. Nothing in the image or in Aethelgard is changed.
2. **Replay.** `harness/replay.sh` runs that argv twice in the pinned image with no network:
   - pure;
   - logged: `sysbus LogAllPeripheralsAccess true` inserted after `sysbus LoadELF`, and `harness/regmap.py` run before `quit`.
3. **The executed map.** `regmap.py` reads every register's fields and tags by reflection from the objects that ran.
4. **Firmware writes.** A write is the firmware's when its log line carries `[cpu: 0x<pc>]`, which `SystemBus.DecorateWithCPUNameAndPC` adds only while a CPU executes. Otherwise it is the judge's (`reader/view2-writes.awk`).
5. **Classification.** Each ledger field is classed by what covers its bits in the executed model:
   - a tag, which holds its reset value;
   - undefined bits;
   - an absent register;
   - a read-only field;
   - a stored field.

   `dropped` is `yes` where the model cannot hold the value the firmware last wrote (`reader/view2-join.awk`). Each row then gets the source line that declares that tag or field (`reader/cite-source.sh`, 722 of 722 resolved; four spot-checked by hand).

**Controls, all passed:**
- **The replay is the CLI's run.**
  - The pure replay reproduces the verdict: the TMP117 identity read takes 360071 instructions, `STEP 2` − `STEP 1` = 362759 − 2688, the verdict's "took 360071 instructions".
  - The captured ELF's sha256 equals each verdict's `elf_sha256`.
  - Logging changes nothing: on all 13 boards the sentinel block (boot word, executed instructions, PC, every register read) is byte-identical between the pure and the logged replay (`data/boards.tsv`).
- **No firmware write hides as the judge's.** On every board the number of writes without CPU context equals the number of `sysbus Write*` commands in the argv: 17, 33, 35, 29, 24 or 11. No DMA controller is written at all.
- **The empty view-(2)-only set is real.** Every register any model warned "Unhandled write" on has ledger rows. On all 13 boards, the set of (peripheral, register) pairs the models warned on equals the set holding a `dropped = yes` row.

## 3. The rule, as committed, and the readings made after the data

The rule is `predictions.md` §2: class by function, then within a class:
1. more boards first;
2. both views before one;
3. written before reset;
4. `ignored-by-model`, then `same-source`, then a judged row with an "also ignored-by-model" note;
5. the id.

**Readings made after seeing data, stated here, not hidden:**
- **R1, boards are counted within the row's class.** The committed text did not say which boards count when one field has different functions on different boards. Counted over all boards, `GPIOA.OTYPER.OT0` ranked first in the motor class "on 13 boards", because PA0 is a motor pin on one board. Meanwhile `TIM1.BDTR.MOE`, dropped on all 9 boards that write it, ranked 24th. That is exactly the WO §5 case of a rank that hides a flight-critical path. Under R1 a row counts only the boards on which it has that class (`queue.tsv` column 4 shows `in class/all`).
- **R2, a read-only model field is not a drop.** At first the classifier called any write to a field with no write mode "dropped". That flagged `RCC.CR.HSERDY`, `PLL1RDY` and `CFGR.SWS`, which are read-only status bits on silicon too, and produced three false "judged-class" findings. Such writes are now `no (a read-only field in the model…)`.
- **R3, a correction to the reader rather than to the rule: a drop is judged over every write, not the last.** `d1-drv8876` writes `ADC_CR.ADCAL` = 1 to start calibration (write #72, `0x90010100`) and 0 later. The first classifier compared only the last value, so it missed that drop. The model's own warning exposed it in the 13-board check above. The cut is unchanged by R3: the same 172 rows.
- **R4, two further reader corrections, both found by the independent review; the queue is byte-identical after both.**
  - ST's header writes `ADC_Common_TypeDef`'s offsets as "ADC1/3 base address + 0x308". `cmsis-regs.awk` skipped them, so the 8 `ADC3_COMMON.CCR` rows on `d1-drv8876` looked unwritten. They are written (write #62, `CKMODE` = 3) and stored. They are `silicon-only`, so not in the queue.
  - A field Renode declares with `WithIgnoredBits` (named `ignored`) is stored but never acted on. It is now classed so: drop `yes` if written non-zero. One ledger row lands on one: `I2Cn.CR1.ERRIE`, `STM32F7_I2C.cs` L129, written 0 on every board.

## 4. Predictions against the measurement

| Prediction (`predictions.md` §3) | Measured |
|---|---|
| 1. The motor class is empty for the committed boards | **True for `boards/`:** neither Sentinel-01 spec has a motor. The ruling widened the scope, and the motor class is then the largest prefix (157 rows) |
| 2. Clocks at the top, `D2CCIP1R` first | Clocks lead the classes present on all 13 boards, and the `D2CCIP1R`/`D2CCIP2R` selectors are their first rows. **Power is empty** (§7) |
| 3. Sensor buses the largest class, SPI mostly "also ignored" | Sensor buses: 204 rows, of which 80 are judged with "also ignored-by-model". **The rest is larger:** 298, mostly USART tags and unused GPIO `OTYPER` |
| 4. Per board, 150–250 rows; 60–120 in the two classes; 40–80 "also" | **Missed:** 280–710 rows per board, and 149–441 in the two classes. Per board "also" rows: 0–88 |
| 5. Tag warnings fire only on non-zero writes; a zero written to a tag is visible only in the access log | **True:** `PeripheralRegister.cs` L682 warns on `UnderlyingValue ^ value` at undefined positions, so a 0 written to a tag whose reset is 0 is silent |
| 6. The cut: `D2CCIP1R`, the enables, BOR/PWR | **Partly:** the selectors are in. BOR and PWR have no row in the two classes. The motor rows, absent from the prediction, lead |

## 5. The queue

**Where it lives.** `data/queue.tsv` holds 722 rows in rank order. Each row cites both views:
- **view (1):** the `ledger.rs` line of the classifying entry, and its source (columns 16–17);
- **view (2):** the executed cover (column 12), the per-board drop (13), the firmware-write counts (14) and the Renode declaration line (18).

The access-log lines themselves (sequence, PC, offset, value) are in `data/writes.tsv`.

**By class:**

| Ranks | Class | Rows | Of which the executed model holds a tag | Dropped on some board |
|---|---|---|---|---|
| 1–157 | Motor output | 157 | 87 | 7 |
| 158–220 | Clocks | 63 | 45 | 12 |
| 221–424 | Sensor buses | 204 | 130 | 26 |
| 425–722 | The rest | 298 | 181 | 6 |

**The head of each class.** "B" is boards in the class; "dropped" counts boards where the model could not hold the written value.

| Rank | Row | B | Ledger | Executed model (066a7f13) | Dropped |
|---|---|---|---|---|---|
| 1 | `GPIOA.OTYPER.OT8` (PA8 = MOTOR1, TIM1_CH1) | 9 | ignored-by-model (`ledger.rs` L1053; AET-WO-4300b FEEDBACK §10) | tag, `STM32_GPIOPort.cs` L175 | 0 (written 0) |
| 2–13 | `TIM1.BDTR.*` (`AOE`, `BKx`, `DTG`, `LOCK`, `MOE`, `OSSI`, `OSSR`) | 9 | ignored-by-model (L1711; AET-WO-4300a FEEDBACK §0) | every field a tag, `STM32_Timer.cs` L380–386 | `MOE`: 9 of 9 |
| 14–16, 17–34 | `TIM1.CCER.CCxNE`, `TIM1.CCMR1/2.OCxPE/FE/CE/M[3]`, `CR1.CKD/UIFREMAP` | 9 | ignored-by-model | tags (`OC{n}PE` L437, interpolated) | `OC1PE`/`OC2PE`: 9; `OC3PE`/`OC4PE`: 5 |
| 35–70 | `TIM1.CCER` (`CCxE`, `CCxP`, `CCxNP`), `CCMR CCxS`, `CR1`, `EGR`, motor-pin `MODER`/`OSPEEDR`/`AFSEL` | 9 | same-source | stored fields, or tags (`EGR`) | 0 |
| 78–141 | The same paths on `TIM2` | 4 | as `TIM1` | as `TIM1` | `OC1PE`: 4, `OC2PE`: 3 |
| 158–164 | `RCC.CR.D2CKRDY`, `D1CCIPR.CKPERSEL`, `D2CCIP1R.SPI123SEL`/`SPI45SEL`, `D2CCIP2R.I2C123SEL`/`USART16SEL`/`USART28SEL`, all `@reset` | 13 | ignored-by-model (L594; `STM32H7_RCC.cs` L408, L410, L425) | tags, `STM32H7_RCC.cs` L408 etc. | `SPI123SEL`: 9 (written `100`) |
| 165–172 | `RCC.D2CCIP1R.*` written | 9 | independent-rule, also ignored-by-model (L458) | tags | `SPI123SEL`: 9 |
| 174–215 | HSE/PLL: `RCC.CFGR`, `RCC.CR.*RDY`, `PLLCFGR.DIVxyEN`/`PLLxRGE`/`VCOSEL`, `FLASH.ACR` | 2 | ignored-by-model / independent-rule | tags, read-only flags | `DIVP2EN`…`DIVR3EN`: 2 (written 0, tag resets 1) |
| 221–232 | `GPIOx.OTYPER` of bus pins; `SPI1.CR2.TSER`, `SPI1.IFCR.*` | 11 | ignored-by-model | tags | `GPIOB.OTYPER.OT6`/`OT7` (I2C open-drain): 11 |
| 253–281 | `I2C1.TIMINGR.*`; `SPI1.CFG1` `MBR`/`CRCSIZE`, `CFG2` `SSM`/`AFCNTR`, `CR1.SSI` | 11 | independent-rule, also ignored-by-model | tags (`STM32F7_I2C.cs` L221…; `STM32H7_SPI.cs`) | each 11 |
| 425… | `USARTn.CR1/CR2` tags; unused pins' `OTYPER`; `ADC3` | ≤13 | ignored-by-model | tags; `FIFOEN` lands in `RESERVED[29+3]` | `USARTn.CR1.FIFOEN`; `ADC3.CR.ADCAL`, `CFGR.JQDIS`, `SMPR1.SMP5` (1 board) |

## 6. The join: rows seen in one view only

Every one-sided row has a reason from the closed set committed before the join (`predictions.md` §2). No other reason was needed.
- **R-reset, 9 rows:** the `@reset` rows, e.g. `RCC.D2CCIP2R.I2C123SEL@reset` and `SPIn.SR.MODF@reset`. The firmware writes nothing there by definition. Where the register is written for another field (`D2CCIP1R`), view (2) still shows the tag.
- **R-window, 16 rows:** the `I2C2.CR2`/`ICR`/`TXDR` transfer fields on `sentinel-01` and `sentinel-03`.
  - The ledger lists them because the generated driver's transfer function writes them.
  - The firmware only initialises I2C2 (`CR1`, `TIMINGR`, `PE`), because no device is declared on the EXT bus.
  - On `sentinel-01-ext-bmp581`, where the BMP581 sits on I2C2, the same fields are written 5 times each. So this is "the path is not executed", in the specific form "no device on the bus".
- **R-never-issued:** none observed, because nothing in any generated firmware uses DMA (§7).
- **R-not-in-ledger:** none. Every firmware write the executed model drops lands on a field the ledger has a row for. Every `written` ledger row with no firmware write is one of the 16 I2C2 `R-window` rows (42 board-rows).
- **Behavioural ignores.** No board's console holds a model's conditional ignore ("Cannot modify … Ignoring write", `STM32H7_RCC.cs` L154, L193, L271, L280, or any "ignor", "cannot" or "not supported" line). So the warning-based control is not missing a logged ignore on these runs.
- **R-judged-class:** none after R2. No judged row without an "also ignored-by-model" note is dropped on any board (WO §5, first case). Four judged rows sit partly on a tag or undefined bits. Each passes today only because the written value fits; they are listed in FEEDBACK.
- **One correction to a recorded gap.** `SCB.CPACR.CP10`/`CP11` (same-source; "whether Renode's core faults a float … is not held") are not dropped. The write lands outside NVIC's register collection, in its own switch (`NVIC.cs` L610–620). That code stores `CPACR` into the CPU and enables the FPU, and no warning is logged.

## 7. Outside the two classes: power, DMA and DShot

- **Power has no row in the queue.**
  - BOR's only ledger row is `FLASH.OPTSR_CUR.BOR_LEV@reset`, `silicon-only`. The firmware never writes the option bytes: the level is set by the factory step in FAB-NOTES.
  - `PWR.CSR1.ACTVOSRDY@reset` and `PWR.D3CR.VOSRDY@reset` are `silicon-only`.
  - `PWR.CR3.*` and `PWR.D3CR.VOS` are `independent-rule`. They are held by Aethelgard's own PWR script in the board `.repl` (`pwr: script`): `CR3`'s first write is stored, and `VOS` is stored. They are not held by a stock model.
  - Nothing in power is both unjudged and a stock-model drop. `RUL-04`'s BOR item therefore has no consumer row to answer today.
- **DMA has no row on any board.**
  - No ledger has a DMA, DMAMUX or BDMA row, and no replay writes a DMA controller.
  - The ledger has `TIMx.DIER.CCxDE`, `DCR` and `DMAR` rows on no board.
  - `SPIn.CFG1.RXDMAEN`/`TXDMAEN` and `I2Cn.CR1.RXDMAEN`/`TXDMAEN` are written 0.
- **DShot is a fixed duty, not frames.**
  - The generated `Tim1::start_outputs` writes PWM mode 1 once: `ARR` 0x6A (a 600 kbit/s bit period at 64 MHz) and every `CCRx` 0x28, then `BDTR` 0x8000 and `CR1` 0x81. "The result has no method", per the generated `board.rs` comment.
  - On `motors-dshot600` that is write #116, `BreakAndDeadTime = 0x8000` at PC `0x8000B14` (`data/writes.tsv`).
  - `RUL-13`'s "DShot DMA" therefore has no consumer row until Aethelgard generates DShot frames.

## 8. The proposed `OQ-03` cut

**Proposed: the prefix through rank 172.** That is the whole motor-output class (ranks 1–157) and the kernel-clock selector block (ranks 158–172).

**Why it falls there:**
- **Rank 172 is where breadth ends within the clocks class.** Everything above it is on all 9 motor boards (`TIM1`), on all 4 two-timer boards (`TIM2`), or on all 13 boards (the selectors). From rank 173, every clocks row is on 3 boards or fewer: `I2C2EN`, then the two HSE fixtures' PLL rows. This is a statement within a class, not across the queue: by the rule's class order, sensor-bus rows on 11 boards (`GPIOB.OTYPER.OT6`/`OT7`, `I2C1.TIMINGR`, dropped on all 11) rank after it.
- **It covers both measured flight paths.**
  - **The motor path.** `TIM1.BDTR.MOE` is a tag (`STM32_Timer.cs` L385), dropped on 9 of 9 boards. The timer drives its outputs at L76–82 (update) and L113–128 (compare) whatever `MOE`, `CCxP` and the break/off-state bits say: only `CCxS` (`IsOutputMode`, L859) and `CCxE` (the channel timer's enable, L847, L861) gate them. RM0433 Rev 9 §38.4.20, p. 1614: with `MOE` = 0, "OC and OCN outputs are disabled or forced to idle state". So a firmware that never sets `MOE` flies in the twin and is dead on silicon.
  - **The clock path.** It is the recorded A1 scar, SPI1 with no kernel clock: `SPI123SEL` is a tag (`STM32H7_RCC.cs` L408), dropped on 9 of 9 boards that write it.
- **It holds without the doubtful boards.** Re-ranked without the five boards `verify` does not pass cleanly, ranks 1–172 hold the same rows.
- **Bounded, not measured:** the 14 HSI non-motor fixtures could raise only HSI RCC rows already inside the cut, or peripheral-enable rows that are at 3 boards or fewer today.

**What inside the cut must refuse, for Scotty's ruling:**
- **118 rows are measured model gaps.**
  - 102 rows where the executed model holds a tag.
  - The 16 `TIMx.CCER.CCxP`/`CCxNP` rows. These are stored fields (L307–321) that the output path never reads; only input capture does (L665, L855).
- **54 rows are stored fields the executed model acts on, or that this measurement cannot decide.** These are motor-pin `MODER`/`OSPEEDR`/`AFSEL`, `CCxE`, `CCxS`, `CR1.ARPE`/`CMS`/`DIR`/`UDIS`/`URS`, and `RCC.APB1LENR.TIM2EN`. They are in the queue because the ledger has no independent judge for them (`same-source`), not because the model drops them. I propose they go to the `OQ-04` path or to the `13NN` builder's source reading, not to the Phase 1 refusal gate.
- **The refusal condition is per row, and it is the `13NN` builder's to set from RM0433.** For most tags it is a written value other than the tag's reset. For `MOE` it is the opposite: the firmware writes 1, which the model already behaves like. The false pass is outputs running with `MOE` = 0.
- **Not in the cut, and why:**
  - **DMA:** no row exists (§7).
  - **BOR:** the firmware writes no BOR field. Its only row is `silicon-only`, and the factory step sets it (§7).
  - **HSE/PLL:** on 2 boards today; 3 if `sentinel-02-bench-hse8`, skipped at 32G, counts. They are next in line after the cut.

**The strongest case against this boundary**, from the independent review, put to Scotty as the alternative:
- The HSE/PLL rows are dropped on every board that uses them: `RCC.CR.HSEBYP`, and the `PLLCFGR` set `DIVP2EN`, `DIVP3EN`, `DIVQ2EN`, `DIVQ3EN`, `DIVR1EN`, `DIVR2EN`, `DIVR3EN`, plus `PLL1RGE` and `PLL1VCOSEL` on one board each.
- They rank low only because two fixtures use an HSE. That is R1's own failure mode, a board count hiding a flight-critical path.
- **The alternative cut adds those 10 dropped rows** (ranks 174–215) to the refusal set, which then has 128 rows.
- I keep the prefix as proposed because no committed example that ran uses an HSE. The one that does, `sentinel-02-bench-hse8`, was skipped at 32G. If Scotty weights the HSE fixtures as flight-representative, the alternative is the better cut.

## 9. Files

- `predictions.md`: the rule and the predictions, committed before any run.
- `harness/`:
  - `capture.sh`: verify plus the `/proc` watcher;
  - `replay.sh`: pure, and logged with the map;
  - `regmap.py`: the executed register map, by reflection;
  - `dumps.txt`;
  - `pipeline.sh`: from the captures to `queue.tsv`.
- `reader/`: `ledger-rows.jq`, `cmsis-regs.awk`, `view1.awk`, `view2-writes.awk`, `view2-join.awk`, `pins.awk`, `rank.awk`, `cite-source.sh`. These are scratch readers, with no build file.
- `data/`:
  - `queue.tsv`: the ranked queue, both views and their citations;
  - `joined.tsv`: every ledger row of every board, joined;
  - `writes.tsv`: the parsed access-log writes and warnings;
  - `pins.tsv`: each board's pin roles, from its generated `board.rs`;
  - `table-entries.tsv`: `ledger.rs` `table()`, rendered;
  - `boards.tsv`.
- **Reproduce:** run `capture.sh` (in the pinned image, with the clone mounted at `/repo`), then `pipeline.sh WORK CAPDIR…`. The raw consoles (about 2.7 MB per board) are not committed. `pipeline.sh` regenerates them from the captures.
