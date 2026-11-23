# TVI-WO-1301a — §1.1 measurement

**Builder:** Claude (Opus 5.5), Furnace, 2026-11-23. **Branch:** `wo/tvi-1301a-pin-drive-by-mode`, from `main` `e891211`.
**State:** §1.1 measured, then **stopped** (WO §1.1 and §7), with the questions in §4. Predictions committed at `0b7d9d5` before any run.
**Renode source read:** `~/.cache/aet-4202/renode-src/inf`. `STM32_GPIOPort.cs` matches `1301`'s pinned sha256 `16ffaed5…`; `BaseGPIOPort.cs`, `Core/GPIO.cs` and `STM32_Timer.cs` (`7e99f48c…`) are read from the same tree, which no hash pins to the build, so every claim drawn from them is measured below as well.
**RM0433:** Rev 9, `~/.cache/aet-held/rm0433-rev9.txt`, pages by `tests/1300/rmpage.sh`.

## 0. Predictions, committed before any measurement

Written after reading the WO, the kickoff, `1301`'s FEEDBACK and `rm0433.md` header, the GPIO guard, the stock `STM32_GPIOPort.cs`, `BaseGPIOPort.cs`, `GPIO.cs` and `STM32_Timer.cs`, the pinned `stm32h7.repl` and `nucleo_h753zi.repl`, and two consumer platforms from `1300`'s captures (ArduPilot's `sentinel-02-own`, generated `motors-pwm`), but before running anything or reading any write sequence.

**What silicon does (RM0433 Rev 9 §11.3.9–11.3.13):**
- input: "The output buffer is disabled"; "A read access to the input data register provides the I/O state" (p537);
- output: "A read access to the input data register gets the I/O state"; "A read access to the output data register gets the last written value" (p537);
- AF: "The output buffer is driven by the signals coming from the peripheral (transmitter enable and data)"; IDR "gets the I/O state" (p538);
- analog: "The output buffer is disabled"; the Schmitt trigger's output "is forced to a constant value (0)"; "Read access to the input data register gets the value "0"" (p539). EXTI: "the port must not be configured in analog mode" (§11.3.8 p536).

So analog-mode `IDR` is cited (0, p539), not `unmodeled`.

**M1. Every stock path that sets a pin's connection or its `IDR`.** The stock port keeps one array, `State`, and serves both `IDR` and `ODR` from it (`STM32_GPIOPort.cs` L196, L200). So every path below changes `IDR` and `ODR`'s read-back together, and none of them looks at `MODER`:
- P1.1 `ODR` write (0x14): `WriteState` drives all 16 pins, State and connection (L200, L130–139, L124–128).
- P1.2 `BSRR` low half (BS): `WriteState(State | val)` (L205). It reads State, not a latched `ODR`, so a pin an AF or an external source drove high is written back high.
- P1.3 `BSRR` high half (BR): `WriteState(State & ~val)` (L208).
- P1.4 offset 0x28 (RM0090's `BRR`): `WriteState(State & ~val)` (L281). The guard refuses it enforcing (`TVI-GPIO-FIELD`); recorded mode passes it.
- P1.5 the AF receiver: when `MODER` = AF and `AFSEL` matches, `GPIOAlternateFunction.OnGPIO` calls `WritePin` (L330–340), which also overwrites `State`, so `ODR` reads the peripheral's level afterwards.
- P1.6 an external sender into the port (`-> gpioPortC@13`, the Nucleo's button): `OnGPIO` sets State and the pin's own connection (L108–112), in every mode, so an output pin's `ODR` reads the external level.
- P1.7 `Reset`: every connection unset, State all 0 (`BaseGPIOPort.Reset` L77–87). No path re-drives a pin from `ODR` after reset.
- P1.8 **No** `MODER` write drives a pin. `ChangeMode` only connects or disconnects the AF receiver (L141–145). So "set `ODR`, then output" is right today only because P1.1 already drove the pin at the `ODR` write; AF → output leaves the pin at the peripheral's last level; output → AF leaves it at `ODR` until the peripheral's next edge.
- P1.9 `LCKR` locks `MODER`, `OSPEEDR`, `PUPDR` and `AFSEL` only (`GuardPinAction`), not `ODR`, as silicon.
- P1.10 `GPIO.Set` is a no-op when the level is unchanged (`Core/GPIO.cs` L47), so `WriteState`'s rewrite of all 16 pins signals only the pins that change.
- P1.11 The sub-word paths reach P1.1–P1.3 through the guard's merge, which reads `BSRR` as 0, so a half-word `BSRR` write is the whole write (as 1301 measured).
- **Planted probes** (monitor-driven, stock and guarded): PB0 analog, `BSRR` = 1 → GreenLED lit, `IDR` = 1, `ODR` = 1; PC13 input, `BSRR` = `0x2000` → `IDR13` = 1 and the SYSCFG/EXTI line 13 sees the edge; output → AF → output keeps the AF level; the button pressed on PC13 in output mode with `ODR` = 0 → `ODR` reads 1.

**M2. Renode's AF routing for the timers.**
- P2.1 102 timer → port routes in the pinned `stm32h7.repl` (L583–637), to each port's local receiver `#pin@af`, over TIM1–5, 8, 12–17, as `1301` counted. Each reaches the pin only when `MODER` = 10 and `AFSEL` = that AF (P1.5), so the AF leg already follows silicon's selection.
- P2.2 Inversion: the `invertedAFPins` lists on ports A, B, E, F, H, J, K (L105–183) give the complementary outputs (`CHxN`) as the inverted channel; `1301` F7's PA1 AF4 gap stands.
- P2.3 The reverse leg, pin → timer input (L639–727, about 60 routes), is a pin connection like any other, so P1.1–P1.3 drive a timer's input from `ODR` in every mode. The stock timer ignores it while the channel is an output (`STM32_Timer.cs` L494–498).
- P2.4 The stock timer drives its output connection whatever `CCER.CCxE` and `BDTR.MOE` say (tagged, Aethelgard's recorded gap). So "the peripheral's signal" in AF mode is the stock timer's ungated channel. That is outside this WO (a row for the timer guard).
- P2.5 The restatement carries all 102 routes onto the guards' variables (`1301` §4 measured every destination equal to stock).

**M3. Which consumer runs observe a pin that is not in output mode, and whether any relies on the defect.**
- P3.1 The observers: every pin → SYSCFG (EXTI through `EXTICR`) and → the timer inputs (stock); ArduPilot's PA0 → `spi1mux@0`, PA1 → `spi2mux@0`; the generated boards' chip selects, and `pa8obs`…`pa11obs` / `pe11obs`… on the motor pins.
- P3.2 **Generated firmware:** each chip select is written high before or after its `MODER` = output, and no SPI transfer happens before `MODER` = output, so no SPI transaction changes. The motor pins are set to AF1 for TIM1 and never written through `ODR`/`BSRR` while in AF mode, so the motor observers' lines are unchanged. **No generated run relies on the defect.**
- P3.3 **ArduPilot:** its board init writes `ODR` (`0xFFFF` on some ports) before `MODER`, as `1301` saw. Under the rule the connections wait for `MODER` = output; no SPI transfer happens in between, so its chip selects behave the same.
- P3.4 **The risk:** ArduPilot reads `IDR`. A pin in input mode whose `ODR` bit was written 1 reads 1 today and would read the external level under the rule. I predict at least one `IDR` read in ArduPilot's 5 s boot whose value differs between the two (a pin configured input after the port-wide `ODR` write). Whether that changes the boot (a branch taken on it) is the stop question; I predict it does not change the boot block or the console, but I hold this one loosely.
- P3.5 A floating input (no external sender, `PUPDR` none) has no level silicon defines, and the stock port models no pull. What `IDR` reads there under the rule (pull from `PUPDR`, 0, or `unmodeled`) is a question for the architect, not something this measurement decides.
- P3.6 No consumer run configures EXTI on a pin it also writes through `ODR`, so no EXTI interrupt changes.

## 1. Results, M1 and M2 (measured 2026-11-23, after §0 was committed at `0b7d9d5`)

**Image:** `aethelgard-tools@sha256:1e0ab105…`, the digest Aethelgard `main` `ee239949` pins (`container/IMAGE`, fetched 23:1xZ). Renode `1.17.0+20260907gitf1dd1b4af.aet1605`. One container at a time, under `~/bin/capped-run`, `nice -n 10`, `--network none`.
**Evidence:** `~/.cache/tvi-1301a/{m1,m3}` (scripts, monitor scripts, consoles), kept until the architect says.

### 1.1 M1: the stock paths, by probe

One monitor script (`m1/probe.body`), run on the **stock** Nucleo board and on **Tviburi's** board (this branch's release; every port guarded, enforcing, LQFP144), each with an LED observer on PA8 wired as the consumer wires its motor pins (`8 -> timer1@00 | pa8obs@0`). Each probe enables the ports' clocks (`AHB4ENR` = `0x7FF`; with `1300a`'s lock an unclocked access is dropped, which would make a dark LED vacuous) and reads `MODER` back. **The stock board and Tviburi's board gave the same value at 48 of the 49 readouts** (`m1/out2`). The one difference is `MODER` B after `machine Reset`: stock 0, the guard `0xFFFFFEBF`, silicon's reset (`1301` A5). So the defect is inherited unchanged by the guard. P14 (below) was run with the instrument of §2, whose shadow-only build gave stock's value at all 51 readouts except the same one (`m1/out3`).

| Probe | Readout (stock = guarded) | Silicon (RM0433 Rev 9) | Prediction |
|---|---|---|---|
| P1: PB0 analog (`MODER` reads `0xFFFFFEBF`), `BSRR` = 1 | GreenLED **lit**; `IDR` = 1; `ODR` = 1 | dark (p539, output buffer disabled); `IDR` 0 (p539); `ODR` 1 | P1.2 held |
| P2: then `MODER` PB0 = 01 | lit | lit | held |
| P3: output → analog with `ODR0` = 1 | lit; `IDR` 1 | not driven; `IDR` 0 | P1.8 held |
| P5: PC13 input, `EXTICR4` = port C, `RTSR1`/`C1IMR1` bit 13; `BSRR` = `0x2000` | `IDR` = `0x2000`; `ODR` = `0x2000`; **`C1PR1` = `0x2000` (EXTI13 fired)** | no edge (p537: output buffer disabled) | held |
| P6: PC13 output, `ODR13` = 0; the Nucleo button pressed (an external sender into the port) | **`ODR` reads `0x2000`**; `IDR` `0x2000` | `ODR` "gets the last written value" (p537): 0 | P1.6 held |
| P7–P8: PA8 AF1, TIM1 CH1 PWM, `CCR1` = 0 (output low); then `ODR` = `0x100` | `pa8obs` **True after the `ODR` write, and still True after 100 µs of running**: no timer edge comes to re-drive it | the pin follows TIM1 (p538): low | held; worse than predicted, the override lasts until the timer's next edge |
| P11: PA8 output, `ODR8` = 1, then AF with TIM1 stopped low | True, and True after 100 µs | follows TIM1: low | P1.8 held: entering AF does not take the peripheral's level |
| P12: GreenLED lit, `machine Reset` | dark; `ODR` 0 | dark | P1.7 held: no reset leaves a connection driven |
| P13: PB0 input, `BSRR` = 1; then PB0 analog | `IDR` 1, GreenLED lit; analog: `IDR` 1 | `IDR` the pin (floating, no pull: undefined); analog: 0 (p539) | held |

- **P1.4** (offset `0x28`): not re-probed; `1301` measured `0x1` there clearing `ODR0` (`TVI-WO-1301.rm0433.md` §2), and the guard refuses it enforcing.
- **P1.5** (an AF level overwrites `State`, so `ODR` reads it): held by source (L330–340, L126); no probe read `ODR` after a timer edge.
- **P1.9, P1.10:** by source. The shadow instrument (§2) confirms P1.10: a `WriteState` signals only the pins whose level changes.
- **The button's press is a time-domain event** (`Button.cs` `SetGPIO` → `HandleTimeDomainEvent`): it reaches the port only when emulation advances. A first P6 without a `RunFor` read 0 on both boards.
- **An observation beside the WO:** on the Nucleo board, every pin with a timer input route or a child (PB0 → GreenLED, PB1 → timers) has **no SYSCFG/EXTI route**: a later `->` for a source replaces the earlier one (as `1301` §3 saw for PB1). So EXTI cannot fire from those pins in any mode, from `ODR` or from a peripheral. A stock platform gap, not this WO's; a row.

### 1.2 M2: Renode's AF routing for the timers

| | Pinned `stm32h7.repl` (`b85cb4af…`) | `model/stm32h7-gpio-restate.repl` |
|---|---|---|
| timer → pin AF routes (`timerN: k -> gpioPortX#pin@af`) | 102 | 102, token-equal after the variable rename |
| pin → timer input routes (`gpioPortX: pin -> timerN@k`) | **102** | 102 |
| ports with `invertedAFPins` | 7 (A, B, E, F, H, J, K; L105–183) | the same 7 lists |

- **P2.1 held:** a timer reaches a pin only through the pin's AF receiver, which passes it only when `MODER` = 10 and `AFSEL` = that AF (L330). P7–P9 show TIM1 driving `pa8obs` through PA8 AF1.
- **P2.2 held;** `1301` F7 (PA1 AF4) stands.
- **P2.3 partly:** the reverse leg is 102 routes, not "about 60". Each is driven by `ODR` in every mode (P1.1–P1.3). The stock timer ignores an input on a channel in output mode (`STM32_Timer.cs` L494–498).
- **P2.4 wrong in part:** the stock timer **does** gate its output on `CCxE` (`CC1E` is a modeled flag, L306; P10: `CC1E` = 0 left `pa8obs` low with `CCR1` > `ARR`). It **ignores `MOE`** (a tag, L385; P14: `MOE` = 0, `CC1E` = 1, `CCR1` > `ARR` drove `pa8obs` high). `CCxNE` is a tag too (L308–316), so a complementary output (`CHxN`, the inverted pins) is never gated. "The peripheral's signal" in AF mode is therefore the stock timer's channel gated by `CCxE` and nothing else. Outside this WO: a row for the timer guard.
- **P2.5 held** (the table above; `1301` §4 measured every destination equal to stock).

## 2. Results, M3: which consumer runs observe a pin that is not in output mode — **the stop fires**

### 2.1 The instrument

A patched copy of this branch's guard (`m3/patch-shadow.py`, applied to a `git archive` export under `~/.cache/tvi-1301a/m3/src`, never to `model/`). The patch adds no rule. It keeps a latched `ODR` (silicon's, with `BSRR`'s BS priority) and, under the port's own name, logs:
- every passed write (`W`), as the control against the committed write tables;
- `DRIVE`: an `ODR`/`BSRR` write that changed a pin's level while the pin was not in output mode;
- `MODE`: each pin's mode change, with its level and latched `ODR`, and `SWITCH-MISMATCH` where a switch to output lands on a level other than the latched `ODR`;
- `IDR-READ`: each `IDR` read, with the stock value and the rule's under two candidates for a floating input (0; `PUPDR`'s pull);
- `ODR-READ` where the stock value differs from the latched one, `EXTERNAL` for a sender into the port, and each pin's observers (following `CombinedInput`).

With the property `shadowApply` (`idr`, `drive` or `all`) it also **applies** the rule as a prototype, so a boot can be diffed against the shadow-only boot. On the M1 probe it was checked both ways: shadow-only gave stock's readouts, and applied gave GreenLED dark at P1 and lit at P2, `pa8obs` low after P8's `ODR` write, and analog `IDR` 0. **Its two limits:** it restores a pin *after* the stock write, so an edge-triggered observer still sees a glitch (P5's EXTI fired in the applied run too, and a chip-select mux sees two extra edges); and it does not take the peripheral's level on entering AF (P11).

**The runs:** `tests/1301/replay-1301.sh`'s `accept` mode (copied as `m3/replay.sh`, `collapse-repeated-log-entries` off), every port recorded on LQFP144:
- ArduPilot's 5 s foreign boot: `1300`'s capture, ELF `a799dfb6…`, platform `3927f28a…`;
- 14 generated boots: `1300`'s captures of 12 boards (`sentinel-02-bench-hse8` included), and `1100`'s captures 16 and 18 for `driver-judges-nothing` and `d1-drv8876`, the boards `1300` did not capture.

**Controls:**
- The shadow-only ArduPilot boot executed `0x53EABA5` instructions, exactly `1301`'s recorded count, and 4,741 `AETHELGARD-CFG2` lines, as `1301`. A second shadow-only run was byte-identical (console minus timestamps), so the run is deterministic and the instrument does not perturb it.
- Every generated board's `BSRR` writes, in order and by value, equal `spike/1100/data/writes.tsv`'s: 11 boards with 1–16 writes each, `driver-judges-nothing` with none in either. `sentinel-02-bench-hse8` is newer than `1100` and has no row there. The static replay of `writes.tsv` (`m3/static-gen.py`) puts each diverging chip-select write at the same PC as the `DRIVE` lines.
- The motor observers' lines (`pa8obs` and on, e.g. 77 `LED state changed` lines on `motors-pwm`) are in the console, so "identical" below is measured, not vacuous.

### 2.2 What diverges, run by run

| Run | `DRIVE` (pin, mode, by) | `IDR` reads that differ | Switch-to-output mismatches | Applied boot vs shadow |
|---|---|---|---|---|
| ArduPilot | 176 at `gpio_init` (`ODR` = `0xFFFF` on all 11 ports while every pin is analog); 2 at `ChibiOS::SPIBus::stop_peripheral()` (`BSRR` clears SCK, PA5 and PB13, in AF mode) | **58 of 58**, all at PC `0x080F7D78` in **`ChibiOS::I2CBus::read_sda(unsigned char)`** (`nm` on the ELF): stock `0xFFFF`, the rule `0xE040` under either candidate | 0 | **changed** (§2.3) |
| 12 generated boards with chip selects | 1 or 2 each: the chip select set high by `BSRR` **one write before** its `MODER` = output, while analog (PA0, PA1, PA10, PA11, PB12, PD14; each observed by an `spiNmux@0` only) | none | 0 | only the prototype's own glitch: 3–7 mux lines at the chip select's first edge |
| `driver-judges-nothing` | 0 | none | 0 | identical |
| `d1-drv8876` | 0 | its one read (PA11, driven by an external sender) reads the same | 0 | identical |

- **No firmware writes EXTI or SYSCFG** (`spike/1150/data/writes-ap.tsv`, `spike/1100/data/writes.tsv`), and **no timer channel is an input** (every generated `CCMR` has `CCxS` = 00; ArduPilot writes only `timer5 CCMR1` = 0). So no EXTI line and no input capture consumes a pin's level in any consumer run.
- **The motor pins** (PA8–PA11, PE11–PE14) are never written through `ODR` or `BSRR` in AF mode. Every motor-observer line is identical between shadow and applied.
- **ArduPilot's other mode traffic does not diverge:** the SPI SCK pins toggle AF ↔ output at every SPI stop and start (PB13 2,353 times, PA5 16), always with level and latched `ODR` both 0. `gpio_init`'s chip-select window (`ODR`, `AFRL`, `AFRH`, `MODER`, at consecutive PCs) holds no SPI access.

### 2.3 The stop: ArduPilot relies on the defect at PB7 (`I2C1_SDA`)

- **The pin:** PB7, `I2C1_SDA`. The board's hwdef says "`PB7 I2C1_SDA I2C1 SPEED_VERYLOW OPENDRAIN FLOATING` … open-drain, pulled up off-chip" (`tests/1301/fixtures/aet-1c32c8ea-ardupilot/boards/sentinel-02.hwdef.dat` L25). The consumer's platform models no pull-up.
- **The run:** ArduPilot's 5 s foreign boot on `sentinel-02-own`. `read_sda` switches PB7 from AF to input (`PUPDR` 00), reads `IDR`, and switches it back, 58 times.
- **Stock:** PB7 reads **1**, only because `gpio_init`'s `ODR` = `0xFFFF` (written while PB7 was analog) is what `IDR` returns (P1.1, P13). On silicon it reads 1 because of the off-chip pull-up. **The stock twin is right by accident.**
- **Under the rule:** a floating input with no pull and no sender reads 0 under every candidate (0; `PUPDR`'s pull, which is none; and "hold", since nothing ever drove PB7). **Applied** (`idr`, `drive` and `all` give the same run, because each leaves PB7 at 0 when `read_sda` reads it):
  - ArduPilot takes `ChibiOS::I2CBus::clear_bus` after each read: **1,160 extra `GPIOB_ODR` writes** at PC `0x080F7D14`, inside `clear_bus` (20 SCL toggles × 58);
  - **1,828,656 fewer instructions** in the 5 s (`0x53EABA5` → `0x522C475`), and the final PC differs;
  - **`AETHELGARD-CFG2` lines 4,741 → 4,549**, ICM-42688 (`u5`) lines 2,355 → 2,259, BMP581 (`u7`) 487 → 483;
  - Aethelgard's register boot block, its flash hashes and the run's end are unchanged.
- **So WO §7 fires:** "A consumer run relies on the defect. Stop with the run and the pin." The run is ArduPilot's foreign boot; the pin is PB7. Building (2) as written would move ArduPilot onto a bus-recovery path silicon does not take, because the twin does not know about the board's pull-up.

## 3. Predictions, marked

| Prediction | Verdict |
|---|---|
| P1.1–P1.3, P1.6–P1.8, P1.10 | held (§1.1) |
| P1.4, P1.5, P1.9, P1.11 | held by source or by `1301`'s measurement; not re-probed |
| Planted probes (§0) | held; the button needs emulation to advance |
| P2.1, P2.2, P2.5 | held |
| P2.3 | **partly:** 102 input routes, not about 60 |
| P2.4 | **wrong in part:** `CCxE` gates the timer's output; only `MOE` (and `CCxNE`) are ignored |
| P3.1 | partly: the observers are the chip-select muxes, the LED observers and the timer inputs; EXTI is wired but never configured, and lost on any pin with a later `->` |
| P3.2, P3.3, P3.6 | held |
| P3.4 | **held in its first half, wrong in its second:** an `IDR` read differs (58 of 58), and **it changes the boot** (§2.3) |
| P3.5 | held as a question; every candidate gives 0 for PB7 |

## 4. For the architect

1. **The stop (§2.3).** A floating input has no level the twin can state, and ArduPilot's boot depends on PB7's off-board pull-up. Candidates:
   - **(a)** The consumer declares a board's external levels (here PB6 and PB7 pulled up) as a property of the guard (for example `externalPulls`), in its platform, never by name or environment (`RUL-10`). Tviburi's own replay harness can add it for its tests, as it already moves the consumer's port references, standing in for an Aethelgard Phase 2 row.
   - **(b)** An `IDR` read of a floating input bit (input, `PUPDR` 00, no sender, nothing declared) refuses `unmodeled`, naming the pin (invariant 1). Without (a), ArduPilot's boot refuses at `read_sda`.
   - **(c)** Keep the stock's `ODR` → `IDR` path for input pins as an acknowledged `stock-wrong` row, and model only (1), (3) and the analog read. The ledger row then stays partly `stock-wrong`.
   - **My recommendation: (a) with (b).** It answers silicon's level from data the board owns and refuses where none is given. ArduPilot then stays on its path only when the pull-up is declared, so WO §2's "ArduPilot's boot unchanged" needs the declaration in the replay. That is a ruling, not a build step.
2. **The level an undriven connection carries** (input or analog, nothing declared): low, hold, or the pull. **No consumer run consumes the difference** (§2.2: every window closes at the next write, with no SPI access in it). The prototype holds. I recommend hold, with a declared external level or pull winning when present.
3. **An AF pin with no Renode source.** Only the 102 timer routes drive pins; the SPI, USART and I2C models drive none. The rule's "the peripheral's signal" is then nothing. I propose: the connection holds, and an `IDR` read of such a pin is `unmodeled`. **No consumer run reads one.**
4. **Entering AF mode takes the peripheral's current level** (P11). The stock AF receiver keeps no level, and Renode's GPIO connections are one-way, so the guard would supply its own AF receivers (`ILocalGPIOReceiver`, re-implemented as `RUL-07` does the bus interfaces), each remembering its last level per AF. Ruled before the build, because it changes how the timers' 102 routes bind.
5. **The memory ceiling.** WO §1.2 and §7 say 1,048,576 KiB with 32 MiB to spare. The kickoff's goal and the roadmap (L66, Scotty 2026-11-23 evening) say 1,572,864 KiB, judged by `TVI-WO-1201`'s budget once it lands. I will build to the latter unless told otherwise.
6. **Rows, outside this WO:**
   - the stock timer drives its output with `MOE` = 0, and never gates `CHxN` (`CCxNE` is a tag) (P2.4);
   - on the stock platform, a pin with a later `->` (a timer input, an LED) loses its SYSCFG/EXTI route (§1.1; `1301` §3);
   - the consumer's platforms model no board pull-ups (PB6/PB7 on Sentinel-02; the chip selects).
