# Phase 1's exit gate: the verdict (TVI-WO-1205), for Scotty

**The verdict:** clauses 2–7 are **met**. Clause 1 is **not met**: 129 items above your cut hold fields nobody has read (below). `TVI-WO-1206` reads them, then re-judges it.

**What it was judged on:**
- `main` with `TVI-WO-1308` merged (`5d4a61a`), and this PR's CI on `33dd82e` (45881160764): 1,166 items (518 cases, 648 mutants, the 21 new ones among them), 0 failures; memory budget −15.0 MiB.
- Clauses 2–7 were also green on `63fe557`, CI 45858432173: 1,000 items, 0 failures.
- **The rule:** each clause rests on named cases, and each case is named by a mutant it kills. This PR adds 21 of those mutants.
- **The evidence:** `TVI-WO-1205.gate.md`.

| Clause | Verdict | What proves it |
|---|---|---|
| **1.** Every path up to your cut refuses by name | **Not met** | The armed run's flight paths: 653 queue rows and 97 armed registers outside them. Guarded or acknowledged by a reading: all but 105 rows and 24 registers, which hold `inherited-unaudited` fields (your ruling, 22:53Z). DMAMUX1 and PWR (`1308`) closed the last two open families. The armed run arms at 9.3 s with no record |
| **2.** Same build, same ledger; any file changes the hash | **Met** | `reproducible`, `identity`, `verify`, `adv-cache` |
| **3.** A removed guard, a crash or a refusal fails at both checks, in both framings | **Met** | Nine cells, each a case named by a mutant it kills (R20–R22, R24, R25, …). `adv-removed` is held by two independent lines (the check's R06/R07 and the runner's S05), each mutant-proven; by design, no single mutant passes it (architect, 19:10Z). The teardown race is fixed at its cause (`1204`) |
| **4.** The MODF scar refuses where stock passes; its text matches the manual | **Met** | `modf`, `stock-modf`, `modf-page` |
| **5.** A forged record is not a record | **Met** | `forge`, `stock-forge`, `adv-spray`, `adv-hook` (R26, R25, R28, R33) |
| **6.** Every field outside its allow-list refuses `unmodeled` | **Met** | All eleven guards' sweeps, each named alone by a mutant |
| **7.** A recorded run is never a pass | **Met** | `recorded`, `adv-recorded` (R32), `load` |

**Clause 1, as you ruled** (point 1, (b), relayed by the architect at 22:53Z; `RUL-17` amended in docs PR #70): `inherited-unaudited` is not acknowledged for the gate.
- **What fails:** 129 items above the cut hold `inherited-unaudited` fields. The ledger names each field, but nobody has read its stock behaviour against the manual.
  - **105 rows,** each that one field: USART1–3 57, GPIO A/B/D/E 23, I2C1/2 10, TIM1 9, RCC 6.
  - **24 registers,** each with at least one such field (three also hold fields that were read): RCC 13, USART 5, TIM1 3, SPI1/2 2, I2C1 1.
- **Everything else above the cut** is guarded, or acknowledged by a reading: an `(A<n>)` row, a `stock, wrong` or stock-undefined reason, a quoted ruling, or a reserved, read-only or tag field.
- **`TVI-WO-1206`** reads the 129, as TIM1's status register was read here, then re-judges this clause.

**What guarded and acknowledged mean here:**
- **Guarded by type, with the evidence from a sibling's case:**
  - 97 rows on another instance's sweep (`usart2`/`usart3` on `usart1`, `i2c2`, `spi2`, GPIO B and D);
  - 9 DMA registers on stream 0's sweep;
  - 9 DMAMUX1 channels on channel 0's.
- **PWR** is judged on the platform the armed run boots: 4400h's Sentinel-03, with PWR restated, revision V, by `tests/1301/replay-1301.sh`.
- **Acknowledged is a ledger entry, not a refusal.** Of the 241 acknowledged rows, 136 are acknowledged by a reading, and 86 of those carry a field known to differ from silicon, each with its ruling. The other 105 are the unread ones above.
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
