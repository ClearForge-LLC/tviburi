# TVI-WO-1100a — §1 measurement

**Builder:** Claude (Opus 5.5), Furnace, 2026-11-26. **Branch:** `wo/tvi-1100a-cut`, from `main` `39e51af`.
**State:** measured. Predictions and rules (§0) were committed at `2514129`, before any run. The runs, the queue's states, the cut and the audit follow (§1–§5). The one-page decision is `TVI-WO-1100a.cut.md`.

**Contents:** §0 predictions and rules · §1 the runs · §2 every row's state · §3 the cut, ranked · §4 Phase 1's gate, clause by clause · §5 findings · §6 files

## 0. Predictions and rules, committed before any run

### 0.1 What was read before writing them

So these predictions are not blind on the following points:
- **`main`'s release,** built from this tree in the pinned image (`aethelgard-tools@sha256:1e0ab105…`, the digest `tests/spike.sh` and Aethelgard `main` `e04ab6e0` both pin): `sha256:8e4a1f4b9cf6b1c2…`. Its ledger's field rows counted by guard and state (1,739 rows), and its `model` rows (which stock class each guard covers, and the instances the stock files declare).
- **The restate files** (`model/stm32h7-*-restate.repl`) and the class map (`tools/tviburi-release/pins.tsv`). **Already seen:** the class map's timer row names no instances, so the ledger's `model` row says `GuardedSTM32_Timer` covers `timer1`–`timer17`. The restate file restates only `timer1` and `timer8`.
- **`1100`'s queue** (`spike/1100/queue.md`, `data/queue.tsv`: 722 rows), **`1150` (v)** (`spike/1150/results.md` (v), `data/queue-ap.tsv`: 844 rows, of which 122 are ArduPilot's alone). Each queue's rows counted by instance.
- **What the earlier work orders say about the armed run:** `1305` (the capture and its unpadded injection), `1300c`'s measure §R (it arms at 9.332 s on `main` and on its branch), and `1306a`'s and `1300c`'s statement that no replay holds a record from any guard.
- **Aethelgard `spike/4400h/out/queue/`** at `fa12abf4`: its README, and the line counts of its Sentinel-03 queues (armed 924 rows, unarmed 923). **Not read:** their rows.
- The CI workflow, `tests/ci/route.sh`, and the clause-6 sweeps' check strings in `tests/spike.sh`, `tests/1300`–`1307`.

### 0.2 The rows

- **The universe:** `1100`'s 722 rows, and `1150` (v)'s 844, keyed by id (`TIM1.BDTR.MOE`, `dma1@0x58.…`). `1150`'s 844 hold `1100`'s 722, so the union is 844 rows. Then **armed-only rows:** a register the armed run writes that no row covers.
  - Where Aethelgard `4400h`'s committed armed Sentinel-03 queue names that register's fields (stock Renode, the same readers), its field ids are used.
  - Otherwise the row is register-level, named `<instance>@<offset>`.
- **Control:** every one of the 844 ids appears exactly once in the output, and each armed-only row is tagged with its provenance. The output's row count is 844 plus the armed-only count, checked by the reader.

### 0.3 The join: by instance, address and bits, never by name

- The queues use CMSIS/Renode names; the ledger uses RM0433's (`TIMx_BDTR`, `USART_CR1`, `GPIOx_OTYPER`). **A row joins a ledger field by its instance, its register's offset from the instance's base, and its bit range overlapping the field's bits.**
  - The instance and absolute address come from `1100`'s `joined.tsv` (columns 16 and 12) for `1100`'s rows. For `1150`'s ArduPilot-only rows, they come from its id and Renode's cover `[lsb+width]` (column 11).
  - The base is the instance's address in the replayed `platform-booted.repl`.
- **A row whose bits overlap two ledger fields** takes the weaker state of the two, and is listed.

### 0.4 Two columns, then the work order's state

- **Reach,** separately for each source:
  - `gen`: `1100`'s generated boards at Aethelgard `f42a0c88`. Written means `1100`'s firmware-write count (column 14) is above 0 on some board. An `@reset` row is "relied on at reset", counted as reached by `gen`.
  - `ap5`: `1150` (v)'s 5 s window, register-level, its column 5 above 0.
  - `arm`: this work order's armed run on `main`, register-level. The run writes the row's register at least once, from a CPU (a `[cpu: …]` PC), in an access log whose completeness is controlled (§0.6).
  - `arm-stock`: `4400h`'s committed armed queue. A cross-check only; a disagreement with `arm` is reported, not reconciled.
