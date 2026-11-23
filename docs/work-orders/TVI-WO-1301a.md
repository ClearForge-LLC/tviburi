# TVI-WO-1301a — A pin drives its connections as silicon does: from ODR only in output mode, from the peripheral in alternate-function mode

**Repo:** `ClearForge-LLC/tviburi` · **Base:** `main`, named in the kickoff.
**Branch:** `wo/tvi-1301a-pin-drive-by-mode`.
**Author:** Claude (co-architect) · **Builder:** strong tier, with a full adversarial pass.
**Phase:** 1, a refinement of `1301`. **Cadence:** a short measurement, a stop, then the build. **After `1301` merges.**

**Grounds:**
- **Northstar** invariants 1 and 2.
- **Architecture:** `RUL-07`, `RUL-16`, `RUL-17`, `RUL-18`.
- **`TVI-WO-1301`** FEEDBACK, F1 (the architect's ruling, 2026-11-23 19:47Z). The stock `STM32_GPIOPort` drives a pin's connections from `ODR` in every mode: LEDs, EXTI, timer inputs and `IDR`. Silicon drives a pin from `ODR` only in output mode: input mode has its "output buffer … disabled" (RM0433 Rev 9 p537), AF mode is "driven by the signals coming from the peripheral" (p538), and analog mode p539. Two cases are measured:
  - after reset, `BSRR` = 1 on GPIOB lights GreenLED with PB0 analog;
  - an `ODR` write on a timer-AF pin overrides the timer's level at the consumer's motor observers.
- The `1301` ledger row is `stock-wrong` today. This work order makes it modeled.

> **What this is:**
> - **(1) The drive rule:**
>   - In output mode (`MODER` 01), the pin and its connections follow `ODR`, as today.
>   - In AF mode (10), they follow the peripheral's signal. Where the stock timer drives the pin through Renode's AF routing, that signal wins and an `ODR` write does not reach the connections.
>   - In input (00) and analog (11) modes, `ODR` writes are latched in the register and reach no connection.
>   - Each case cites its page.
> - **(2) `IDR`:**
>   - In input mode, it reads the external level.
>   - In output mode, it reads the pin as driven.
>   - What it reads in analog mode is cited, or `unmodeled`.
> - **(3) Mode changes:** switching to output mode drives the latched `ODR` value at that moment. This is the common "set `ODR`, then switch to output" pattern, which must pass with the right level.
> - **(4) The consumer's observers:** the generated firmware's motor observers and ArduPilot's boot stay identical to `1301`'s runs, unless a run was relying on the defect. If one was, stop.
> - **Each rule comes with a planted firmware, and each test with a mutant it kills.** The ledger row moves from `stock-wrong` to modeled.
>
> It is NOT:
> - EXTI's own guard;
> - the peripheral-start "which signal" wiring (a row);
> - any change to Aethelgard.

## 1. Build

1. **Measure first.** Commit predictions, then record:
   - every path in the stock port that propagates `ODR` to a connection or to `IDR`;
   - Renode's AF routing for the timers (`invertedAFPins` and the 102 routes `1301` checked);
   - which consumer runs observe a pin that is not in output mode.

   **Stop** with them, and with whether any consumer run relies on the defect.
2. **Build (1)–(4).** Measure the one-file memory where `1301` ruled it is judged: in CI, with at least 5 boots, each under 1,048,576 KiB with at least 32 MiB to spare.

## 2. Acceptance

- **GreenLED stays dark** after a `BSRR` write on PB0 in analog mode. It lights after `MODER` = output.
- **A timer-AF motor pin's observer follows the timer**, not an `ODR` write.
- **"Set `ODR`, then output" drives the latched level** at the switch.
- **ArduPilot's boot and the generated firmware's observers** are unchanged, or the stop fired.
- **CI is green, with the memory step.**

## 3. Fences

- **Fence:** the GPIO guard and its data in `model/`, `tests/`, planted firmware in `firmware/`, and `docs/work-orders/TVI-WO-1301a.*`.
- **Not:** `model/Refusal.cs`, the RCC and SPI guards, `tools/` (beyond a ledger row's class), the steering documents, or Aethelgard.
- One PR, left unmerged. Write `~/.cache/tvi-1301a/pr-title-body.md`.

## 5. Adversarial pass, full

- **A route the rule misses:**
  - `BSRR` and `BRR`-like paths;
  - a sub-word write;
  - `LCKR`;
  - a reset that leaves a connection driven;
  - a mode change while a timer drives the pin.
- **A false refusal or a false level** on a valid order: `ODR` before `MODER`; `MODER` before `ODR`; AF to output and back.

## 7. Flag-and-stop conditions

- **After §1.1,** with the measurement.
- **A consumer run relies on the defect.** Stop with the run and the pin.
- **The one-file peak in CI** exceeds 1,048,576 KiB, or leaves less than 32 MiB. Stop.
