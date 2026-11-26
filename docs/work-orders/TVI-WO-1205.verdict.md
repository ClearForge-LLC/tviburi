# Phase 1's exit gate: the verdict (TVI-WO-1205), for Scotty

**The verdict: met.** All seven clauses hold on `main` with `TVI-WO-1308` merged (`5d4a61a`). Each is proved in CI by a named case and a mutant that case kills.
- **The run judged:** CI run 45858432173 on `63fe557`: 1,000 items, 0 failures. Clause 1's final rows are on `5d4a61a`.
- **Not met when this work order started:** clause 1 waited on `1308`. Clause 6's sweeps for five guards rested on a scratch run until their mutants were committed here, and DMAMUX1's and PWR's until `DX34` and `PW39` (granted 18:38Z).
- **The evidence:** `TVI-WO-1205.gate.md`, clause by clause, with what a skeptic would try and the case that stops it.

| Clause | Verdict | What proves it |
|---|---|---|
| **1.** Every path up to your cut refuses by name | **Met** | The armed run reaches 653 rows and registers on the four flight paths. 412 are guarded, 241 acknowledged by name, 0 open. DMAMUX1 and PWR (`1308`) closed the last two families. PWR is judged on the platform the armed run boots: 4400h's Sentinel-03, with PWR restated, revision V, by `tests/1301/replay-1301.sh`. The armed run arms at 9.3 s with no record |
| **2.** Same build, same ledger; any file changes the hash | **Met** | `reproducible`, `identity`, `verify`, `adv-cache`; mutants `U04`, `U05`, `U07`, `U10` |
| **3.** A removed guard, a crash or a refusal fails at both checks, in both log framings | **Met** | All nine cells have a case. Mutants `R06`, `R07`, `R16`, `R17`, `S01`, `S04`, `S05`. The teardown race behind the old reruns is fixed at its cause (`1204`) |
| **4.** The MODF scar refuses where stock passes; its text matches the manual | **Met** | `modf`, `stock-modf` (stock runs the same writes to the end), `modf-page` (against RM0433's p2184 text) |
| **5.** A forged record line is not a record | **Met** | `forge`, `stock-forge`, `adv-spray`, `adv-hook`; mutants `R02`, `R13`, `R19` |
| **6.** Every field outside its allow-list refuses `unmodeled` | **Met** | Every guard's sweep, each now killed by a committed mutant |
| **7.** A recorded run is never a pass | **Met** | `recorded`, `adv-recorded`, `load`; mutants `R12`, `R18`, `T02`, `T03` |

**Three things to know about "met":**
- **97 guarded rows, and DMAMUX1's nine armed channels after C0, are guarded by type, with the evidence from a sibling's case.** The rows: `usart2`/`usart3` (swept on `usart1`), `i2c2`, `spi2`, GPIO ports B and D. The channels: C1–C7, C10 and C11, judged by C0's rows. The same guard code runs at each.
- **"Acknowledged by name" is a ledger entry, not a refusal.** 241 rows on the flight paths behave as the stock model does, each named with its reason. TIM1's status flags were audited here: ArduPilot never reads them.
- **A sweep proves the guard enforces its table, not that the table is right.** Each row's rightness is its RM0433 citation, held by review.

**Carried into Phase 2 (Aethelgard's half):** `TVI-WO-1205.consumer.md`, one ordered checklist of 34 items, plus five carried over. Each cites its source and says how the release lets Aethelgard check it. The ones that block a first consumer run:
- the class map, with every `-> dmamux1@N` line and every RCC property moved onto its guard;
- the board properties: crystal, revision, battery level, pulls;
- reading Renode's `[ERROR]` lines as records, not as a failed load.

**Carried into Phase 4 (the bench):**
- the 69 inherited rows off the cut (TIM5, the interrupt controller, RTC, watchdog, ADC3's channels, debug, RNG);
- the timer's period and first edge, with Aethelgard;
- floating inputs in the end record;
- the guards that stop at a malformed access in recorded mode (`1204` R9).

**For the roadmap (the architect's PR):**
- Phase 1's gate met;
- `1205`'s rows (FEEDBACK §6);
- A10 recorded in `TVI-WO-0100b.rm0433.md`, as granted.
