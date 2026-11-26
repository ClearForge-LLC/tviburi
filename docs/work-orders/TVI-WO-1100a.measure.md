# TVI-WO-1100a — §1 measurement

**Builder:** Claude (Opus 5.5), Furnace, 2026-11-26. **Branch:** `wo/tvi-1100a-cut`, from `main` `39e51af`.
**State:** predictions and the join rules (§0), committed before any Renode run of this work order.

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
