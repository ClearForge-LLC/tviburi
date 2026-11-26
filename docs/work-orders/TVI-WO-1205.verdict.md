# Phase 1's exit gate: the verdict (TVI-WO-1205), for Scotty

**The verdict:** clauses 2–7 are **met**. Clause 1 is [held for your ruling on the unaudited acknowledgements, below].

**What it was judged on:**
- `main` with `TVI-WO-1308` merged (`5d4a61a`), and this PR's CI [run].
- Clauses 2–7 were also green on `63fe557`, CI 45858432173: 1,000 items, 0 failures.
- **The rule:** each clause rests on named cases, and each case is named by a mutant it kills. This PR adds 21 of those mutants.
- **The evidence:** `TVI-WO-1205.gate.md`.

| Clause | Verdict | What proves it |
|---|---|---|
| **1.** Every path up to your cut refuses by name | [held] | The armed run's flight paths: 653 queue rows (412 guarded, 241 acknowledged) and 97 armed registers outside them (71, 26). 0 open; DMAMUX1 and PWR (`1308`) closed the last two families. The armed run arms at 9.3 s with no record |
| **2.** Same build, same ledger; any file changes the hash | **Met** | `reproducible`, `identity`, `verify`, `adv-cache` |
| **3.** A removed guard, a crash or a refusal fails at both checks, in both framings | **Met** | Nine cells, each a case named by a mutant it kills (R20–R22, R24, R25, …). `adv-removed` is held by two independent lines (the check's R06/R07 and the runner's S05), each mutant-proven; by design, no single mutant passes it (architect, 19:10Z). The teardown race is fixed at its cause (`1204`) |
| **4.** The MODF scar refuses where stock passes; its text matches the manual | **Met** | `modf`, `stock-modf`, `modf-page` |
| **5.** A forged record is not a record | **Met** | `forge`, `stock-forge`, `adv-spray`, `adv-hook` (R26, R25, R28, R33) |
| **6.** Every field outside its allow-list refuses `unmodeled` | **Met** | All eleven guards' sweeps, each named alone by a mutant |
| **7.** A recorded run is never a pass | **Met** | `recorded`, `adv-recorded` (R32), `load` |

**The question for clause 1** (put to you by the architect, 18:56Z):
- **What is at issue:** 105 rows and 24 registers on the flight paths are acknowledged only through RUL-17's "inherited, unaudited (acknowledged by name)". The ledger names each one, but nobody has read its stock behaviour against the manual.
- **Your cut counted them as acknowledged.**
- **(a):** they count. Clause 1 is **met**, and the reading of them is a later row.
- **(b):** they don't count. Clause 1 is **not met** until each is read, as TIM1's status register was here, which ArduPilot never reads.
- **The architect recommends (a).**

**What "met" means here:**
- **Guarded by type, with the evidence from a sibling's case:**
  - 97 rows on another instance's sweep (`usart2`/`usart3` on `usart1`, `i2c2`, `spi2`, GPIO B and D);
  - 9 DMA registers on stream 0's sweep;
  - 9 DMAMUX1 channels on channel 0's.
- **PWR** is judged on the platform the armed run boots: 4400h's Sentinel-03, with PWR restated, revision V, by `tests/1301/replay-1301.sh`.
- **Acknowledged is a ledger entry, not a refusal.** 86 of the 241 rows carry a field known to differ from silicon, each with its ruling.
- **A sweep proves the guard enforces its table,** not that the table is right. Each row's rightness is its RM0433 citation, held by review.

**Into Phase 2:** `TVI-WO-1205.consumer.md`, 37 ordered items plus five carried over. Each cites its source and says how the release lets Aethelgard check it. First:
- the class map, with every `-> dmamux1@N` line and RCC property moved onto its guard;
- the board's crystal, revision, battery level and pulls;
- `[ERROR]` lines read as records, not as a failed load.

**Into Phase 4:**
- **The 69 inherited rows off the cut:** TIM2 54, mostly from generated boards; TIM5 5; BDMA 5; NVIC 4; DWT 1.
- **The 82 inherited armed registers outside rows:** NVIC, RTC, TIM5, BDMA, watchdog, DWT, RNG.
- **Also:** the timer's period and first edge, with Aethelgard; floating inputs in the end record.

**Done in this PR outside its fence, as granted:**
- **A10** in `TVI-WO-0100b.rm0433.md`;
- **the `TIMx_SR` rows' text;**
- **21 mutants.**
