# TVI-WO-1302 — The advanced-control timer guard: a motor output drives only when silicon would

**Repo:** `ClearForge-LLC/tviburi` · **Base:** `main`, named in the kickoff.
**Branch:** `wo/tvi-1302-advanced-timer-guard`.
**Author:** Claude (co-architect) · **Builder:** strong tier, with a full adversarial pass.
**Phase:** 1, the third `13NN`. **Cadence:** a measurement, a stop, then the build. **After `1201` and `1301a` merge** (it is judged by `1201`'s memory budget, and its pins are driven through `1301a`'s AF path).

**Grounds:**
- **Northstar** invariants 1, 2, 5, 8; §6.
- **Architecture:** `RUL-04` and `RUL-13` as amended. After the RCC and GPIO, the order is "timers and DMA". Aethelgard's recorded gaps include timer `MOE` and `CCxP` as tags, the period off by one, and a fixed 250 MHz clock. `RUL-07`, `RUL-09` (b), `RUL-16`, `RUL-17`, `RUL-18`, `RUL-19` also apply.
- **`TVI-WO-1301a`'s measurement:** the stock timer drives its output with `MOE` = 0 and never gates `CHxN` (`CCxNE` is a tag). On silicon, an advanced-control timer's outputs are disabled while `BDTR.MOE` = 0 (RM0433 Rev 9, the TIM1/TIM8 output-control table: cite the page). So a firmware that never sets `MOE` spins no motor on silicon, while the twin's motor observers see pulses. That is invariant 1's false pass, on the path that flies.
- **`TVI-WO-1100`'s queue:** every motor board drives TIM1 (some also TIM2). ArduPilot does not arm in its 5 s window, so its TIM1/TIM2 rows mean "not reached", not "never" (`RUL-13`'s amendment).

> **What this is:**
> - **(1) Measure first.** Every TIM1 and TIM8 field (and TIM2–TIM5, where a motor board drives them) that the generated firmware and ArduPilot write. Record:
>   - the order of `CCMRx`, `CCER` (`CCxE`, `CCxP`, `CCxNE`, `CCxNP`), `BDTR` (`MOE`, `AOE`, `OSSR`, `OSSI`, `LOCK`, `DTG`), `CR1.CEN` and `EGR.UG`;
>   - what the stock model does with each, and which consumer observers each reaches;
>   - **ArduPilot armed:** whether a longer or armed window can be produced in recorded mode (the `OQ-03` row), and what it writes once armed.
> - **(2) The guard** over TIM1 (and TIM8 if written), with the form `RUL-07` allows for the stock class, measured as `1301` did. Its rules:
>   - **`predicted-fault`:** a channel output reaches a pin while silicon holds it disabled. That covers `MOE` = 0, `CCxE` = 0, and on the complementary output `CCxNE` = 0. Each state is cited by the output-control table's page, with `OSSR`/`OSSI` taking their idle states. The guard gates the output; when the firmware then expects pulses, it does not refuse on its own. The consumer's observers see silicon's level.
>   - **Polarity** (`CCxP`, `CCxNP`): served from copies and applied to the driven level, cited.
>   - **`LOCK`:** writes the lock level forbids are blocked or refused, per the page.
>   - **`unmodeled`:** every other field outside the allow-list (`RUL-17`), including dead time if no consumer needs it.
> - **(3) The period off by one, and the clock:** measure whether either reaches a consumer verdict now that `4240e` and Aethelgard's platform emit the solved timer clock. If either does, include it; if not, make it a row.
> - **Each refusal or gate comes with a planted firmware, and each test with a mutant it kills.**
>
> It is NOT:
> - DMA (DShot's circular transfers): the next `13NN`;
> - BOR;
> - the peripheral-start "which signal" wiring (a row);
> - any change to Aethelgard.

## 1. Build

1. **Measure first** (1), with predictions committed before the runs. **Stop** with:
   - the field table, and the fields to acknowledge;
   - each consumer observer the gate would change, by board, against silicon's expected level (cite);
   - the one-file memory, as `1201`'s budget reports it.
2. **Build (2)–(3)** to the ruling.
3. **Merge `main` before READY.**

## 2. Acceptance

- **A planted firmware** that configures TIM1 PWM with `MOE` left 0 drives no pulse at the pin or the motor observer, cited. With `MOE` = 1, it drives as today.
- **ArduPilot's boot, recorded,** shows no `would-refuse` under the timer outside the acknowledged table.
- **The 14 generated boots:** each motor board's observers are unchanged, or each difference is cited and named, as `1301a` did. A board whose generated firmware never sets `MOE` is a finding for Aethelgard, not a stop.
- **The budget:** within `1201`'s 32 MiB, under the 1,572,864 KiB ceiling.
- **CI is green.**

## 3. Fences

- **Fence:**
  - `model/`: the timer guard, its data and allow-list, and the restatement;
  - `scripts/prelude.resc`: one include;
  - `tools/tviburi-release`: the class row;
  - `tests/`, planted `firmware/`;
  - `docs/work-orders/TVI-WO-1302.*`.
- **Not:** `model/Refusal.cs` and the record grammar (a new field is a stop); the RCC, SPI and GPIO guards; the steering documents; Aethelgard.
- One PR, left unmerged. Write `~/.cache/tvi-1302/pr-title-body.md`.

## 5. Adversarial pass, full

- **An output that still reaches the pin:**
  - `AOE` setting `MOE` on update;
  - a break input clearing `MOE` (and the guard not seeing it);
  - `CCxE` written in a sub-word;
  - `LOCK` bypassed by a reset.
- **A gate that changes a consumer observer silicon would not change.**
- **A polarity applied twice** (the guard and the stock model both).

## 7. Flag-and-stop conditions

- **After §1.1,** with the measurement.
- **Either firmware's boot would refuse,** or an observer would change, on a path the manual says is valid. Stop with the order and the page.
- **The budget or the ceiling fails** in CI. Stop with the medians.
