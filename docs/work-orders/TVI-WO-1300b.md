# TVI-WO-1300b — The RCC guard's open rows: a USART's kernel clock lost while it runs, the armed run's RCC records, and no default crystal

**Repo:** `ClearForge-LLC/tviburi` · **Base:** `main`, named in the kickoff.
**Branch:** `wo/tvi-1300b-rcc-rows`.
**Author:** Claude (co-architect) · **Builder:** strong tier, with a targeted adversarial pass.
**Phase:** 1, a refinement of `1300a`. Written 2026-11-25. **Cadence:** a short measurement, a stop, then the build.

**Grounds:**
- **Northstar** invariants 1, 5 and 6 (absence halts; nothing substitutes a default).
- **Architecture:** `RUL-07`, `RUL-09` (b), `RUL-17`, `RUL-18`.
- **Roadmap, Phase 1 rows** ("From `TVI-WO-1305`"):
  - "A kernel clock lost under a running USART is not refused: the RCC guard's `TVI-RCC-CLOCK` watches the SPI guards only. Reception stops, but transmission goes on."
  - "An armed Sentinel-03 run is not a pass on main's other guards: RCC `ADC12EN`/`ADC3EN` and their `LPEN` clears…"
- **`TVI-WO-1305a`'s finding** (ruled 2026-11-25): the RCC guard's constructor defaults `hseFrequency` to 8 MHz, so a platform that declares no crystal gets 8 MHz. The flash guard and the USART guard both read it.
- **The roadmap's RCC rows:** `rtc` is never locked, because the stock model reads `RCC_BDCR.RTCEN` as 1 at reset (`1300a` §1.1 M1, Q4).

> **What this is:**
> - **(1) Measure first, with predictions committed:**
>   - every RCC record in the armed Sentinel-03 run (`1305`'s material in `~/.cache/tvi-1305/armed`, read-only), the 14 generated boots and ArduPilot's fixture boot;
>   - which committed platforms (Tviburi's and Aethelgard's generated ones) declare `hseFrequency`, and which take the default;
>   - `RTCEN`'s reset value on the stock model against RM0433.
>   - **Stop** with the table.
> - **(2) `TVI-RCC-CLOCK` covers the USART guards.** A write after which a running USART's kernel clock (`UE` = 1) stops is refused, as it is for a running SPI. Use the USART guard's own source (`D2CCIP2R`) and the RCC guard's existing query; there is no second clock model.
> - **(3) The armed run's RCC records, each ruled by its page:**
>   - `ADC12EN` / `ADC3EN` enables are modeled: an enable is a field the guard already serves for other peripherals;
>   - `*LPEN` clears (`ADC12LPEN`, `ADC3LPEN`, `USART2LPEN`, `TIM1LPEN`) are acknowledged by name, since CSleep is not modeled (a roadmap row). The record says so; it is never a silent pass.
>   - Anything else (1) finds is `unmodeled` until ruled.
> - **(4) No default crystal.** A platform that declares no `hseFrequency`: any clock derived from the HSE is `unmodeled`, by name (invariant 6). A platform that declares one is unchanged.
>   - If (1) finds a committed platform that relies on the default, stop: it changes verdicts.
> - **(5) `RTCEN` at reset,** as RM0433 says, with its page. Then `rtc` is locked as silicon locks it. If the measured effect moves a committed boot, name it.
> - **Each rule comes with a planted firmware, and each test with a mutant it kills.**
>
> It is NOT:
> - the DMA guard's `CIRC` row (its own work order);
> - CSleep (`*LPEN` across `WFI`);
> - the I2C guard;
> - any change to Aethelgard.

## 1. Build

1. **Measure (1)** with predictions committed (`docs/work-orders/TVI-WO-1300b.measure.md`). **Stop** with the table and your proposal for each record.
2. **Build (2)–(5)** to the ruling.
3. **Replay:** the 14 generated boots and ArduPilot's fixture are identical to `main` except where a rule here changes a record, and each change is named. The armed run still arms.

## 2. Acceptance

- (2): a planted firmware that stops a running USART's kernel source refuses `TVI-RCC-CLOCK`; the same with `UE` = 0 passes.
- (3): the armed run's RCC records are each modeled, acknowledged by name, or ruled. None is left as an unexplained `TVI-RCC-FIELD`.
- (4): a planted platform with no `hseFrequency` and an HSE-rooted clock refuses `unmodeled`; one declaring 16 MHz computes from 16 MHz.
- (5): `RTCEN` reads silicon's reset value, with the page.
- Mutants for each rule are killed; CI is green; the budget is judged in CI.

## 3. Fences

- **Fence:**
  - `model/GuardedSTM32H7_RCC.cs`, and the USART guard only where (2) registers it;
  - `firmware/` (planted paths for this WO);
  - `tests/1300/` (or a new `tests/1300b/`);
  - your rows in `tests/mutants.tsv` and `tests/ci/times.tsv`;
  - `docs/work-orders/TVI-WO-1300b.*`.
- **Not:** any other guard's rules, `tools/`, the steering documents, or Aethelgard.
- **Running beside you:**
  - `TVI-WO-1305a` (the USART guard) is in review. Merge `main` after it lands; your (2) touches its registration.
  - Whichever merges second re-weighs its rows.
  - Seven shards hold about one more guard (`1202a` F2).
- One PR, left unmerged. Write `~/.cache/tvi-1300b/pr-title-body.md`.

## 5. Adversarial pass, targeted

- **A default that survives:** a code path that still reaches 8 MHz when nothing was declared.
- **An acknowledgement wider than the measurement:** an `LPEN` clear acknowledged on a peripheral the armed run never touched.
- **A clock-lost check that watches the wrong source.**

## 7. Flag-and-stop conditions

- **After the measurement** (the one planned stop).
- **A committed platform relies on the 8 MHz default.** Stop.
- **The armed run no longer arms.** Stop with the first refusal.
