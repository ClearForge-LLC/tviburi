# TVI-WO-1301a — FEEDBACK

**Builder:** Claude (Opus 5.5), Furnace, 2026-11-23/24. **Branch:** `wo/tvi-1301a-pin-drive-by-mode`, from `main` `e891211`.

**State:**
- §1.1 measured and stopped (`TVI-WO-1301a.measure.md`): ArduPilot relies on the defect at PB7 (`I2C1_SDA`). Ruled 2026-11-23T23:52Z: (a) with (d), hold, the held level for a sourceless AF pin, entering AF takes the peripheral's level, the ceiling 1,572,864 KiB. The build followed. **Ruled again 2026-11-24T01:05Z, on §4:** an undriven input or analog pin goes low, not held (§4.1); the rest became rows. The build was changed and re-measured (§2, §6).
- No §7 stop is open. ArduPilot's boot is unchanged, with PB6/PB7 declared pulled up in the replay. The generated firmware's motor observers are unchanged on all 14 boots.
- `model/Refusal.cs`, the RCC and SPI guards, `tools/`, the steering documents and Aethelgard are untouched. One PR, left unmerged.

## 1. What was built

| Part | File |
|---|---|
| The drive rule. ODR is latched by the guard and read back as written (p537). A pin is driven from ODR in output mode, from the timer routed to its AFSEL in AF mode (taken at the switch, then followed through the stock AF receiver; p538), and from nothing in input or analog mode (p537, p539). A pin the port does not drive takes a sender's level, the board's declared level or PUPDR's pull (none in analog, p539); with none, an input or analog pin is low (p537, p539; ruled 01:05Z), and an AF pin no Renode model drives holds (ruled 23:52Z (3)) | `model/GuardedSTM32_GPIOPort.cs` (`Pass`, `Drive`, `Outside`, `Peripheral`, `Sources`) |
| IDR by mode: output, ODR; AF, the pin's level; analog, 0 (p539); input, the level from outside, else 0 (ruled: a floating pin) | `Idr` |
| The property `external` ("PB6:up PB7:up"), checked against the port's own bus address (`TVI-GPIO-CONFIG` otherwise); the witness carries it beside `package` | constructor, `Check` |
| The ledger: ODR, BSRR.BS and MODER move to modeled by the guard, as does PUPDR (its pulls now set undriven pins). The IDR row states the floating-pin assumption plainly | `Fields` |
| 18 new planted cases, each `gpio-<case>`: `analogled`, `analogout`, `odrfirst`, `moderfirst`, `analoghold`, `afodr`, `afenter`, `afenterhigh`, `afout`, `afentern`, `afswitch`, `afnosrc`, `inputidr`, `button`, `externalbad`, `resetodr`, `odrhalf`, `exti` (and `registrations` and `resetwrite` changed) | `firmware/gpio.rs`, `tests/1301/gpio.sh` |
| Mutants H01–H21, one per guard added; G05, G06 and G20 re-pointed at the code they guard now | `tests/mutants.tsv` |
| The replay declares a board's levels (`ACCEPT_EXTERNAL`). The CI memory step declares Sentinel-02's I2C1 pull-ups, citing the hwdef (L24–25), standing in for the consumer | `tests/1301/replay-1301.sh`, `tests/1301/ci-memory.sh` |

**The memory cost of reading the peripheral's level** (ruling 4 asked for it): ArduPilot's 5 s boot, replayed five times on each guard, interleaved, on Furnace with load near 2. `main`'s guard peaked at 973,784; 976,868; 985,000; 987,224; 989,940 KiB, and this one at 980,628; 981,168; 985,104; 991,500; 1,000,196. The median is +104 KiB, inside the spread between runs. That is Furnace's figure; CI's is in §6.

**Not done as ruled, and why:** ruling (4) said "through the guard's own AF receivers". The guard keeps the stock receivers, which already carry a timer's edges while the pin is in AF mode and gate on the selected AF. At the switch, it reads the routed timer output's current level from the output itself. The outputs are found once, by scanning the machine's numbered GPIO outputs for endpoints on this port's receivers. This gives the same behaviour with less code and no change to how the 102 routes bind. The cost is in §4.

