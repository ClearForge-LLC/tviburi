# The `OQ-03` cut: one page for Scotty's ruling (TVI-WO-1100a)

**What is being decided:** which of the twin's unmodeled paths must refuse before Phase 1 closes (`OQ-03`; the roadmap's Phase 1 exit gate, clause 1), and which wait for Phase 4's bench. **Proposed from data, 2026-11-26. Not ruled.** The evidence is `TVI-WO-1100a.measure.md`, its row tables in `spike/1100a/data/`.

**What was measured:**
- **The run:** ArduPilot (unmodified Copter-4.7.2), on Aethelgard's Sentinel-03, armed over MAVLink with no force, then flying its motors.
- **The twin:** today's `main` (`39e51af`), every guard on and in recorded mode.
- **Its result:** it arms at 9.3 s with no record from any guard. That needs the battery pin's voltage declared, which no consumer property supplies yet.
- **The rows judged:** 935. These are the old queues (`1100`: the generated boards, 722 rows; `1150` (v): ArduPilot's first 5 s, 122 more) and 91 rows only the armed run reaches.
- **Every row's state:** **guarded** 610 (modeled, or any other value refuses); **acknowledged by name** in the ledger 253; **inherited, unaudited** 72; **not reached** 0.

## The proposed cut (recommended)

**Above the line: everything the armed run reaches on the four paths that fly.**
- **Motor:** TIM1 and its pins.
- **Clock tree:** RCC, `FLASH.ACR`, PWR.
- **Sensor buses:** SPI1/2, I2C1/2, the battery ADC, and their DMA routes.
- **Command link:** USART1 (MAVLink) and its DMA routes.

On `main` today, all of it is guarded or acknowledged, **except two families:**

| Open family | Why it matters | What the armed run does there | Work order implied (one slice) |
|---|---|---|---|
| **DMAMUX1** (the DMA request router; stock Renode, unaudited) | It routes every DMA stream the flight uses: IMU (SPI2), barometer (SPI1), battery (ADC1), MAVLink (USART1). The DMA guard checks the route only when a stream starts | 7 channels configured, 2 of them rewritten 37,678 times | **A DMAMUX1 guard,** all 16 channels |
| **PWR** (voltage scaling; a stock stub that answers "ready" to everything) | The CPU's voltage scale bounds its clock. The stub hides a firmware that raises the clock before the voltage is ready, and the flash guard has to assume scale 1 | `VOS` set to Scale 1 (RM0433 Rev 9 p310), the LDO and USB regulator enabled (p308), backup-domain access opened (p305) | **A PWR guard** |

**Why the line falls there:** this is the first measured window in which ArduPilot actually flies. Above it, a false pass reaches a motor, a sensor reading or a command; below it, it does not. The motor path, the first worry (`1100`'s motor-timer rows), is fully covered: TIM1 has been guarded since `1302`.

## The cautious alternative

**The same cut, plus TIM5,** ArduPilot's scheduler clock: every task deadline is a TIM5 compare (`CCR1` rewritten 130,856 times in the run, with 128,191 distinct values).
- **Cost:** a third slice, the timer guard extended to TIM2–TIM5. That also takes the 54 rows of TIM2, which only generated test boards use. It adds about one guard's CI time and memory.
- **Not recommended:** a wrong tick moves *when* ArduPilot does things, which the twin does not claim to time exactly (northstar §5), not *whether* a path it takes is modeled.

**A stricter reading of clause 1 is also possible:** the 253 acknowledged rows must refuse rather than pass by name. That means re-opening every past acknowledgement ruling, across all nine guards, so it is not recommended.

## What it leaves for Phase 4's bench

- **What the armed run reaches off the four paths:** TIM5 (unless the alternative is ruled), the interrupt controller, the RTC, the watchdog, BDMA and DMAMUX2 (ADC3's internal channels; the platform has no DMAMUX2 model at all), the debug units, the RNG.
- **What only the generated test boards reach:** TIM2.
- **Unused instances:** `i2c4`, `lpuart1`, `spi4`, every other timer.
- **The 253 acknowledged rows,** which keep their ledger entries.

## The gate's other clauses, at `main` `39e51af` (CI run 45774342008, green)

| Clause | State | To close it |
|---|---|---|
| 2 identical ledgers; any changed file changes the hash | **met** | — |
| 3 removed guard / crash / refusal fails, runner and records check, both log framings | **cells unproven:** removed and crash in Aethelgard's console framing; a crash at Tviburi's runner | a test fix |
| 4 the MODF scar refuses where stock Renode passes, condition matching the manual's page | **partly:** the refusal is met; the stock half passes because the run has no witness, not because stock reached the end (it does; measured); the page match is checked by hand, not in CI | a test fix |
| 5 a forged record line is not a record | **met** | — |
| 6 each field outside a guard's allow-list refuses | **met for 7 of 9 guards;** 20 rows unproven (I2C 2, ADC 18) | a test fix |
| 7 a recorded run is never a pass | **met** | — |

**Two further fixes, outside the clauses:**
- **The ledger says TIM2–TIM7, TIM12–TIM17 and SPI4 are guarded; they are not.** The class map lacks their instance list. This is a one-line fix with a test.
- The RCC and DMA guards, and the PWR stub, log their registers as `unknown` in the access log.

**Work orders implied, in order:**
1. **DMAMUX1 guard.**
2. **PWR guard.**
3. **Gate proofs:** the clause 3, 4 and 6 test fixes and the class-map fix, in one slice.

(+ the TIM2–TIM5 guard if the alternative is ruled.)

Then clause 1 is met up to the cut, and Phase 1's gate can be judged.
