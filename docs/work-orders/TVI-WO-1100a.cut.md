# The `OQ-03` cut: one page for Scotty's ruling (TVI-WO-1100a)

**The decision:** which of the twin's unmodeled paths must refuse before Phase 1 closes (Phase 1 exit gate, clause 1), and which wait for Phase 4's bench. Proposed from data on 2026-11-26. **Ruled by Scotty, 2026-11-26: the recommended cut.** The evidence is `TVI-WO-1100a.measure.md`.

**The measurement:** unmodified ArduPilot on Aethelgard's Sentinel-03, armed over MAVLink and driving its motors, on today's twin (`main` `39e51af`). It arms at 9.3 s with no record from any guard, once its battery voltage is declared, which no consumer property does yet. Of the 935 queue rows, 610 are **guarded**, 253 **acknowledged by name** in the ledger, 72 **inherited and unaudited**, and 0 unreached.

## The cut (recommended)

**Above the line: everything the armed run reaches on the four paths that fly.**
- **Motor:** TIM1 and its pins.
- **Clock tree:** RCC, flash wait states, PWR.
- **Sensor buses:** SPI1/2, I2C1/2, the battery ADC.
- **Command links:** USART1–3: the console's MAVLink, CRSF RC, MAVLink2.
- Their DMA routes on each of these paths.

All of it is guarded or acknowledged today **except two families**. Each is one work order:

| Open family | Why it matters | Work order |
|---|---|---|
| **DMAMUX1**, the DMA request router (stock Renode) | Every DMA stream the flight uses is routed through it: IMU, barometer, battery, MAVLink, RC (10 channels) | **a DMAMUX1 guard** |
| **PWR**, voltage scaling (a stock stub that answers "ready" to everything) | The voltage scale bounds the clock. The stub hides a firmware that raises the clock before the voltage is ready | **a PWR guard** |

The motor path, the first worry in `1100`, is fully covered: TIM1 has been guarded since `1302`.

**The cautious alternative: also TIM5,** ArduPilot's scheduler clock (every task deadline is a TIM5 compare, 130,856 in the run).
- **Cost:** a third work order, the timer guard extended to TIM2–TIM5. It also takes TIM2's 54 generated-board rows.
- **Not recommended:** a wrong tick changes *when* ArduPilot acts, which the twin does not claim to time exactly. It does not change *whether* a path it takes is modeled.

## Left for Phase 4's bench

- **Reached by the armed run off the four paths:** TIM5, unless the alternative is ruled; the interrupt controller; the RTC; the watchdog; ADC3's internal channels (BDMA, and DMAMUX2, which has no model at all); the debug units; the RNG.
- **Reached only by generated test boards:** TIM2.
- **Unused instances.**
- **The 253 acknowledged rows,** which keep their ledger entries.

## The gate's other clauses, at `main` (CI 45774342008: 970 of 970 cases and mutants passed)

- **Met:** 2 (identical ledgers; any change moves the hash), 5 (forged records rejected), 7 (recorded runs never pass).
- **Partly met, all fixed by tests** (one "gate proofs" work order):
  - **3:** a removed guard or a crash is shown only in one of the two log framings, and no crash is run at Tviburi's runner.
  - **4:** the stock half of the MODF scar passes for lack of a witness, not on the firmware. The manual's page is checked by hand, not in CI.
  - **6:** 20 allow-list rows of the I2C and ADC guards have no refusing case.

## Also needs fixing before Aethelgard consumes a release

**The release's class map restates every timer and `spi4` onto guards** that Tviburi has only ever run on TIM1, TIM8, SPI1 and SPI2. A consumer following it would likely see ArduPilot's TIM5 refused (inferred, not run). It goes in the gate-proofs work order or earlier.

## Order of work if ruled as recommended

1. DMAMUX1 guard.
2. PWR guard.
3. Gate proofs (clauses 3, 4 and 6, and the class map).

Then clause 1 is met up to the cut, and Phase 1's gate can be judged.