## 2. Acceptance (WO §2)

| Clause | Where shown | Result |
|---|---|---|
| GreenLED stays dark after BSRR on PB0 in analog mode, and lights at MODER = output | `gpio-analogled`, `gpio-analogout` | pass |
| A timer-AF motor pin's observer follows the timer, not an ODR write | `gpio-afodr` (PA8 AF1, observed as the generated boards observe it) | pass |
| "Set ODR, then output" drives the latched level at the switch | `gpio-odrfirst`, `gpio-analogout`; `gpio-moderfirst` for the other order | pass |
| ArduPilot's boot unchanged, **with Sentinel-02's PB6/PB7 pull-ups declared in the replay** (ruled 23:52Z) | 1300's capture, replayed against `main`'s guard | console identical line for line (minus records and timestamps); `0x53EABA5` instructions; 4,741 CFG2 lines; boot block identical; 0 GPIO would-refuse |
| The generated firmware's observers unchanged | 14 boots, against `main`'s guard | motor-observer lines identical on all 14; instruction counts identical. Seven boards differ only in line order: a chip select now rises at its MODER = output write, one write after the BSRR that set it. **One boot-block value differs, cited:** `d1-drv8876`'s `GPIOA_IDR`, `0x800` → `0xA800`. The new bits are PA13 and PA15, the JTAG pins: AF0 at reset with their reset pull-ups (`PUPDR` A `0x64000000`, p542). In AF mode IDR reads the pin (p538), so the new value is silicon's; stock read 0. **Accepted 01:05Z as silicon's value** |
| CI green, with the memory step | the PR | see §6 |

**Re-measured after the 01:05Z change** (an undriven input or analog pin goes low), on `63743d0`'s tree: the 15 boots again against `main`'s guard. The results are the same as above, row for row: ArduPilot identical (console, `0x53EABA5`, 4,741 CFG2 lines, boot block); motor observers identical on all 14; the same seven boards differ only in line order; `d1-drv8876`'s `GPIOA_IDR` is `0xA800`.

## 3. The adversarial pass (WO §5)

Two passes: mine, through the build; and an independent one by a fresh reviewer given the code, the stock source and RM0433, not my conclusions. Each finding was checked in the code before acting.

| WO §5 item | Result |
|---|---|
| BSRR and BRR-like paths | BSRR and offset 0x28 (recorded mode only; enforcing refuses it) update the guard's ODR; neither reaches the stock port. `gpio-analogled`, `gpio-bsrr`, `gpio-exti` |
| A sub-word write | merged into the word from the guard's ODR copy: `gpio-odrhalf`; BSRR halves: `gpio-bsrrhalf` |
| LCKR | locks configuration only, not ODR, as silicon; its key sequence still refuses (`gpio-lckr`) |
| A reset that leaves a connection driven | `gpio-resetodr`: after `machine Reset` the pin is low, PB4 carries its reset pull-up again, and ODR is 0 |
| A mode change while a timer drives the pin | `gpio-afenter`, `gpio-afenterhigh`, `gpio-afout`, `gpio-afswitch` |
| Valid orders: ODR before MODER, MODER before ODR, AF → output → AF | `gpio-odrfirst`, `gpio-moderfirst`, `gpio-afout` |

