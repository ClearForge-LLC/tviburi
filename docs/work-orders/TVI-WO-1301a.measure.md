# TVI-WO-1301a — §1.1 measurement

**Builder:** Claude (Opus 5.5), Furnace, 2026-11-23. **Branch:** `wo/tvi-1301a-pin-drive-by-mode`, from `main` `e891211`.
**State:** predictions committed (§0); measuring.
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
