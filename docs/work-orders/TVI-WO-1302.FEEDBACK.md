# TVI-WO-1302 — FEEDBACK

**Builder:** Claude (Opus 5.5), Furnace, 2026-11-24. **Branch:** `wo/tvi-1302-advanced-timer-guard`, from `main` `9e61b81`.

**State:**
- §1.1 was measured and stopped (`TVI-WO-1302.measure.md`), then ruled at 2026-11-24T06:10Z (Q1–Q7). A fence question about the guard-name lists was ruled at 06:52Z.
- The guard is built to the rulings. No §7 stop is open.
- `model/Refusal.cs`, the record grammar, the RCC/SPI/GPIO guards' C#, the steering documents and Aethelgard are untouched. One PR, left unmerged.

## 1. What was built

| Part | File |
|---|---|
| The guard: a subclass of `STM32_Timer` that re-implements the three bus interfaces and `INumberedGPIOOutput`, so the platform's routes bind to its own four outputs and the stock lines feed only its taps (measure.md §2.3). Each output is silicon's level by Table 337 (p1608): `OCxREF xor CCxP` with `MOE` = 1 and `CCxE` = 1; the idle level `OISx` with `MOE` = 0, `OSSI` = 1 and `CCxE` = 1; otherwise not driven (Hi-Z), **shown low** (ruled Q4: all 33 committed motor outputs have a fitted 442 kΩ pull-down). `OCxREF` is the stock line, except that in PWM mode 1 a `CCRx` of 0 holds it at 0 (p1558), and `CCRx` reads 0 at reset (p1610), both from the guard's copy (ruled Q2). The period and the first edge stay the stock model's (ruled Q1) | `model/GuardedSTM32_Timer.cs` |
| Rules: `TVI-TIM-CONFIG`, `-WIDTH` (p1588: half-words or words), `-READ`, `-DMAR` (p1616), `-LOCK` (`predicted-fault`, p1615: frozen by the first `BDTR` write; a changing write refuses, the same value passes), `-FIELD` (the allow-list, `unmodeled`: `CCxNE` and `CCxNP` = 1 among them, ruled Q5 (a)) | the same |
| The allow-list: 225 rows over the 25 registers of TIM1/TIM8 (every bit 31:0), generated from RM0433 Rev 9 §38.4 with the classes ruled at Q7 | the same, `Fields` |
| The restatement (variant B, ruled Q3): the GPIO generator's one change and its regenerated output (only the `timer1`/`timer8` renames, §2), a generator for the timer restatement, and the board's include and modes. **The RCC restatement is untouched** (§2) | `model/stm32h7-{gpio,timer}-restate.{awk,repl}`, `model/tviburi-nucleo-h753zi.repl` |
| The class row | `tools/tviburi-release/pins.tsv` |
| 35 cases, each `timer-<case>` in `tests/spike.sh`'s acceptance group (registered there, additively); 28 planted firmware paths | `tests/1302/timer.sh`, `firmware/timer.rs` |
| The table's generator and its input: RM0433 Rev 9 §38.4's fields, extracted and checked to cover each register once; `timer-regen` checks the table byte for byte | `tests/1302/fields.py`, `tests/1302/data/rm0433-rev9-tim18-fields.tsv` |
| Mutants TM01–TM31, one per guard added or changed, two for the restatement, one for the table's regeneration | `tests/mutants.tsv` |
| The replay instantiates the timer guards on a tree that has them, moving the consumer's `timer1:`/`timer8:` frequency onto them (the measured trap) | `tests/1301/replay-1301.sh` |
| `timer1 timer8` appended to each guard-name list (granted 06:52Z) | `tests/1301/gpio.sh`, `tests/1300/rcc.sh`, `tests/spike.sh` (6), `tests/mutants.tsv` (G17), `scripts/tviburi-spike` |