**Found and fixed in the build (independent review):**
- **A port reset forgot an external sender's level** (a button held through `GPIOCRST`, p435). The level is outside the port, so the reset now keeps it (`gpio-button`).
- **A port with a refused configuration dropped a sender's edges.** It now drives no pin, so the sender's level reaches the pin.
- **Test gaps closed:** G20 would have survived (it now has `resetwrite`'s OSPEEDR write); the pull-down check could not fail; six new guards had no case; `resetodr` read the LED's own reset; `bit()` read a missing value as 0.

## 4. Findings, for the architect

1. **"Hold" was wrong for input and analog pins** (the reviewer's finding; hold was my recommendation, ruled 23:52Z). p539: the Schmitt trigger's output "is forced to a constant value (0)", and a pin nothing drives lights no LED on silicon. **Ruled 01:05Z and changed:** such a pin takes the declared level, then the pull, and is otherwise low. `gpio-analoghold` now shows GreenLED going dark. Hold remains only for an AF pin no Renode model drives (`gpio-afnosrc`). With the pin low, `IDR` for an input reads the pin, so the separate floating path, and its mutant, went. **Still a row:** an analog pin declared high is high to every observer, EXTI included, where p539 gives the on-chip side 0. Renode's single connection cannot separate the two.
2. **A sourceless AF pin takes a sender, a declared level or a pull, before holding.** That is ruling (2) applied to ruling (3). With the replay's declaration, PB6 (I2C1_SCL) is driven high on entering AF, not held.
3. **A complementary output (`CHxN`) rises on entering AF with its timer idle** (`gpio-afentern`), because Renode inverts CH1 and gates nothing on `CCxNE` (measure.md §1.2). Silicon does not drive the pin with `CC1NE` = 0. Neither firmware enters an inverted (pin, AF) pair. **Ruled 01:05Z: a row for the timer guard.**
4. **The timer sources are found once,** at the first AF evaluation, and only among numbered GPIO outputs. A route added later from the monitor still carries edges through the stock receiver, but entering AF reads the pull instead of that source's level. The pinned platform has no duplicate (pin, AF) route.
5. **New edges at a port's first access:** pins nobody drives take their reset pulls then (PA13, PA15 and PB4 high, PA14 low), and declared levels too. On ArduPilot that is PA15 → `timer2@00` and PB4 → `timer3@00`, both channels in output mode. Nothing changed in either firmware's run (§2).
6. **The teardown race (`1300` §5.5) is common on a loaded Furnace.** During this build the GPIO case runs lost end records in 2 of 20 cases, then 5 of 8, then 0 of 20. A loop of the `button` case's monitor script lost them in 2 of 6 runs on `main`'s guard and 1 of 6 on this one. A second loop on `main`'s guard, with the load average at 14–16, lost them in 0 of 8 runs of that script, 0 of 8 without the button presses, and 2 of 8 with `mach clear` before `quit`. It is not this change, and it fails closed. 1300a bounded it under 0.85% on a quieter host. **Ruled 01:05Z: a row;** it fails closed, and CI reruns are the remedy until it is placed.
7. **The witness cannot list the undeclared floating pins read in a run.** Ruling (1)(d) asked for that if it fit the optional witness fields. It does not: the witness is emitted at registration, before the run, and `Records.End()` carries no fields. **Ruled 01:05Z: a row for the records owner** (it needs `model/Refusal.cs`). The witness does carry `external`, so a consumer sees what was declared.
8. **Memory:** WO §1.2 and §7 say 1,048,576 KiB; Scotty's ruling of 2026-11-23 evening (roadmap L66) raised the ceiling to 1,572,864 KiB, to be judged by `TVI-WO-1201`'s budget once it lands. `1201` has not merged; the CI step still checks 1301's stricter limit, and this branch passes it (§6).

## 5. The reduction pass

Reduction pass (diff): −15 net · 1 duplicate removed (the planted firmware's two timer helpers, one for TIM1 and one for TIM2/TIM5, merged into `tim`) · 0 dead paths removed · 0 protected kept · 0 findings. Proven by the suite and the mutation run in §6, which ran on the reduced tree.

## 6. Suites, memory and CI

**After the 01:05Z change,** on Furnace: all 51 GPIO cases pass (`gpio-afodr` and `gpio-inputidr` lost end records once and passed on rerun). The diff-sized mutation run (H01–H21, G05, G06, G20; H06, H07 and H13 re-pointed) killed 24 of 24.

**Before it,** on `2d8eae6` (the reduced tree, before `main`'s docs-only `0d3b4af` was merged):
- **`tests/spike.sh`: 154 ok, 2 FAIL.** The two (`gpio-afentern`, `gpio-resetodr`) read every value right and lost end records (§4.6). Both passed on rerun.
- **`tests/adversarial.sh`:** all pass.
- **The mutation run, diff-sized:** H01–H21, G05, G06 and G20, each killed by its named case. 24 of 24. CI runs every row.
- **Memory:** §1.
- **CI:** see the PR.