- **Guard state:** from the instance actually bound in the replayed platform, never from the ledger's class row.
  - **guarded:** the instance is a restated guard (the restate file's line is the evidence), and the field's ledger row either is `guard` (modeled), or has a restricted `allowed` set (any other value refuses, `unmodeled`, `RUL-17`), whatever its state (`tag`, `reserved`, `read-only`, `stock-wrong`, `stock-undefined`). The case that refuses it is the guard's clause-6 sweep line naming that register and field.
  - **acknowledged by name:** the instance is guarded, and the field's state is `acknowledged` or `inherited-unaudited` (`"inherited, unaudited (acknowledged by name)"`) with `allowed` `any`. The evidence is the guard's `Fields` line and its reason.
  - **inherited and unaudited:** the instance is stock in the replayed platform. The evidence is the ledger's `model` row and the platform line.
  - **not reached:** reach is none in all three sources (`gen`, `ap5`, `arm`). This replaces only *inherited*: a guarded or acknowledged row keeps that state.
  - **A row that fits none of these** (a guarded field with `allowed` `any` and a state other than `guard`; a row whose bits are in no ledger field) is a **finding**, not a fifth state.

### 0.5 The cut's rule

- **Above the line:** every row the armed run reaches (`arm`) on one of four paths:
  - **motor:** TIM1, the motor pins, and their DMA;
  - **sensor bus:** SPI1/2, I2C1–3, their pins, DMA and DMAMUX routes;
  - **command link:** USART1 (MAVLink) and its DMA/DMAMUX route;
  - **clock tree:** RCC, PWR's voltage scaling, `FLASH.ACR`.
- **Ranked** in `RUL-13`'s amended tiers (1 RCC enables and selectors; 2 GPIO routing; 3 timers and DMA; 4a other clock tree; 4b the rest), and within a tier by path in the order motor, clock tree, sensor bus, command link.
- **Below the line, waiting for Phase 4's bench:** rows reached only by the generated bring-up boards, or only unarmed; rows not reached; and rows on no flight path (USB, CEC, RNG, the watchdog, debug).
- **Cost:** per family, the existing guard extended, or a new guard named.

### 0.6 The runs

- **A (control):** `1305`'s armed capture (Aethelgard `4400h` `s03-arm-s`: Sentinel-03, ELF `dbf598d8…`, the unpadded injection). It runs on `main`'s release with every guard recorded, `ACCEPT_USART_DMA="usart1:41"` and `ACCEPT_EXTERNAL="PB6:up PB7:up"`, as `1300c` ran it. Nothing is added.
- **B (reach):** the same, plus `sysbus LogAllPeripheralsAccess true`, placed before the run's `RunFor` and after the consumer's write hooks. `collapse-repeated-log-entries` is false.
- **The harness:** the committed `tests/1301/replay-1301.sh` plus `1305a`'s knobs, copied to `spike/1100a/harness/`, with the diff in its header.

### 0.7 Predictions

| # | Prediction |
|---|---|
| P1 | **A arms on `main`:** `COMMAND_ACK` 400 result 0 and an armed `base_mode` near 9.33 s. **No refusal and no `would-refuse` record from any guard.** Every guarded instance has a recorded-mode witness naming release `8e4a1f4b…`. An `end` may be lost to the teardown race; a rerun recovers it |
| P2 | **B's boot block and arm time equal A's.** B logs non-zero CPU writes on `spi1`, `spi2` and `flashController`. The flash count is likely 0, because the consumer's `AETHELGARD-ERASE` hook displaces the access log there (memory of `1304`). If so, flash reach is taken from `ap5` and `arm-stock`, and the reason is stated |
| P3 | **Reach:** B writes TIM1 (its configuration and `CCR1`–`CCR4`) and TIM5 (the tick), and **no TIM2, TIM3 or TIM4**. It writes DMAMUX1 (the USART and SPI routes) and USART2/3 beyond `ap5`. It writes **no TIM1 `DIER.UDE`/`DCR`/`DMAR`** (the run is PWM, not DShot). BDMA and ADC3 are written, the battery monitor. Armed-only rows: 60–200 |
| P4 | **States over the 844 rows:** guarded 600–700; acknowledged by name 80–160; inherited 50–90 (TIM2's 54, TIM5's 5, PWR's 3, `SCB`, `nvic`, `dwt`); not reached 0–5 |
| P5 | **The inherited rows the armed run reaches are TIM5, PWR, DMAMUX1, BDMA, `nvic`/`SCB`, `dwt` and SYSCFG.** TIM2's 54 are not reached by `arm` |
| P6 | **The cut** has 0 unguarded rows on the motor path (TIM1 and GPIO are guarded). The open rows are on the clock tree (PWR `VOS`), the sensor and command routes (DMAMUX1, and BDMA for ADC3) and TIM5. **Recommendation:** DMAMUX1 and PWR as two slices; TIM5, TIM2–4, BDMA, `i2c4` and `lpuart1` below the line |
| P7 | **The audit:** clauses 2, 4, 5 and 7 met by named cases. Clause 3 met for every cell I look up. **Clause 6** met by every guard's `fields` sweep, but each is a **monitor-write sweep on one instance**, not "a planted firmware", so it is flagged as a wording gap rather than marked silently. **Clause 1 is not met:** open rows are listed |

## 1. The runs

### 1.1 What ran

- **The tree:** this branch at `2514129`, which is `main` `39e51af` plus this work order's records. Its release is `sha256:8e4a1f4b9cf6b1c2…`, and its ledger is `sha256:dae643a7fc6db7bf…` (1,845 lines).
- **The image:** `aethelgard-tools@sha256:1e0ab105…`. Every container ran under `~/bin/capped-run` (8G, swap 0), at `nice -n 10`, `--network none`, one at a time (one exception, §5 F9).
- **The capture:** `1305`'s capture of Aethelgard `4400h`'s `s03-arm-s`:
  - Sentinel-03, unmodified Copter-4.7.2, ELF `dbf598d8…`;
  - its unpadded injection, `inject.resc`: 21 frames over 14 s;
  - every guard recorded.
- **The harness:** `spike/1100a/harness/replay-armed.sh` and `inner.sh`. They patch the committed `tests/1301/replay-1301.sh` at run time, never a copy; the diff is kept with each run.

| Run | What it adds | Wall, peak | Arms | Records |
|---|---|---|---|---|
| **A0** | §0.6 as written: `1300c`'s flags only | 139 s, 6.65 GB; **Renode aborted** (core dump) at virtual 21.27 s | `COMMAND_ACK` 400 result 0 at 9.003 s, armed `base_mode` from 9.332 s | **805,530 `would-refuse` `TVI-ADC-LEVEL`** on `adcM1S2` channel 10, one per `DR` read; nothing else |
| **A** (control) | the argv's `RunFor "16.0000000005"` cut to `"0.000000001"` (`1305`'s window: the injection's own 14 s); `ACCEPT_ADC_LEVELS="adcM1S2:10:1.40"` (`1307`'s declared battery pin) | 70 s, 3.83 GB | 9.003 s; armed from 9.332 s | **none:** 32 witnesses and 32 ends, every one recorded, release `8e4a1f4b…` |
| **B** (reach) | A, plus `sysbus LogAllPeripheralsAccess true` before the injection (after the consumer's hooks); `collapse-repeated-log-entries = False` | 100 s, 4.96 GB | 9.003 s; armed from 9.332 s | none: 32 and 32 |

**Controls, all passed:**
- **B's boot block equals A's, line for line:** 503,285,255 instructions, the PC, every register the consumer reads, and both flash hashes. The consumer's own hook lines are equal too (`AETHELGARD-CFG2` 79,401, `AETHELGARD-ERASE` 5).
- **B's log holds every firmware write, in three forms** (corrected by the adversarial pass, D3-1: the first reader read only the first):
  - 4,683,319 CPU writes (each with a `[cpu: …]` PC), on 380 registers of 34 instances;
  - **31 writes the bus refused** because the RCC guard locks an unclocked peripheral (`model/GuardedSTM32H7_RCC.cs` L23–28). They are logged as `sysbus: Tried to write a locked peripheral`, with no PC, at 0.97 ms, between CPU writes: BDMA `CCRx`/`IFCR`, DMA1/DMA2 `SxCR`/`LIFCR`/`HIFCR`, and I2C1/I2C2 `CR1`, each 0 or all ones. They reach 14 registers no CPU write reaches. Silicon ignores a write to an unclocked peripheral too (p373);
  - **3 CPU writes to addresses no peripheral answers:** DMAMUX2 `C7CR` = 0x11 (0x5802581C, request 17, ADC3's route to BDMA), DBGMCU 0x5C00103C = 0x8, and the AXI interconnect's GPV at 0x51008108 = 1 (`data/arm-unmapped.tsv`).
  - **The hooked peripherals were not emptied:** `spi1` 215,008 writes, `spi2` 1,029,937, `flashController` 733.
  - A guard's records are one JSON object after the source, never a `Write… to` line, so none is counted as an access.
- **Three wrappers log their registers as `unknown`:** `rcc`, `dma1`/`dma2` and `pwr`. So the join is by offset, never by name (§0.3).

### 1.2 The arm

- The run is ArduPilot's arm over MAVLink, with no force (`reader/mav.py`, from `1305`).
- `COMMAND_ACK` for 400 (arm) gives result 4 at 7.005 s, then 0 at 9.003 s.
- Armed `base_mode` 209/217 from 9.332 s, as `1300c` measured on `main` before this tree.
- The armed run **still arms on `main`**, so WO §7's second stop did not fire. It arms only with the battery pin declared (A0): see §5 F4.

### 1.3 Readings made after the data, stated here, not hidden

- **R1, `read-only` with `allowed: any` is guarded.** §0.4 listed it as a finding. `RUL-16` defines `read-only` as "silicon ignores a write, and the guard does too": a modeled behaviour. Its 223 ledger rows (`ISR`, `SR` flags, `RDR`) are read as **guarded (modeled)**.
- **R2, `stock-wrong` with `allowed: any` is acknowledged when its own ledger text names the ruling.** Examples: "acknowledged by name, ruled 2026-11-24T14:10Z Q6", or "coordinated with Aethelgard's motor witness". Read as **acknowledged by name**, the ruling quoted in the evidence.
  - 50 of the ledger's 56 such rows name one.
  - **The six that do not** are `DMA_SxFCR.FTH`, and `TIMx_SR` `UIF` and `CC1IF`–`CC4IF`. They are §5 F3. No queue row lands on them; the `TIMx_SR` register is reached outside the queue (§2.4).
- **R3, a field whose `allowed` set is every value it can hold** (RCC's `LPEN` acknowledgements: `[0, 1]` on one bit) is not restricted. An `acknowledged` state takes precedence over a restricted set. Where the set is not every value, the evidence says "other values refuse".
- **R4, armed-only rows are `4400h`'s armed-queue ids not among the 844.** §0.2 said "a register the armed run writes that no row covers". But under `1150`'s rule, a row is a ledger field or a write the stock model drops, and the armed run writes 253 registers that are neither.
  - The faithful extension is the armed queue made by the same readers: 91 ids (`4400h` at `fa12abf4`, committed as `data/aet-4400h-queue-ap-armed-sentinel-03.tsv`).
  - The 253 registers (240 found by the first reader, 13 more after the adversarial pass, §1.1) are reported separately, at register level, with their state (§2.4). The cut reads both.
- **R5, the window and the battery level** (A0 → A): the knobs `1305` and `1307` ran with, which §0.6 did not name.

### 1.4 Predictions against the measurement

| # | Measured |
|---|---|
| P1 | **Right, after R5:** arms (9.003 s ack, 9.332 s armed), no record, 32 recorded witnesses on `8e4a1f4b…`. **Wrong as written:** without the declared level, the ADC guard records every battery read (A0) |
| P2 | **Right** on the boot block and the arm. **Wrong** on flash: the access log kept `flashController`'s 733 writes beside the consumer's `AETHELGARD-ERASE` hook |
| P3 | **Right:** TIM1 (`CCR1`–`CCR4` 1,999 writes each after the arm, PWM at 400 Hz: `PSC` 0xC7, `ARR` 0x9C3), TIM5, no TIM2–TIM4, no TIM1 `UDE`/`DCR`/`DMAR`, DMAMUX1 (10 channels), BDMA, ADC3. **Armed-only rows: 91** (60–200 predicted) |
| P4 | **States:** guarded 610, acknowledged 253, inherited 72, not reached 0. The guarded count is inside the predicted range; acknowledged was predicted 80–160 and the inherited range held; this is over 935 rows, not the 844 the prediction counted. R1 and R2 decided where 279 ledger rows fall |
| P5 | **Partly:** TIM5, PWR, DMAMUX1, BDMA, `nvic`, `dwt` are inherited and reached. **SYSCFG is not written** in the armed run. TIM2's 54 are not reached by `arm` (right) |
| P6 | **Right:** no unguarded row on the motor path; the open families on the four paths are PWR and DMAMUX1. BDMA (ADC3's internal channels) is on no named path |
| P7 | **Partly:** clauses 2, 5 and 7 met. **Clause 4** is met by its record, but its stock half passes for another reason. **Clause 3** has unproven cells. **Clause 6** is unproven for 20 ledger rows of two guards. **Clause 1:** two families open (§4) |

## 2. Every row's state

### 2.1 The rows and their reach

- **935 rows,** each placed by instance, offset and bits (`reader/rows.py`; the control is 844 + 91, none unplaced, none twice):
  - 722 of `1100`'s queue;
  - 122 of `1150` (v)'s ArduPilot-only rows;
  - 91 armed-only (R4).
- **Reach:**
  - **875 reached by the armed run's CPU writes** on the guarded twin, and **3 by a write the bus refused** at the clock lock (DMA2 stream 1's `CR`, written 0 before `DMA2EN`);
  - **56 reached only by `1100`'s generated boards:** TIM2's 54 (four boards drive TIM2; Sentinel-03's ArduPilot has no `timer2` line at all in B), and `SPI1`/`SPI2` `SR.MODF@reset` (B reads neither `SR`);
  - **1 only by `1150`'s 5 s window:** `dma2@0x3C.FS`, DMA2 stream 1's `FCR` on Sentinel-02's allocation, which B never writes;
  - **none unreached.**
- **The stock cross-check** (`4400h`'s armed queue, stock Renode, the same ELF and plan):
  - 859 rows reached on both, 56 on neither, and 11 that the stock queue does not hold (ArduPilot-only rows of Sentinel-02's DMA allocation: `DMA2.LIFCR` stream 1, DMA1 streams 1, 2 and 4, DMA2 stream 1);
  - **9 differ:** `I2C2.ICR`, written 55 times on the guarded twin and never on stock. ArduPilot probes I2C2's empty bus ("Unknown slave at address 85"); the I2C guard NACKs as silicon does (`1306a`), and ChibiOS clears `NACKCF` (`ICR` = 0x10). On stock Renode the probe never NACKs. The reach differs because the guard models silicon here; no row is hidden.

### 2.2 States, by where the row came from

| Provenance | Guarded | Acknowledged by name | Inherited, unaudited | Not reached |
|---|---|---|---|---|
| `1100` (722) | 547 | 119 | 56 (TIM2 54, `SCB.CPACR` 2) | 0 |
| `1150` ArduPilot-only (122) | 40 | 71 | 11 (TIM5 5, PWR 3, `nvic` 2, `dwt` 1) | 0 |
| armed-only (91) | 23 | 63 | 5 (BDMA) | 0 |
| **all (935)** | **610** | **253** | **72** | **0** |

- **`1100`'s own proposed cut, ranks 1–172:** guarded 95 (TIM1 53, motor-pin GPIO 27, RCC 15), acknowledged 23, **inherited 54: all of TIM2.**
- **Every row's evidence** is in `spike/1100a/data/states.tsv`:
  - **guarded:** the guard's `Fields` line, and its clause-6 case;
  - **acknowledged:** the `Fields` line and the ruling;
  - **inherited:** the ledger's `model` or `canned` row and the type the run bound.

### 2.3 How strong a "guarded" is (WO §5, second attack)

Of the 610 guarded rows (corrected by the adversarial pass, D2-1: the first join called a `guard` row "modeled" before it looked at the row's allowed set):
- **315 are modeled with every value allowed** (the guard's `Fields` row is `guard`, or `read-only` by R1). No value refuses; the guard behaves as silicon.
- **130 are refused by a case that writes this very field on this very instance:** the release's `# CASE refuse <REG.FIELD>` line, or `# FIELD` in `tests/1300` and `tests/1301`. Some of these are also modeled: 99 `guard` rows hold a restricted set, for example GPIO `PUPDRn` `[0,1,2]`, the RCC selectors, `FLASH_ACR.LATENCY`.
- **165 are refused by a case on a sibling instance of the same guard class:**
  - GPIO ports B–F's `PUPDR` by port A's sweep (77);
  - USART2/3 by USART1's (54);
  - I2C2 by I2C1's (17);
  - SPI2 by SPI1's (16);
  - ADC3 by ADC1's (1).

  The code is one class, but no case writes these instances. Each row's scope is `states.tsv` column 20.
- **None is refused only by a neighbour's case:**
  - SPI's `MASKED` fields (refused by another rule first) land on no queue row;
  - TIM1's `DMAR` offset lands on none.
- **A whole-word row** (`RCC.APB2LPENR.undefined bits 0x8180000`) is judged by its own bits, 19, 20 and 27 (D2-2); it was first judged by every field in its word.
- **DMA streams 1–7** are judged by stream 0's rows, and the DMA rows in the queue are modeled or acknowledged. So none rests on a stream-0 case.
- **Three `@reset` rows** (`SPI1`/`SPI2` `SR.MODF@reset`, `RCC.CR.D2CKRDY@reset`) are about the value read at reset, but their evidence is the field's `read-only` row, which is about writes. The value the guard serves is the stock model's (S2-3, a weakness of the evidence, not a state change).

### 2.4 The armed run's reach outside the queue

253 registers the armed run writes are neither a ledger field nor a stock drop (R4); 14 of them only by a write the bus refused at the clock lock. Their states (`data/arm-reach-outside.tsv`):

| Path | Guarded | Acknowledged | Inherited | Finding |
|---|---|---|---|---|
| motor | 4 (`CCR1`–`CCR4`, modeled) | 4 (`CR2`, `DIER`, `PSC`, `ARR`) | 0 | 1 (`TIMx_SR`: `BIF`/`B2IF` are `tag` with `allowed: any`; §5 F3) |
| clock tree | 6 | 13 | **2: PWR `CPUCR` (0x10) and `D3CR` (0x18, `VOS` = 11)** | 0 |
| sensor bus | 34 | 3 | **5: DMAMUX1 C3 (request 9, ADC1), C4 (38, SPI1 TX), C6 (40, SPI2 TX), C10 (39, SPI2 RX), C11 (37, SPI1 RX)** | 0 |
| command link | 6 | 2 | **2: DMAMUX1 C0 (41, USART1 RX), C5 (42, USART1 TX)** | 0 |
| other | 70 | 16 (six DMA2 stream registers written 0 while locked) | 85 (`nvic` 43, `rtc` 19, `timer5` 8, BDMA 7 written 0 while locked, `watchdog` 3, DMAMUX1 C1/C2/C7 for USART2/3, `dwt`, `rng`) | 0 |

- The request ids come from the armed run's own `CxCR` writes, read against RM0433 Rev 9 Table 121 (pp698–699; p604).
- **Outside every model:** DMAMUX2 `C7CR` (ADC3's route to BDMA), DBGMCU and the AXI GPV are written with no peripheral to answer (§1.1). DMAMUX2 is not even an inherited model: the platform has none.

## 3. The cut, ranked by what flies first

### 3.1 What the armed run reaches on the four paths

- **What is counted:** rows (§2.2) and registers outside the queue (§2.4) together.
- **Order:** ranked as §0.5 committed: `RUL-13`'s amended tiers first, then the path (motor, clock tree, sensor bus, command link). Tiers are as `1150`'s `rank-ap.awk` assigns them, so PWR is 4b.
- **"Open"** means inherited and unaudited: no guard, no acknowledgement.

| Rank | Tier | Path | Family | Reached: rows + registers | Guarded | Acknowledged | **Open** | Cost to guard what is open |
|---|---|---|---|---|---|---|---|---|
| 1 | 1 | clock tree (TIM1's `LPEN` on motor) | RCC enables and kernel-clock selectors | 36 + 5 | 23 | 18 | **0** | none |
| 2 | 2 | motor, sensor bus | GPIO alternate-function routing and mode | 44 + 0 | 44 | 0 | **0** | none |
| 3 | 3 | motor | TIM1 | 67 + 9 | 57 | 18 | **0** (one ledger-form finding, §5 F3) | none |
| 4 | 3 | sensor bus, command link | DMA1/DMA2 streams (SPI1/2, ADC1, USART1) | 59 + 21 | 37 | 43 | **0** | none |
| 5 | 3 | sensor bus, command link | **DMAMUX1 routes** | 0 + 7 | 0 | 0 | **7 channels: C3, C4, C6, C10, C11 (sensor); C0, C5 (command)** | **a new DMAMUX1 guard,** one slice for all 16 channels. It answers `1303` §1.1's row: writes with the clocks off, sub-word writes dropped, sync/event/generator fields as tags, a `CxCR` rewrite under a live stream unseen |
| 6 | 4a | clock tree | RCC other (PLLs, prescalers, `BDCR`), `FLASH.ACR` | 54 + 14 | 44 | 24 | **0** | none |
| 7 | 4b | clock tree | **PWR** | 3 + 2 | 0 | 0 | **5: `CR1` (`DBP`, p305), `CR2`, `CR3` (`LDOEN`, `USB33DEN`, p308), `CPUCR`, `D3CR` (`VOS` = 11, Scale 1, p310)** | **a new PWR guard** over the stock canned stub (`stm32h7.repl` L728, "a stock Python script's fixed answers"): `VOS`, `LDOEN`/`SCUEN`, `ACTVOSRDY`/`VOSRDY`. One slice |
| 8 | 4b | motor, sensor bus | GPIO other (`OTYPER`, `OSPEEDR`, `PUPDR`) | 64 + 0 | 41 | 23 | **0** | none |
| 9 | 4b | sensor bus | SPI1/2, I2C1/2, ADC1 | 138 + 22 | 121 | 39 | **0** | none |
| 10 | 4b | command link | USART1 | 54 + 2 | 29 | 27 | **0** | none |

- **The line, recommended:** every row and register the armed run reaches on the four paths is above it.
- **Below it, waiting for Phase 4's bench:**
  - what the armed run reaches on no named path: TIM5 (ChibiOS's tick, 5 rows and 8 registers), `nvic` (45), `rtc` (19), the watchdog (3), BDMA and DMAMUX2 (ADC3's internal channels; DMAMUX2 has no model at all), `dwt`, DBGMCU, the AXI GPV, `rng`, and USART2/3's DMAMUX routes (C1, C2, C7, which the DMAMUX1 slice takes anyway);
  - what only the generated bring-up boards reach: TIM2's 54 rows;
  - DMA2 stream 1 (5 s window only);
  - `i2c4`, `lpuart1`, `spi4`, and every unwritten timer;
  - the 253 acknowledged rows, which keep their acknowledgement.

### 3.2 The recommendation and its alternative

**Recommended cut:** the armed run's reach on the motor, sensor-bus, command-link and clock-tree paths (§3.1).
- **Why there:** it is the first measured window in which ArduPilot flies: armed, commanding its motors, reading its sensors over DMA, and answering MAVLink.
- Above the line, everything but two families is guarded or acknowledged on `main` today. So the gate's clause 1 closes with **two slices**:
  - **PWR:** it is on the clock tree. Its stub answers every voltage-scale change "ready" at once, and the flash guard already "decides at VOS1 where VOS is unknown" (`1304`'s ruling).
  - **DMAMUX1:** every DMA stream the armed run uses for its IMU, barometer, battery and MAVLink is routed through it. The DMA guard checks the route only at each stream's enable.
- **Rows below it** are on paths the armed run does not take, or takes outside the four.

**The cautious reader's alternative: the same cut plus TIM5.**
- ChibiOS runs tickless on TIM5: B writes its `CCR1` 130,856 times, with 128,191 distinct values (258,850 writes to TIM5 in all), so every scheduler deadline is a TIM5 compare.
- A stock-timer gap there, such as the period off by one or `CCxIF` set only with its interrupt enabled (both `stock-wrong` on TIM1, `1302`), shifts every loop rate the twin reports. It does so silently, on the path that flies first.
- **Cost:** a third slice, the timer guard extended to the general-purpose timers TIM2–TIM5 (Table 342, p1695: a different register set from TIM1/TIM8). That also takes TIM2's 54 generated-board rows. About one guard's CI time and memory, by `1306a`–`1300c`'s pattern (+7–9 MiB at the median; about one shard).
- **Why I do not recommend it:** TIM5 is ArduPilot's own timebase, and the twin's timing is instruction-accurate with named gaps, not cycle-accurate (northstar §5). A wrong tick changes when the firmware does things, not whether a path it takes is modeled. The four paths are where a false pass reaches a motor, a sensor reading or a command.

## 4. Phase 1's exit gate, clause by clause, at `main` `39e51af`

**CI at `main`'s head:** run 45774342008 (push of `39e51af`, so `--all`): route, ten shards, budget, budget-tests and `ci`, all success (07:18–07:41Z). Every case and mutant below ran in it.

| Clause | Met? | The job, case or test that proves it | What it does not prove |
|---|---|---|---|
| **1.** every path up to the cut refuses by name, complete record; a planted firmware hits each | **Not met: two families open** (PWR, 5; DMAMUX1, 7) up to the recommended cut | Guarded rows: each guard's clause-6 sweep (§2.3) and its named cases. Acknowledged rows: the ledger by name (`RUL-17`) | (a) **"Refuses by name"** is met by a refusal for a disallowed value, by modeling for a modeled field, and by a name in the ledger for an acknowledged one. A strict reading (each of the 253 acknowledged rows must refuse) is not met, and is the cut's cautious form. (b) **"A planted firmware"**: the sweeps are monitor writes (`pc: none`), not firmware; the guard's rule does not read the PC. (c) 165 guarded rows rest on a sibling instance's case (§2.3) |
| **2.** same build, byte-identical ledgers; any overlay file changes the model hash | **Met** | `reproducible` (two builds, two checkouts, umasks 022/077, mtimes 2000/2030: tarball, manifest, overlay, ledger identical); `identity` (one byte in a model file, the platform's comment, the bench data: each a new release hash); `adv-release` | — |
| **3.** removing a guard, crashing, or a refusal: a non-pass at the runner **and** from a records-only check, in both framings, the console fixture included | **Partly. Unproven cells:** removed and crash in **console** framing; crash at the **runner** | Refusal: `modf` (runner 3), `unit` `the_console_fixture_is_read_in_both_framings` (committed `aet-7dec1d7b` console and `logFile` fixtures: Refused). Removed: `adv-removed` (asserts the runner's exit 1 only); the records check on a run with no guard is `stock-forge`'s `PROBLEM spi1: no load witness` (`logFile`). Crash: `crash` (check on `logFile`: no end record). Missing witness/end: `unit` `missing_witness_or_end_fails`, `logFile` host-stamp framing only | No console-framed case for a removed guard or a crash: the committed fixtures hold a refusal, a `TVI-LOAD`, a mode-less witness and forgeries. No runner case kills the simulator (`adv-noend-error` stops a script, and may still dispose). **A fix:** two console-framed fixtures and a runner kill case |
| **4.** the MODF scar firmware refuses where stock Renode passes; condition text matches RM0433 Rev 9 by page | **Partly: the refusal is met; the stock half passes for another reason; the page match is not checked in CI** | `modf`: REFUSED (3), `TVI-SPI-MODF`, `predicted-fault`, citing §50.5.2 p2184. `stock-modf`: exit 1 | Measured here (`TVIBURI_TESTS="modf stock-modf"`): stock Renode runs the firmware past the faulting write (`after-marker=0xAF7E0002`, Renode exit 0), but `stock-modf` asserts only exit 1, which comes from "no load witness". A stock Renode that faulted would pass it too. The page match is hand-checked (p2184: "SSI bit … pulled low", read again here), not checked in CI. **A fix:** assert the after-marker |
| **5.** a firmware line imitating a record is not one | **Met** | `forge`, `stock-forge` (USART3 analyzer forgeries; `logFile`); `unit` `forged_records_on_the_console_are_not_records` (console fixture) | — |
| **6.** per guarded peripheral, each field outside its allow-list refuses `unmodeled` | **Met for 7 guards; unproven for 20 rows of 2** | Each guard's `fields` case: `tests/spike.sh` (SPI), `tests/1300` (RCC), `1301` (GPIO), `1302` (timer), `1303` (DMA), `1304` (flash), `1305` (USART), `1306` (I2C), `1307` (ADC). Restricted rows named by a refuse case: SPI 54/54, RCC 356/356, GPIO 22/22, timer 159/159, DMA 40/40, flash 43/43, USART 67/67, **I2C 49/51, ADC 105/123** (`data/clause6.tsv`) | I2C `ISR.TXE`, `ISR.TXIS` (the sweep skips `ISR`); ADC `CFGR.AWD1CH`, `SQR1`–`SQR4` `SQ1`–`SQ16` (a channel 0–19 allowed; 20–31 should refuse) and `ADCx_CCR.PRESC` (the sweep takes "allowed only at 0" rows only). Each sweep writes one instance by monitor, and DMA streams 1–7 only with 0. The I2C and ADC checks count records and distinct field names, without matching each to its write; SPI's `MASKED` cases check the rule, not the kind. **A fix:** drive the I2C and ADC sweeps from the ledger, as the other five are |
| **7.** a recorded-mode run is never a pass | **Met** | `recorded`, `adv-recorded` (a recorded run with no `would-refuse` still FAILs), `load` (`mode "Recorded"` is `TVI-LOAD`), `unit` `a_recorded_mode_run_is_never_a_pass` | — |

**WO §7's third stop (a clause of 2–7 unmet on `main`):**
- Clauses 3, 4 and 6 are unproven in the cells named above. Each is **a fix to tests, not a work order of model work.** One "gate proofs" slice can carry all three.
- No model behaviour was found wrong in those cells: what is missing is the case.

## 5. Findings

- **F1, the ledger over-claims guarding for two classes** (invariant 2: "an inherited model the ledger presents as audited").
  - The class map (`tools/tviburi-release/pins.tsv`) names no instances for `SPI.STM32H7_SPI` or `Timers.STM32_Timer`. So the ledger's `model` rows say `guarded_by` the guard for `spi4` and for `timer2`–`timer7` and `timer12`–`timer17`.
  - The replay binds every one of them to the stock type: no restate file declares them.
  - A consumer reading the ledger, or applying the class map to every instance of the type, would take TIM2–TIM5 as guarded.
  - **A fix:** the instance column, as the other seven classes have.
- **F2, the armed run's DMA `LIFCR`/`HIFCR`, USART `CR1`/`CR2`/`CR3` and TIM `CCMR` rows** are acknowledged through `stock-wrong` rows that quote their ruling (R2), not through `RUL-17`'s "inherited, unaudited (acknowledged by name)" class. **Two forms carry one meaning;** the ledger's reader must know both.
- **F3, ledger rows whose form names no ruling:**
  - `TIMx_SR` `BIF`/`B2IF` (`tag`, `allowed: any`; reached by the armed run, written 0 only);
  - `TIMx_SR` `UIF`/`CC1IF`–`CC4IF` and `DMA_SxFCR.FTH` (`stock-wrong`, `allowed: any`, no ruling quoted).

  For an `rc_w0` flag that never sets, writing 0 or 1 changes nothing on silicon either. So no silent pass was found on the measured values; the form is the finding.
- **F4, the armed run arms on `main` only with a declared battery level.**
  - Without `levels`, the ADC guard records `TVI-ADC-LEVEL` at every `DR` read: 805,530 in A0.
  - With the argv's own 16 s `RunFor` after the injection, Renode aborted (core dump) at 21.27 s with 6.65 GB resident.
  - The consumer has no property for the level (`1307` Q10, a row). An enforcing armed run through Aethelgard today would refuse at its first battery read.
- **F5, the access log names `rcc`, `dma1`/`dma2` and `pwr` registers `unknown`.** `1305`'s nested-enum pattern was not carried to the RCC and DMA wrappers. PWR is the stock stub, which has none. A consumer reading register names from the access log loses them there.
- **F6, I2C2's reach differs between the guarded and the stock twin** (§2.1): the guard's NACK makes ChibiOS write `ICR`. Expected from `1306a`; recorded for whoever compares the two.
- **F7, clause 4's stock half** passes on the missing witness, not on the firmware (§4). **F8, clauses 3 and 6** have the unproven cells §4 names.
- **F10, firmware writes outside the access log's CPU lines** (§1.1): 31 refused at the clock lock, and 3 to no peripheral at all, DMAMUX2 among them. A consumer that reads only CPU-tagged access lines misses them, as this work order's first reader did.
- **F11, `RCC.D3CCIPR.SPI6SEL` is placed at bits 29:28,** from Renode's tag width; RM0433 and the ledger give 30:28. Its state is unaffected.
- **F9, one unrecorded container.** A one-line read-only `grep` of the stock `stm32h7.repl` ran with plain `podman run`, not `~/bin/capped-run`. It was a disclosed lapse, not repeated.

## 6. Files

All in `spike/1100a/`:
- `harness/replay-armed.sh`, `harness/inner.sh`: runs A and B;
- `harness/pipeline.sh`: the consoles to the tables;
- `reader/mav.py`: `1305`'s MAVLink reader, copied;
- `reader/arm-writes.py`: B's CPU writes per register, and the run's `peripherals` listing;
- `reader/rows.py`: the 935 rows placed;
- `reader/join.py`: reach and state;
- `reader/clause6.py`: clause 6 per guard.

`data/`:
- `rows.tsv`;
- `states.tsv`: one line per row, with its evidence;
- `arm-reach-outside.tsv` (§2.4);
- `arm-writes.tsv` (with the writes refused at the clock lock, its last column), `arm-unmapped.tsv`, `arm-periph.tsv`;
- `clause6.tsv`;
- `cmsis-regs.tsv`: `1100`'s `cmsis-regs.awk` on Aethelgard `f42a0c88`'s pinned header;
- `aet-4400h-queue-ap-armed-sentinel-03.tsv`: Aethelgard `fa12abf4`, unchanged.

**Not committed:** the consoles (A0 3.85 M lines, A 1.79 M, B 12.0 M; B is 76 MB compressed). `pipeline.sh` regenerates them from the capture.
